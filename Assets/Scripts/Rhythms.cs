using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

public class Rhythms : MonoBehaviour
{
    public KMBombModule module;
    public KMSelectable[] buttons;
    public SpriteRenderer blinkSprite;
    public MeshRenderer blinkModel;
    public Material lightOnMaterial;
    public Material lightOffMaterial;
    public TextMesh colorblindText;
    public KMColorblindMode ColorblindMode;

    static int _moduleIdCounter = 1;
    int _moduleId;

    const float lightIntensity = 0.5f;
    const float flashLength = 0.12f;
    const float beepLength = 1.1f;
    const float preBeepPause = 0.4f;
    const float buttonMashTime = 0.7f;

    private static T[] newArray<T>(params T[] array) { return array; }

    // 1 = 16th note triplet, 2 = 8th triplet, 3 = 8th note, 4 = 1/4 triplet, 6 = 1/4 note, 9 = dotted 1/4 note, 12 = 1/2 note
    private static readonly int[][] _rhythms = newArray(
        new int[] { 6, 2, 2, 2, 6, 2, 2, 2 },
        new int[] { 12, 6, 3, 3 },
        new int[] { 3, 6, 3, 6, 6 },
        new int[] { 9, 3, 6, 2, 2, 2 },
        new int[] { 3, 3, 6, 12 },
        new int[] { 6, 12, 2, 2, 2 },
        new int[] { 6 });

    private static readonly Color[] _colors = newArray(
        new Color(53.0f / 256, 46.0f / 256, 233.0f / 256),      // blue
        new Color(256.0f / 256, 46.0f / 256, 0.0f / 256),       // red
        new Color(20.0f / 256, 256.0f / 256, 40.0f / 256),      // green
        new Color(256.0f / 256, 200.0f / 256, 0.0f / 256));     // yellow

    // This is the first button press the player needs to make
    // _solutionStep1[rhythm][color]
    // x % 4 = button to press; x / 4 = instruction: 0=press, 1=hold 1 beep, etc.; -1=nothing (pass automatically); -2=“mash buttons”
    private static readonly int[][] _solutionStep1 = newArray(
        new int[] { 8, -2, 11, 10 },
        new int[] { 4, 1, 2, 6 },
        new int[] { 6, 5, 0, 3 },
        new int[] { 1, 7, 7, 6 },
        new int[] { 5, 3, 4, 3 },
        new int[] { 4, 1, 2, 2 },
        new int[] { 0, 2, 3, 1 });
    private static readonly int[][] _solutionStep2 = newArray(
        new int[] { 1, -1, 1, 1 },
        new int[] { 3, 0, 5, 7 },
        new int[] { 1, 6, 3, 7 },
        new int[] { 2, 0, 3, 1 },
        new int[] { 2, 2, 1, 6 },
        new int[] { 7, 5, 1, 4 },
        new int[] { 5, 6, 7, 1 });
    private static readonly string[] _labels = new string[] { "♩", "♪", "♫", "♬" };
    private static readonly string[] _colorNames = new string[] { "blue", "red", "green", "yellow" };

    private bool _colorblind = false;
    private int _rhythm;
    private int _lightColor;
    private int _correctButton;
    private int _correctAction;
    private int _tempo = 95;    // This will be varied per module, and also slightly speed up after strikes
    private bool _active = false;
    private bool _lightsBlinking = false;
    private int _step;
    private int _beepsPlayed = 0;
    private int _currentPressId = 0;
    private int _selectedButton = 0;
    private bool _buttonIsPhysicallyHeld = false;
    private bool _buttonIsMetaphoricallyHeld = false;
    KMAudio.KMAudioRef _beepAudio;
    KMSelectable[] _twitchPlaysButtons;

    // Used for the “mash buttons” action
    private int _timesPressed = 0;
    private int _timesNeeded = 0;
    private float _lastTimePressed;

