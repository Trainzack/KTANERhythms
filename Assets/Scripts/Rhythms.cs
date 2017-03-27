using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Newtonsoft.Json;

public class Rhythms
	: MonoBehaviour
{
	#region Used In Editor
	public KMSelectable[] buttons;
	public SpriteRenderer blinkSprite;
	public Light blinkLight;
	public MeshRenderer blinkModel;
	public Material lightOnMaterial;
	public Material lightOffMaterial;

	public KMModSettings settings;

	public TextMesh colorblindText;
	#endregion
	[HideInInspector]
	public bool isSolved = false;//Not sure what this is used for (probably other modules), but I saw it in Chess.

	#region Settings Defined
	private bool colorBlindMode = false;
	private int DebugPattern = -1;
	private int DebugColor = -1;
	#endregion


	static int moduleNumber = 1;

	int thisModuleNumber;

	float lightIntensity = 0.5f;

	float flashLength = 0.12f;

	float buttonMashTime = 0.7f;

	#region Problem Decision
	int[][] patterns = new int[][] //1 = 16th note triplet, 2 = 8th trip, 3 = 8th note, 4 = 1/4 trip, 6 = 1/4 note, 12 = 1/2 note 
	{
		new int[] {6,2,2,2,	6,2,2,2},
		new int[] {12,6,3,3},
		new int[] {3,6,3,6,6},
		new int[] {6+3,3,6,2,2,2},
		new int[] {3,3,6,12},
		new int[] {6,12,2,2,2},
		new int[] {6},
	};

	int pattern;

	Color[] colors = new Color[]
	{
		new Color(53.0f/256,	46.0f/256,	233.0f/256), 	//blue
		new Color(256.0f/256,	46.0f/256,	0.0f/256), 		//red
		new Color(20.0f/256,	256.0f/256,	40.0f/256), 	//green
		new Color(256.0f/256,	200.0f/256,	0.0f/256), 		//yellow
	};

	int lightColor;

	int tempo = 95;
	#endregion

	#region Solutions
	//This is the first button press the player needs to make
	int[][] solutionsStep1 = new int[][] //Left is pattern, right is color
	{									//Number %4 = button to press, number /4 = instruction: 0: press, 1: hold 1 beep, 2: hold 2 beep, or number = -2: press repeatedly
		new int[] {8,-2,11,10},//Pattern 0
		new int[] {4,1,2,6},
		new int[] {6,5,0,3},
		new int[] {1,7,7,6},
		new int[] {5,3,4,3},
		new int[] {4,1,2,2},
		new int[] {0,2,3,1},
	};

	//This is the second button press the player needs to make
	int[][] solutionsStep2 = new int[][] //Left is pattern, right is color
	{									//Number %4 = button to press, number /4 = instruction: 0: press, 1: hold 1 beep, 2: hold 2 beep, or number = -1: nothing,pass automatically
		new int[] {1,-1,1,1},//Pattern 0
		new int[] {3,0,5,7},
		new int[] {1,6,3,7},
		new int[] {2,0,3,1},
		new int[] {2,2,1,6},
		new int[] {7,5,1,4},
		new int[] {5,6,7,1},
	};

	int correctButton;

	int correctAction;
	#endregion

	#region Used In Module
	bool active = false;

	bool lightsBlinking = false;

	int step;

	int beepsPlayed = 0;

	int currentPressId = 0;

	int selectedButton = 0;

	bool buttonIsPhysicallyHeld = false;

	bool buttonIsMetaphoricallyHeld = false;

	KMAudio.KMAudioRef audioRefBeep;

	#endregion

	#region Used for *** action
	int timesPressed = 0;

	int timesNeeded;

	float lastTimePressed;

	#endregion

	void Start()
	{
		Init();
	}

	string[] labels = new string[] { "♩", "♪", "♫", "♬" };

	string[] colorNames = new string[] {"blue","red","green","yellow"};

	void Init()
	{
		lightOff ();
		blinkLight.range = 0.1f;

		thisModuleNumber = moduleNumber++;

		loadSettings ();
		if (!colorBlindMode) {Destroy (colorblindText);};
		SetColorblindText ("");

		GetComponent<KMBombModule>().OnActivate += OnActivate;


		for (int i = buttons.Length - 1; i > 0; i--) {//Shuffle the buttons array, stolen from the unity forums
			int r = Random.Range (0, i);
			KMSelectable tmp = buttons [i];
			buttons [i] = buttons [r];
			buttons [r] = tmp;
		}

		for (int i = 0; i < buttons.Length; i++)
		{
			string label = labels [i];

			TextMesh buttonText = buttons[i].GetComponentInChildren<TextMesh>();
			buttonText.text = label;
			int j = i;
			buttons[i].OnInteract += delegate () { ; buttons[j].AddInteractionPunch(0.2f); OnPress(j); return false; };
			buttons[i].OnInteractEnded += OnRelease;
		}

		/**
		for (int i = 0; i < patterns.Length; i++) {//This is debug code to ensure that all patterns are the propper length
			int sum = 0;
			foreach (int d in patterns[i]) {
				sum += d;
			}
			Debug.Log ("Pattern " + i + ": " + sum);
		}*/

	}

	void OnActivate()
	{
		int litIndicators = 0;
		List<string> indicators = GetComponent<KMBombInfo> ().QueryWidgets (KMBombInfo.QUERYKEY_GET_INDICATOR, null);
		foreach (string response in indicators) {
			Dictionary<string, string> responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(response);
			if (responseDict ["on"] == "True") {
				litIndicators++;
				for (int i = 0; i < solutionsStep1.Length; i ++) {//If there is a lit indicator on the bomb and the color is yellow (3), then hold the buttons for one additional beep per lit indicator
					solutionsStep1 [i] [3] += 4;
					solutionsStep2 [i] [3] += 4;
				}
			};
		}

		//LogMessage (ManualGen.getManual ());

		string message = "Detected " + litIndicators + " lit indicator(s)";



		int batteryCount = 0;
		List<string> responses = GetComponent<KMBombInfo>().QueryWidgets(KMBombInfo.QUERYKEY_GET_BATTERIES, null);
		foreach (string response in responses)
		{
			Dictionary<string, int> responseDict = JsonConvert.DeserializeObject<Dictionary<string, int>>(response);
			batteryCount += responseDict["numbatteries"];
		}
			

		if (batteryCount > 1) { //If there is more than one batter on the bomb and the rythm is quarter notes, repeat 1st instruction
			message += " and more than one battery.";
			for (int i = 0; i < solutionsStep1 [6].Length; i++) {
				solutionsStep2 [6] [i] = solutionsStep1 [6] [i];
			}
		} else {
			message += " and one or no batteries.";
		}

		LogMessage (message);

		lightOn();
		active = true;
		SetPattern();
	}

	void SetCorrect()
	{
		int[][] solutionTable = (step == 1) ? solutionsStep1 : solutionsStep2;
		int solution = solutionTable [pattern] [lightColor];
		if (solution == -1) {
			Pass ();
		} else if (solution == -2) {
			timesPressed = 0;
			timesNeeded = Random.Range (15, 20);
			correctButton = 1;
			correctAction = -2;
			LogMessage ("Correct action: mash any button " + timesNeeded + " times");
		} else {
			correctButton = solution % 4;
			correctAction = solution / 4;
			LogMessage ("Correct action for stage " + step + ": press the button labled " + labels[correctButton] + " for " + correctAction + " beep(s)");
		}
		SetColorblindText (colorNames[lightColor]);
	}

	void SetPattern() 
	{

		pattern = Random.Range (0, patterns.Length);
		lightColor = Random.Range (0, colors.Length);
		if (DebugPattern > -1) {
			pattern = DebugPattern;
		}
		if (DebugColor > -1) {
			lightColor = DebugColor;
		}

		blinkLight.color = colors[lightColor];
		blinkSprite.color = colors[lightColor];

		tempo += Random.Range (1, 7);//Pacing, and prevent nearby patterns matching each other.

		LogMessage ("Selected pattern number " + (pattern + 1) + " and a " + colorNames[lightColor]+ " light");

		step = 1;
		SetCorrect ();



		StartCoroutine (RunPattern (patterns[pattern]));
	}

	IEnumerator RunPattern(int[] pattern) 
	{
		lightsBlinking = true;		
		while (this.lightsBlinking & this.active) {
			for (int i = 0; i < pattern.Length & lightsBlinking & active; i++) {
				if (!this.lightsBlinking | !this.active) {
					break;//Stop trying to flash if we aren't flashing.
				}
				lightOn ();
				yield return new WaitForSecondsRealtime ((pattern [i] * 10.0f / tempo) - (flashLength));
				//lightOff ();
				StartCoroutine(slowLightOff(flashLength));
				yield return new WaitForSecondsRealtime (flashLength);
				continue;
			}
		}

	}

	void OnPress(int button)
	{
		buttonIsPhysicallyHeld = true;
		selectedButton = button;
		StartCoroutine (MoveButton (true, button));

		if (active) {//Buttons that are pressed before bomb is active don't do anything
			GetComponent<KMAudio> ().PlayGameSoundAtTransform (KMSoundOverride.SoundEffect.BigButtonPress, transform);
			buttonIsMetaphoricallyHeld = true;
			if (correctAction == -2) {
				if (timesPressed == 0) {
					lastTimePressed = Time.time;
				}
			} else {
				StartCoroutine (beepCount ());
			}
		} else {
			LogMessage ("Ignoring press as module is not currently active.");
		}
	}

	/**
	 * press: Whether to press in (true) or release out (false)
	 **/
	IEnumerator MoveButton (bool press, int button) {//This actually moves the physical button.
		Transform t = buttons [button].GetComponent<Transform> ();
		float translateAmount = 0.0009f;
		if (press) {
			translateAmount *= -1;
		}
		for (int i = 0; i < 5; i++) {
			t.Translate(new Vector3 (0,translateAmount));
			yield return new WaitForEndOfFrame ();
		}
		//Debug.Log ("Button moved");
	}


	void OnRelease()
	{
		stopBeep ();
		if (buttonIsPhysicallyHeld) {
			StartCoroutine(MoveButton(false, selectedButton));
			GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
			buttonIsPhysicallyHeld = false;
		}
		if (buttonIsMetaphoricallyHeld) {//No releasing buttons when they aren't held

			buttonIsMetaphoricallyHeld = false;
			string message = "Button labeled " + labels [selectedButton] + " pressed and released";
			if (correctAction == -2) {//For RAPID BUTTON PRESSES
				timesPressed++;
				message += ", pressed " + timesPressed + "/" + timesNeeded + " times";
				if (timesNeeded <= timesPressed ){ //GetComponent<KMBombInfo> ().GetTime() < 1.5f) {
					message += ", module has been passed!";
					LogMessage (message);
					Pass ();
				} else if (Time.time - lastTimePressed > buttonMashTime) {
					message += ", but this release was too late! The delay was " + (Time.time - lastTimePressed) + ", when it should be less than " + buttonMashTime;
					LogMessage (message);
					StartCoroutine( Strike ()); //Can't let them wait too long
				}
				lastTimePressed = Time.time;
			} else {//For REGULAR BUTTON PRESSES
				message += " at " + beepsPlayed + " beeps";
				if (selectedButton == correctButton && beepsPlayed == correctAction) {
					message += " (correct)";
					if (step == 1) {
						message += ", moving on to stage 2.";
						step = 2;
						LogMessage (message);
						SetCorrect ();
					} else {
						message += ", module has been passed!";
						LogMessage (message);
						Pass ();
					}
				} else {
					message += " (incorrect)";

					LogMessage (message);
					StartCoroutine (Strike ());
				}

			}
		} else {
			//The only way to get here is if you press the button before the bomb is active.
			LogMessage("Ignoring improper release");
		}
	}

	IEnumerator beepCount() {
		beepsPlayed = 0;
		currentPressId++;
		int thisPressId = currentPressId;
		yield return new WaitForSeconds (0.4f);
		while (thisPressId == currentPressId & buttonIsMetaphoricallyHeld) {
			//If thisPressId != currentPressId, then another instance of this method is active.
			beepsPlayed++;
			//LogMessage ("Beep: " + beepsPlayed + " PressID: " + thisPressId);
			stopBeep ();
			audioRefBeep = GetComponent<KMAudio>().PlaySoundAtTransformWithRef("HoldChirp", transform);
			yield return new WaitForSeconds (1.1f); //This value fine-tuned with the help of Rexkix

		}
	}

	void stopBeep() {
		if (audioRefBeep != null && audioRefBeep.StopSound != null) {
			//LogMessage ("Halting beep sound!");
			audioRefBeep.StopSound ();
		}
	}

	//PASS/FAIL

	void Pass() {
		GetComponent<KMBombModule>().HandlePass();
		active = false;
		lightsBlinking = false;
		lightOff ();
		isSolved = true;
		SetColorblindText ("");
		stopBeep ();
	}

	IEnumerator Strike() {
		lightsBlinking = false;
		active = false;
		//buttonIsMetaphoricallyHeld = false;
		lightOff ();
		SetColorblindText ("");
		GetComponent<KMBombModule> ().HandleStrike ();
		stopBeep ();
		LogMessage ("Gave strike #" + GetComponent<KMBombInfo> ().GetStrikes());
		yield return new WaitForSecondsRealtime (1.5f);
		active = true;
		SetPattern ();
	}



	//LIGHT CONTROL


	void lightOn()
	{
		blinkLight.enabled = true;
		blinkLight.intensity = lightIntensity;
		blinkSprite.enabled = true;
		Color color = blinkSprite.color;
		color.a = 1.0f;
		blinkSprite.color = color;
		blinkModel.material.SetColor(0, colors [lightColor]); 
		blinkModel.material = lightOnMaterial;
	}

	IEnumerator slowLightOff(float time) 
	{
		time  *= (2.0f/3.0f); //Solves a race condition that can result in the lights not turning back on (Is this only in the unity editor?)

		blinkModel.material = lightOffMaterial;
		while (blinkLight.intensity > 0) {
			blinkLight.intensity -= (Time.deltaTime * lightIntensity) / (time);
			Color color = blinkSprite.color;
			color.a -= Time.deltaTime / time;
			blinkSprite.color = color;
			yield return null;
		}
	}

	void lightOff()
	{
		blinkLight.enabled = false;
		blinkSprite.enabled = false;
		blinkModel.material.SetColor (0,new Color (0, 0, 0));
		blinkModel.material = lightOffMaterial;
	}

	void LogMessage(string message) {
		Debug.Log ("[Rhythms #" + thisModuleNumber +"] " + message);
	}

	void SetColorblindText(string text) {
		if (colorBlindMode) {
			colorblindText.text = text;
		}
	}

	void loadSettings() {

		try {
			RhythmsSettings modSettings = JsonConvert.DeserializeObject<RhythmsSettings> (settings.Settings);
			if (modSettings != null) {
				colorBlindMode = modSettings.GetColorBlindMode ();
				int _DebugPattern = modSettings.GetDebugModePattern () - 1;
				int _DebugColor = modSettings.GetDebugModeColor () - 1;
				if (_DebugPattern >= 0 & _DebugPattern < patterns.Length) {
					DebugPattern = _DebugPattern;
				}
				if (_DebugColor >= 0 & _DebugColor < colors.Length) {
					DebugColor = _DebugColor;
				}
			} else {
				LogMessage ("Could not read settings file!");
			}
		} catch (JsonReaderException e) {
			LogMessage ("Malformed settings file! " + e.Message);
		}
				
	}

}