    void Start()
    {
        lightOff();

        _moduleId = _moduleIdCounter++;
        _colorblind = ColorblindMode.ColorblindModeActive;

        module.OnActivate += OnActivate;

        // Used for Twitch Plays TL/TR/BL/BR notation.
        _twitchPlaysButtons = new[] { buttons[0], buttons[1], buttons[2], buttons[3] };

        // Shuffle the buttons array
        for (int i = buttons.Length - 1; i > 0; i--)
        {
            int r = Random.Range(0, i);
            var tmp = buttons[i];
            buttons[i] = buttons[r];
            buttons[r] = tmp;
        }

        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].GetComponentInChildren<TextMesh>().text = _labels[i];
            int j = i;
            buttons[i].OnInteract += delegate () { buttons[j].AddInteractionPunch(0.2f); OnPress(j); return false; };
            buttons[i].OnInteractEnded += OnRelease;
        }
    }

    void OnActivate()
    {
        int litIndicators = 0;
        List<string> indicators = GetComponent<KMBombInfo>().QueryWidgets(KMBombInfo.QUERYKEY_GET_INDICATOR, null);
        foreach (string response in indicators)
        {
            Dictionary<string, string> responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(response);
            if (responseDict["on"] == "True")
            {
                litIndicators++;
                for (int i = 0; i < _solutionStep1.Length; i++)
                {//If there is a lit indicator on the bomb and the color is yellow (3), then hold the buttons for one additional beep per lit indicator
                    _solutionStep1[i][3] += 4;
                    _solutionStep2[i][3] += 4;
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


        if (batteryCount > 1)
        { //If there is more than one batter on the bomb and the rythm is quarter notes, repeat 1st instruction
            message += " and more than one battery.";
            for (int i = 0; i < _solutionStep1[6].Length; i++)
            {
                _solutionStep2[6][i] = _solutionStep1[6][i];
            }
        }
        else
        {
            message += " and one or no batteries.";
        }

        LogMessage(message);

        lightOn();
        _active = true;
        SetPattern();
    }

    void SetCorrect()
    {
        int[][] solutionTable = (_step == 1) ? _solutionStep1 : _solutionStep2;
        int solution = solutionTable[_rhythm][_lightColor];
        if (solution == -1)
        {
            Pass();
        }
        else if (solution == -2)
        {
            _timesPressed = 0;
            _timesNeeded = Random.Range(15, 20);
            _correctButton = 1;
            _correctAction = -2;
            LogMessage("Correct action: mash any button " + _timesNeeded + " times");
        }
        else
        {
            _correctButton = solution % 4;
            _correctAction = solution / 4;
            LogMessage("Correct action for stage " + _step + ": press the button labled " + _labels[_correctButton] + " for " + _correctAction + " beep(s)");
        }
        SetColorblindText();
    }

    void SetPattern()
    {
        _rhythm = Random.Range(0, _rhythms.Length);
        _lightColor = Random.Range(0, _colors.Length);
        blinkSprite.color = _colors[_lightColor];

        _tempo += Random.Range(1, 7);//Pacing, and prevent nearby patterns matching each other.

        LogMessage("Selected pattern number " + (_rhythm + 1) + " and a " + _colorNames[_lightColor] + " light");

        _step = 1;
        SetCorrect();



        StartCoroutine(RunPattern(_rhythms[_rhythm]));
    }

    IEnumerator RunPattern(int[] pattern)
    {
        _lightsBlinking = true;
        SetColorblindText();
        while (_lightsBlinking && _active)
        {
            for (int i = 0; i < pattern.Length && _lightsBlinking && _active; i++)
            {
                if (!_lightsBlinking || !_active)
                    break;

                lightOn();
                yield return new WaitForSecondsRealtime((pattern[i] * 10.0f / _tempo) - (flashLength));
                //lightOff ();
                StartCoroutine(slowLightOff(flashLength));
                yield return new WaitForSecondsRealtime(flashLength);
                continue;
            }
        }
    }

    void OnPress(int button)
    {
        _buttonIsPhysicallyHeld = true;
        _selectedButton = button;
        StartCoroutine(MoveButton(true, button));

        if (_active)
        {
            GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
            _buttonIsMetaphoricallyHeld = true;
            if (_correctAction == -2)
            {
                if (_timesPressed == 0)
                {
                    _lastTimePressed = Time.time;
                }
            }
            else
            {
                StartCoroutine(beepCount());
            }
        }
        else
        {
            LogMessage("Ignoring press as module is not currently active.");
        }
    }

    /**
     * press: Whether to press in (true) or release out (false)
     **/
    IEnumerator MoveButton(bool press, int button)
    {//This actually moves the physical button.
        Transform t = buttons[button].GetComponent<Transform>();
        float translateAmount = 0.0009f;
        if (press)
        {
            translateAmount *= -1;
        }
        for (int i = 0; i < 5; i++)
        {
            t.Translate(new Vector3(0, translateAmount));
            yield return new WaitForEndOfFrame();
        }
        //Debug.Log ("Button moved");
    }


    void OnRelease()
    {
        stopBeep();
        if (_buttonIsPhysicallyHeld)
        {
            StartCoroutine(MoveButton(false, _selectedButton));
            GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
            _buttonIsPhysicallyHeld = false;
        }
        if (_buttonIsMetaphoricallyHeld)
        {//No releasing buttons when they aren't held

            _buttonIsMetaphoricallyHeld = false;
            string message = "Button labeled " + _labels[_selectedButton] + " pressed and released";
            if (_correctAction == -2)
            {//For RAPID BUTTON PRESSES
                _timesPressed++;
                message += ", pressed " + _timesPressed + "/" + _timesNeeded + " times";
                if (_timesNeeded <= _timesPressed)
                { //GetComponent<KMBombInfo> ().GetTime() < 1.5f) {
                    message += ", module has been passed!";
                    LogMessage(message);
                    Pass();
                }
                else if (Time.time - _lastTimePressed > buttonMashTime)
                {
                    message += ", but this release was too late! The delay was " + (Time.time - _lastTimePressed) + ", when it should be less than " + buttonMashTime;
                    LogMessage(message);
                    StartCoroutine(Strike()); //Can't let them wait too long
                }
                _lastTimePressed = Time.time;
            }
            else
            {//For REGULAR BUTTON PRESSES
                message += " at " + _beepsPlayed + " beeps";
                if (_selectedButton == _correctButton && _beepsPlayed == _correctAction)
                {
                    message += " (correct)";
                    if (_step == 1)
                    {
                        message += ", moving on to stage 2.";
                        _step = 2;
                        LogMessage(message);
                        SetCorrect();
                    }
                    else
                    {
                        message += ", module has been passed!";
                        LogMessage(message);
                        Pass();
                    }
                }
                else
                {
                    message += " (incorrect)";

                    LogMessage(message);
                    StartCoroutine(Strike());
                }

            }
        }
        else
        {
            //The only way to get here is if you press the button before the bomb is active.
            LogMessage("Ignoring improper release");
        }
    }

    IEnumerator beepCount()
    {
        _beepsPlayed = 0;
        _currentPressId++;
        int thisPressId = _currentPressId;
        yield return new WaitForSeconds(preBeepPause);
        while (thisPressId == _currentPressId & _buttonIsMetaphoricallyHeld)
        {
            //If thisPressId != currentPressId, then another instance of this method is active.
            _beepsPlayed++;
            //LogMessage ("Beep: " + beepsPlayed + " PressID: " + thisPressId);
            stopBeep();
            _beepAudio = GetComponent<KMAudio>().PlaySoundAtTransformWithRef("HoldChirp", transform);
            yield return new WaitForSeconds(beepLength);

        }
    }

    void stopBeep()
    {
        if (_beepAudio != null && _beepAudio.StopSound != null)
        {
            //LogMessage ("Halting beep sound!");
            _beepAudio.StopSound();
        }
    }

    //PASS/FAIL

    void Pass()
    {
        GetComponent<KMBombModule>().HandlePass();
        _active = false;
        _lightsBlinking = false;
        lightOff();
        SetColorblindText();
        stopBeep();
    }

    IEnumerator Strike()
    {
        _lightsBlinking = false;
        _active = false;
        //buttonIsMetaphoricallyHeld = false;
        lightOff();
        SetColorblindText();
        LogMessage("Giving strike #" + (GetComponent<KMBombInfo>().GetStrikes() + 1));
        GetComponent<KMBombModule>().HandleStrike();
        stopBeep();
        yield return new WaitForSecondsRealtime(1.5f);
        _active = true;
        SetPattern();
    }

    //LIGHT CONTROL

    void lightOn()
    {
        blinkSprite.enabled = true;
        Color color = blinkSprite.color;
        color.a = 1.0f;
        blinkSprite.color = color;
        blinkModel.material.SetColor(0, _colors[_lightColor]);
        blinkModel.material = lightOnMaterial;
    }

    IEnumerator slowLightOff(float time)
    {
        time *= (2.0f / 3.0f); //Solves a race condition that can result in the lights not turning back on (Is this only in the unity editor?)

        blinkModel.material = lightOffMaterial;
        var duration = .35f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += (Time.deltaTime * lightIntensity) / (time);
            Color color = blinkSprite.color;
            color.a -= Time.deltaTime / time;
            blinkSprite.color = color;
            yield return null;
        }
    }

    void lightOff()
    {
        blinkSprite.enabled = false;
        blinkModel.material.SetColor(0, new Color(0, 0, 0));
        blinkModel.material = lightOffMaterial;
    }

    void LogMessage(string message)
    {
        Debug.Log("[Rhythms #" + _moduleId + "] " + message);
    }

    void SetColorblindText()
    {
        colorblindText.text = _colorNames[_lightColor];
        colorblindText.gameObject.SetActive(_colorblind && _lightsBlinking);
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} press ♩ [valid keys: ♩, ♪, ♫, ♬] | !{0} press tl [valid positions: tl, tr, bl, br, or 1–4] | !{0} press ♩ 2 [hold button for 2 beats] | !{0} mash | !{0} colorblind";
#pragma warning restore 0414

    public IEnumerator ProcessTwitchCommand(string command)
    {
        if (Regex.IsMatch(command, @"^\s*colorblind\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            _colorblind = true;
            SetColorblindText();
            yield return null;
            yield break;
        }

        if (Regex.IsMatch(command, @"^\s*mash\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            LogMessage("Mashing buttons!");
            yield return null;
            do
                yield return new[] { buttons[Random.Range(0, 4)] };
            while (_timesPressed < _timesNeeded);
            yield break;
        }

        var modulesMatch = Regex.Match(command, @"^\s*((press|hold)\s+)?(♩|♪|♫|♬|tl|tr|bl|br|lt|rt|lb|rb|[1-4])(\s+(for\s+)?([0-9]+))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        const int buttonGroup = 3;
        const int durationGroup = 6;
        if (!modulesMatch.Success)
        {
            LogMessage("Invalid Twitch command \"" + command + "\".");
            yield break;
        }

        KMSelectable selectedButton = null;
        string buttonName = modulesMatch.Groups[buttonGroup].Value;
        switch (buttonName)
        {
            case "tl":
            case "lt":
            case "1":
                selectedButton = _twitchPlaysButtons[0];
                break;
            case "tr":
            case "rt":
            case "2":
                selectedButton = _twitchPlaysButtons[1];
                break;
            case "bl":
            case "lb":
            case "3":
                selectedButton = _twitchPlaysButtons[2];
                break;
            case "br":
            case "rb":
            case "4":
                selectedButton = _twitchPlaysButtons[3];
                break;
            default:
                for (int i = 0; i < 4; i++)
                {
                    if (_labels[i].Equals(buttonName, System.StringComparison.InvariantCultureIgnoreCase))
                    {
                        selectedButton = buttons[i];
                    }
                }
                break;
        }

        if (selectedButton == null)
        {
            LogMessage("Invalid Twitch command \"" + command + "\" (invalid button '" + modulesMatch.Groups[buttonGroup].Value + "').");
            yield break;
        }

        yield return null;

        int count;
        if (!modulesMatch.Groups[durationGroup].Success || !int.TryParse(modulesMatch.Groups[durationGroup].Value, out count) || count == 0)
        {
            // Not holding, just tapping
            yield return new[] { selectedButton };
            yield break;
        }

        float duration = count * beepLength;

        LogMessage("Valid Twitch command \"" + command + "\". Holding for a duration of " + duration + " seconds.");

        yield return selectedButton;
        yield return new WaitForSeconds(duration);
        yield return selectedButton;
    }
}

