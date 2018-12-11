using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KModkit;
using SimonSpins;
using UnityEngine;

using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Simon Spins
/// Created by Timwi
/// </summary>
public class SimonSpinsModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;
    public KMRuleSeedable RuleSeedable;

    public Texture[] SymbolTextures;
    public Texture[] BackSymbolTextures;
    public Texture[] StripeTextures;
    public Color[] FaceColors;
    public Color[] ArmFrameColors;
    public Color Grey;
    public GameObject DebugParent;
    public MeshRenderer[] DebugLeds;
    public TextMesh DebugRunning;

    public Transform[] Paddles;
    public KMSelectable[] Heads;
    private MeshRenderer[] _headsMR;
    public MeshRenderer[] Arms;
    public MeshRenderer[] Faces;
    public MeshRenderer[] Symbols;
    public Transform[] Protrusions;
    public Transform[] ProtrusionParents;
    public MeshRenderer[] ProtrusionsMR;
    public MeshRenderer[] BackSymbols;
    public Transform[] Colliders;

    enum Property
    {
        Level,
        Symbol,
        SymbolSize,
        SymbolFill,
        SymbolSpin,
        SymbolFlash,
        PaddleSpin,
        PaddleFlip,
        FaceColor,
        FaceStripe,
        PaddleShape,
        FrameColor,
        ArmColor,
        BackSymbolPattern,
        BackSymbolColor,
        BackSymbol,
        ProtrusionPlacement,
        ProtrusionCount,
        ArmLength,
        ArmCount
    }

    private static readonly Dictionary<Property, string[]> _propertyValueNames = new Dictionary<Property, string[]>
    {
        { Property.Level, new[] { "bottom", "middle", "top" } },
        { Property.Symbol, new[] { "I", "X", "Y" } },
        { Property.SymbolSize, new[] { "large", "medium", "small" } },
        { Property.SymbolFill, new[] { "filled", "hollow", "striped" } },
        { Property.SymbolSpin, new[] { "CCW", "none", "CW" } },
        { Property.SymbolFlash, new[] { "none", "on/off", "inverting" } },
        { Property.PaddleSpin, new[] { "CCW", "none", "CW" } },
        { Property.PaddleFlip, new[] { "left", "right", "none" } },
        { Property.FaceColor, new[] { "blue", "red", "yellow" } },
        { Property.FaceStripe, new[] { "arm-aligned", "perpendicular to arm", "none" } },
        { Property.PaddleShape, new[] { "circle", "pentagon", "square" } },
        { Property.FrameColor, new[] { "blue", "red", "yellow" } },
        { Property.ArmColor, new[] { "blue", "red", "yellow" } },
        { Property.BackSymbolPattern, new[] { "single", "arm-aligned", "perpendicular to arm" } },
        { Property.BackSymbolColor, new[] { "blue", "red", "yellow" } },
        { Property.BackSymbol, new[] { "circle", "square", "star" } },
        { Property.ProtrusionPlacement, new[] { "left", "outside", "right" } },
        { Property.ProtrusionCount, new[] { "1", "2", "3" } },
        { Property.ArmLength, new[] { "short", "medium", "long" } },
        { Property.ArmCount, new[] { "1", "2", "3" } }
    };

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _windDown = false;
    private int _runningBacking;
    private int _running { get { return _runningBacking; } set { _runningBacking = value; DebugRunning.text = value.ToString(); } }
    private readonly float[] _armAngles = new float[] { 0, 120, -120 };
    private readonly float[] _armSpeeds = new float[] { 0, 0, 0 };
    private readonly float[] _armAcceleration = new float[] { 20, 20, 20 };
    private readonly float[] _armDecelDelay = new float[] { 0, 0, 0 };
    private readonly float[] _symbolAngles = new float[] { 30, 110, 190 };
    private readonly float[] _headHeights = new float[] { -.003f, -.003f, -.0025f };
    private readonly float[] _flipAngles = new float[] { 0, 0, 0 };
    private readonly bool[] _faceColorFading = new bool[] { false, false, false };
    private readonly bool[] _permaflipped = new bool[] { false, false, false };
    private readonly float[] _protrustionScales = new float[] { 1, 1, .8f };
    private readonly float[] _protrustionScalesGone = new float[] { .8f, .8f, .6f };

    private readonly List<CoroutineInfo> _activeCoroutines = new List<CoroutineInfo>();
    private readonly Dictionary<Property, int[]> _curPropertyValues = new Dictionary<Property, int[]>();

    private Property[] _tableProperties;
    private int[][] _tablePropertyValues;
    private int[] _rememberedValues;
    private int _subprogress;
    private int _numberOfStages;
    private int _firstRuleIndex;

    void Start()
    {
        DebugParent.SetActive(Application.isEditor);

        _moduleId = _moduleIdCounter++;
        _headsMR = Heads.Select(kms => kms.GetComponent<MeshRenderer>()).ToArray();
        for (int i = 0; i < 3; i++)
        {
            Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
            Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);
            Faces[i].material.color = Grey;
            setupButton(i);
        }

        // Paddles are identified by their shape (circle, pentagon, square)
        _curPropertyValues[Property.PaddleShape] = new[] { 0, 1, 2 };

        var rnd = RuleSeedable.GetRNG();
        for (var i = rnd.Next(0, 24); i >= 0; i--)
            rnd.NextDouble();

        _tableProperties = (Property[]) Enum.GetValues(typeof(Property));
        rnd.ShuffleFisherYates(_tableProperties);
        _tablePropertyValues = _tableProperties.Select(p => rnd.ShuffleFisherYates(Enumerable.Range(0, 3).ToArray())).ToArray();

        for (int i = 0; i < 20; i++)
            Debug.LogFormat(@"<Simon Spins #{0}> RULE {1} = {2} = {3}", _moduleId, i, _tableProperties[i], _tablePropertyValues[i].Select(x => _propertyValueNames[_tableProperties[i]][x]).Join(", "));

        var snRulePairs = new Func<int>[][]
        {
            new Func<int>[] { () => Bomb.GetSerialNumberNumbers().First(), () => Bomb.GetSerialNumberNumbers().Last() },
            new Func<int>[] { () => Bomb.GetSerialNumberNumbers().First(), () => Bomb.GetSerialNumberNumbers().Skip(1).First() },
            new Func<int>[] { () => Bomb.GetSerialNumber()[2] - '0', () => Bomb.GetSerialNumber()[5] - '0' },
            new Func<int>[] { () => { var nums = Bomb.GetSerialNumberNumbers().ToArray(); return nums[nums.Length - 2]; }, () => Bomb.GetSerialNumberNumbers().Last() }
        };

        var alternativeNumbers = new Func<int>[][]
        {
            new Func<int>[]
            {
                () => Bomb.GetIndicators().Count(),
                () => Bomb.GetOnIndicators().Count(),
                () => Bomb.GetOffIndicators().Count(),
                () => Bomb.GetIndicators().SelectMany(ind => ind).Distinct().Count(),
                () => Bomb.GetIndicators().SelectMany(ind => ind).Where(ch => "AEIOU".Contains(ch)).Distinct().Count(),
                () => Bomb.GetIndicators().SelectMany(ind => ind).Where(ch => !"AEIOU".Contains(ch)).Distinct().Count(),
                () => Bomb.GetOnIndicators().SelectMany(ind => ind).Distinct().Count(),
                () => Bomb.GetOnIndicators().SelectMany(ind => ind).Where(ch => "AEIOU".Contains(ch)).Distinct().Count(),
                () => Bomb.GetOnIndicators().SelectMany(ind => ind).Where(ch => !"AEIOU".Contains(ch)).Distinct().Count(),
                () => Bomb.GetOffIndicators().SelectMany(ind => ind).Distinct().Count(),
                () => Bomb.GetOffIndicators().SelectMany(ind => ind).Where(ch => "AEIOU".Contains(ch)).Distinct().Count(),
                () => Bomb.GetOffIndicators().SelectMany(ind => ind).Where(ch => !"AEIOU".Contains(ch)).Distinct().Count()
            },
            new Func<int>[]
            {
                () => Bomb.GetPortCount(),
                () => Bomb.GetPortPlateCount(),
                () => Bomb.GetPortPlates().Count(pp => pp.Length > 0),
                () => Bomb.GetPortPlates().Count(pp => pp.Length > 1),
                () => Bomb.CountUniquePorts(),
                () => Bomb.GetPorts().GroupBy(p => p).Count(gr => gr.Count() > 1),
                () => Bomb.GetPortCount(Port.Parallel),
                () => Bomb.GetPortCount(Port.Serial),
                () => Bomb.GetPortCount(Port.RJ45),
                () => Bomb.GetPortCount(Port.StereoRCA),
                () => Bomb.GetPortCount(Port.DVI),
                () => Bomb.GetPortCount(Port.PS2),
                () => Bomb.GetPorts().Count(p => !p.Equals(Port.Parallel.ToString())),
                () => Bomb.GetPorts().Count(p => !p.Equals(Port.Serial.ToString())),
                () => Bomb.GetPorts().Count(p => !p.Equals(Port.RJ45.ToString())),
                () => Bomb.GetPorts().Count(p => !p.Equals(Port.StereoRCA.ToString())),
                () => Bomb.GetPorts().Count(p => !p.Equals(Port.DVI.ToString())),
                () => Bomb.GetPorts().Count(p => !p.Equals(Port.PS2.ToString()))
            },
            new Func<int>[]
            {
                () => Bomb.GetBatteryCount(),
                () => Bomb.GetBatteryCount(Battery.D),
                () => (Bomb.GetBatteryCount(Battery.AA) + Bomb.GetBatteryCount(Battery.AAx3) + Bomb.GetBatteryCount(Battery.AAx4)) / 2,
                () => Bomb.GetModuleNames().Count(),
                () => Bomb.GetSolvableModuleNames().Count()
            }
        };

        var rulePair = snRulePairs[rnd.Next(0, snRulePairs.Length)];
        switch (rnd.Next(0, 3))
        {
            case 0:
                var arr = alternativeNumbers[rnd.Next(0, alternativeNumbers.Length)];
                rulePair = new[] { rulePair[rnd.Next(0, 2)], arr[rnd.Next(0, arr.Length)] };
                break;
            case 1:
                rulePair = new[] { rulePair[1], rulePair[0] };
                break;
                // case 2 simply does nothing
        }

        _firstRuleIndex = rulePair[0]() + 10 * (rulePair[1]() % 2);

        _numberOfStages = Rnd.Range(3, 6);
        Debug.LogFormat(@"[Simon Spins #{0}] Starting row: {1} ({2})", _moduleId, _firstRuleIndex, _tableProperties[_firstRuleIndex]);
        Debug.LogFormat(@"[Simon Spins #{0}] Number of stages: {1}.", _moduleId, _numberOfStages);
        StartCoroutine(Init(first: true));
    }

    private void setupButton(int i)
    {
        Coroutine coroutine = null;
        bool isLongPress = false;

        Heads[i].OnInteract = delegate
        {
            Heads[i].AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Heads[i].transform);

            if (_rememberedValues == null || _windDown)      // Module is solved or in transition
                return false;

            if (coroutine != null)
                StopCoroutine(coroutine);
            isLongPress = false;
            coroutine = StartCoroutine(longPress(() =>
            {
                Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, Heads[i].transform);
                isLongPress = true;
            }));
            return false;
        };

        Heads[i].OnInteractEnded = delegate
        {
            if (coroutine != null)
                StopCoroutine(coroutine);

            if (_rememberedValues == null || _windDown)      // Module is solved or in transition
                return;

            if (isLongPress)
            {
                _permaflipped[i] = !_permaflipped[i];
            }
            else
            {
                var lastProperty = _tableProperties[(_firstRuleIndex + 1 + _subprogress) % 20];
                if (_curPropertyValues[lastProperty][i] == _rememberedValues[_subprogress])
                {
                    Debug.LogFormat(@"[Simon Spins #{0}] Correctly pressed the paddle where {1} is {2}.", _moduleId, lastProperty, _propertyValueNames[lastProperty][_rememberedValues[_subprogress]]);
                    _subprogress++;
                    if (_subprogress == _rememberedValues.Length)
                    {
                        if (_rememberedValues.Length == _numberOfStages)
                        {
                            Debug.LogFormat(@"[Simon Spins #{0}] Module solved.", _moduleId);
                            _rememberedValues = null;
                            _windDown = true;
                            Module.HandlePass();
                        }
                        else
                        {
                            Debug.LogFormat(@"[Simon Spins #{0}] Proceeding to stage {1}.", _moduleId, _rememberedValues.Length + 1);
                            StartCoroutine(Init(first: false));
                        }
                    }
                }
                else
                {
                    Debug.LogFormat(@"[Simon Spins #{0}] You pressed the paddle where {1} is {2}, but I expected {3}. Strike and reset to Stage 1.", _moduleId, lastProperty, _propertyValueNames[lastProperty][_curPropertyValues[lastProperty][i]], _propertyValueNames[lastProperty][_rememberedValues[_subprogress]]);
                    Module.HandleStrike();
                    StartCoroutine(Init(first: true));
                }

                return;
            }
        };
    }

    private IEnumerator longPress(Action after)
    {
        yield return new WaitForSeconds(.7f);
        after();
    }

    private IEnumerator Init(bool first)
    {
        if (_curPropertyValues.ContainsKey(Property.PaddleSpin))    // This is only false at the very start of the module
        {
            const float angleDifference = 70;
            // Determine the current position (angle) of the paddle that isn’t rotating.
            // The other paddles cannot stop anywhere within angleDifference° of that.
            var stationaryPaddle = Enumerable.Range(0, 3).First(i => _curPropertyValues[Property.PaddleSpin][i] == 1);
            var angularRegionsTaken = new AngleRegions();
            angularRegionsTaken.AddRegion(_armAngles[stationaryPaddle] - angleDifference, _armAngles[stationaryPaddle] + angleDifference);

            for (int i = 0; i < 3; i++)
                if (i != stationaryPaddle)
                {
                    // Where would this paddle end up if it were to stop now?
                    var timeToStop = _armSpeeds[i] / _armAcceleration[i];
                    var endAngle = _armAngles[i] + _armSpeeds[i] * timeToStop - _armAcceleration[i] * Mathf.Pow(timeToStop, 2) / 2;
                    var allowedEndAngle = _armSpeeds[i] < 0 ? angularRegionsTaken.FindPrevious(endAngle) : angularRegionsTaken.FindNext(endAngle);
                    // Delay the deceleration so that it will stop at an allowed angle
                    _armDecelDelay[i] = ((((allowedEndAngle - endAngle) * Mathf.Sign(_armSpeeds[i])) % 360 + 360) % 360) / Mathf.Abs(_armSpeeds[i]);
                    angularRegionsTaken.AddRegion(allowedEndAngle - angleDifference, allowedEndAngle + angleDifference);
                }
        }

        // Reassign the properties at random
        var allProperties = (Property[]) Enum.GetValues(typeof(Property));
        for (int i = 0; i < allProperties.Length; i++)
        {
            // Paddles are identified by their shape, so the PaddleShape property remains the same every time.
            if (allProperties[i] != Property.PaddleShape)
                _curPropertyValues[allProperties[i]] = Enumerable.Range(0, 3).ToArray().Shuffle();
            Debug.LogFormat(@"<Simon Spins #{0}> {1}: {2}", _moduleId, allProperties[i], _curPropertyValues[allProperties[i]].Select(val => _propertyValueNames[allProperties[i]][val]).Join(", "));
        }

        _subprogress = 0;
        if (first)
        {
            for (int i = _activeCoroutines.Count - 1; i >= 0; i--)
                if (_activeCoroutines[i].AbruptCancelAllowed)
                {
                    StopCoroutine(_activeCoroutines[i].Coroutine);
                    _activeCoroutines[i].WhenDone();
                    _activeCoroutines.RemoveAt(i);
                }
            for (int i = 0; i < 3; i++)
            {
                _headsMR[i].material.color = Grey;
                Faces[i].material.color = Grey;
                Faces[i].material.mainTexture = null;
                Symbols[i].gameObject.SetActive(false);
                for (int j = 0; j < 3; j++)
                {
                    Arms[3 * i + j].material.color = Grey;
                    ProtrusionsMR[3 * i + j].material.color = Grey;
                }
            }

            var firstProperty = _tableProperties[_firstRuleIndex];
            var firstPropertyValue = _tablePropertyValues[_firstRuleIndex][0];
            var firstPaddle = Enumerable.Range(0, 3).First(i => _curPropertyValues[firstProperty][i] == firstPropertyValue);
            _rememberedValues = new[] { _curPropertyValues[_tableProperties[(_firstRuleIndex + 1) % 20]][firstPaddle] };
            Debug.LogFormat(@"[Simon Spins #{0}] Stage 1: find paddle where {1} is {2} and remember that its {3} is {4}.", _moduleId, firstProperty, _propertyValueNames[firstProperty][firstPropertyValue], _tableProperties[(_firstRuleIndex + 1) % 20], _propertyValueNames[_tableProperties[(_firstRuleIndex + 1) % 20]][_rememberedValues[0]]);
        }
        else
        {
            var stage = _rememberedValues.Length;
            Array.Resize(ref _rememberedValues, stage + 1);
            var lastProperty = _tableProperties[(_firstRuleIndex + stage) % 20];
            var nextValue = _tablePropertyValues[(_firstRuleIndex + stage) % 20][(Array.IndexOf(_tablePropertyValues[(_firstRuleIndex + stage) % 20], _rememberedValues[stage - 1]) + 1) % 3];
            var nextPaddle = Enumerable.Range(0, 3).First(i => _curPropertyValues[lastProperty][i] == nextValue);
            _rememberedValues[stage] = _curPropertyValues[_tableProperties[(_firstRuleIndex + stage + 1) % 20]][nextPaddle];
            Debug.LogFormat(@"[Simon Spins #{0}] Stage {1}: find paddle where {2} is {3} and remember that its {4} is {5}.", _moduleId, stage + 1, lastProperty, _propertyValueNames[lastProperty][nextValue], _tableProperties[(_firstRuleIndex + stage + 1) % 20], _propertyValueNames[_tableProperties[(_firstRuleIndex + stage + 1) % 20]][_rememberedValues[stage]]);
        }

        _windDown = true;
        while (_running > 0)
            yield return null;
        _windDown = false;

        for (int i = 0; i < 3; i++)
            StartCoroutine(run(i));
    }

    private float easeCubic(float t) { return 3 * t * t - 2 * t * t * t; }
    private float interp(float t, float from, float to) { return t * (to - from) + from; }
    private Color interp(float t, Color from, Color to) { return new Color(interp(t, from.r, to.r), interp(t, from.g, to.g), interp(t, from.b, to.b), interp(t, from.a, to.a)); }

    private IEnumerator CoroutineWithCleanup(IEnumerator coroutine, CoroutineInfo ci)
    {
        _activeCoroutines.Add(ci);
        while (coroutine.MoveNext())
            yield return coroutine.Current;
        ci.WhenDone();
        _activeCoroutines.Remove(ci);
    }

    private void StartCoroutineWithCleanup(int ledIndex, bool abruptCancelAllowed, IEnumerator coroutine, Action cleanup = null)
    {
        StartCoroutineWithCleanup(ledIndex, abruptCancelAllowed, () => Color.white, () => Color.black, coroutine, cleanup);
    }

    private void StartCoroutineWithCleanup(int ledIndex, bool abruptCancelAllowed, Func<Color> startColor, Func<Color> endColor, IEnumerator coroutine, Action cleanup = null)
    {
        _running++;
        DebugLeds[ledIndex].material.color = startColor();
        var ci = new CoroutineInfo();
        ci.LedIndex = ledIndex;
        ci.AbruptCancelAllowed = abruptCancelAllowed;
        ci.WhenDone = () =>
        {
            if (cleanup != null)
                cleanup();
            DebugLeds[ledIndex].material.color = endColor();
            _running--;
        };
        ci.Coroutine = StartCoroutine(CoroutineWithCleanup(coroutine, ci));
    }

    private IEnumerator run(int i)
    {
        _running++;
        DebugLeds[i].material.color = Color.blue;

        // Change the colors of the arms
        StartCoroutineWithCleanup(3 + i, true, runAnimation(Arms[3 * i].material.color, ArmFrameColors[_curPropertyValues[Property.ArmColor][i]], interp, color =>
        {
            for (int j = 0; j < 3; j++)
                Arms[3 * i + j].material.color = color;
        }));

        // Change the colors of the frames
        StartCoroutineWithCleanup(6 + i, true, runAnimation(_headsMR[i].material.color, ArmFrameColors[_curPropertyValues[Property.FrameColor][i]], interp, color =>
        {
            _headsMR[i].material.color = color;
            for (int j = 0; j < 3; j++)
                ProtrusionsMR[3 * i + j].material.color = color;
        }));

        // Change the colors of the faces
        _faceColorFading[i] = true;
        StartCoroutineWithCleanup(9 + i, true, runAnimation(Faces[i].material.color, FaceColors[_curPropertyValues[Property.FaceColor][i]], interp,
            color => { Faces[i].material.color = color; }, whenDone: () => { _faceColorFading[i] = false; }));

        // Flash the symbol
        var symbolTextureIx = new[] { Property.Symbol, Property.SymbolFill, Property.SymbolSize }.Aggregate(0, (prev, next) => prev * 3 + _curPropertyValues[next][i]);
        StartCoroutineWithCleanup(12 + i, true, flashSymbol(i, symbolTextureIx, _curPropertyValues[Property.FaceColor][i], _curPropertyValues[Property.SymbolFlash][i], _curPropertyValues[Property.FaceStripe][i]));

        // Move each paddle to the correct arm length
        StartCoroutineWithCleanup(15 + i, false, runAnimation(
            initialValue: new { ArmLen = Heads[i].transform.localPosition.z, Thicknesses = Arms.Skip(3 * i).Take(3).Select(arm => arm.transform.localScale.x).ToArray() },
            finalValue: new { ArmLen = (i == 1 ? .0405f : .04f) + .01625f * _curPropertyValues[Property.ArmLength][i], Thicknesses = Enumerable.Range(0, 3).Select(j => j <= _curPropertyValues[Property.ArmCount][i] ? .02f : .01f).ToArray() },
            interpolation: (t, s, f) => new { ArmLen = interp(easeCubic(t), s.ArmLen, f.ArmLen), Thicknesses = Enumerable.Range(0, 3).Select(j => interp(easeCubic(t), s.Thicknesses[j], f.Thicknesses[j])).ToArray() },
            setObjects: inf =>
            {
                // Head position
                Heads[i].transform.localPosition = new Vector3(0, _headHeights[i], inf.ArmLen);
                // Arms lengths
                for (int j = 0; j < 3; j++)
                    Arms[3 * i + j].transform.localScale = new Vector3(inf.Thicknesses[j], inf.Thicknesses[j], inf.ArmLen * 10 - .18f);
            }));

        // Number of arms
        var rgb = new bool[3];
        Func<Color> getColor = () => new Color(rgb[0] ? 1 : 0, rgb[1] ? 1 : 0, rgb[2] ? 1 : 0);
        foreach (var j in Enumerable.Range(0, 3))   // use foreach instead of for so that the variable j can be safely captured
            StartCoroutineWithCleanup(18 + i, false, () => { rgb[j] = true; return getColor(); }, () => { rgb[j] = false; return getColor(); }, runAnimation(
                initialValue: Arms[3 * i + j].transform.localPosition.x,
                finalValue: .005f * Math.Min(j, _curPropertyValues[Property.ArmCount][i]) - .0025f * _curPropertyValues[Property.ArmCount][i],
                interpolation: (t, s, f) => interp(easeCubic(t), s, f),
                setObjects: armPos => { Arms[3 * i + j].transform.localPosition = new Vector3(armPos, 0, 0); }));

        // Number and position of the protrusions
        ProtrusionParents[i].localEulerAngles = new Vector3(0, (i == 1 ? 72 : 90) * (_curPropertyValues[Property.ProtrusionPlacement][i] - 1), 0);
        for (int j = 0; j < 3; j++)
        {
            Protrusions[3 * i + j].gameObject.SetActive(j <= _curPropertyValues[Property.ProtrusionCount][i]);
            if (i == 0)
                Protrusions[3 * i + j].localEulerAngles = new Vector3(0, 16 * j - 8 * _curPropertyValues[Property.ProtrusionCount][i]);
            else
                Protrusions[3 * i + j].localPosition = new Vector3(.035f * j - .0175f * _curPropertyValues[Property.ProtrusionCount][i], 0, i == 1 ? -.0175f : -.011f);
        }

        // Show (and later hide) the protrusions
        StartCoroutineWithCleanup(21 + i, false, showHideProtrusions(i));

        // Move each paddle to the correct level (vertical position)
        // Execute this here so that the continuous paddle-rotation animation doesn’t start until the paddles are at the correct height
        var e = runAnimation(Paddles[i].localPosition.y, .02f + .01f * _curPropertyValues[Property.Level][i], (t, s, f) => interp(easeCubic(t), s, f), y => { Paddles[i].localPosition = new Vector3(0, y, 0); }, suppressDelay: true);
        while (e.MoveNext())
            yield return e.Current;

        // Flip the paddle (also sets the back symbols)
        StartCoroutineWithCleanup(24 + i, false, flipPaddle(i, _curPropertyValues[Property.PaddleFlip][i]));

        _armSpeeds[i] = 0;
        var targetArmSpeed = Rnd.Range(40f, 60f);
        _armAcceleration[i] = (_curPropertyValues[Property.PaddleSpin][i] - 1) * Rnd.Range(16f, 24f); // degrees per second per second
        var symbolAcceleration = (_curPropertyValues[Property.SymbolSpin][i] - 1) * Rnd.Range(48f, 56f); // degrees per second per second
        var symbolAngularSpeed = 0f;

        if (_curPropertyValues[Property.PaddleSpin][i] == 1)    // paddle won’t move, so it doesn’t need to accelerate or decelerate
        {
            DebugLeds[i].material.color = Color.white;
            while (!_windDown)
                yield return null;
        }
        else if (!_windDown)    // short-circuit if the user already pressed a paddle while the “move each paddle to the correct level” animation was still running
        {
            // ACCELERATION
            DebugLeds[i].material.color = Color.green;
            var elapsed = 0f;
            while (Mathf.Abs(_armSpeeds[i]) < Mathf.Abs(targetArmSpeed))
            {
                _armSpeeds[i] += Time.deltaTime * _armAcceleration[i];
                _armAngles[i] += Time.deltaTime * _armSpeeds[i];
                Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);

                symbolAngularSpeed += Time.deltaTime * symbolAcceleration;
                _symbolAngles[i] += Time.deltaTime * symbolAngularSpeed;
                Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);

                yield return null;
                elapsed += Time.deltaTime;

                if (_windDown)
                    break;
            }

            // CONTINUOUS MOVEMENT
            DebugLeds[i].material.color = Color.white;
            while (!_windDown || _armDecelDelay[i] > 0)
            {
                if (_windDown)
                    _armDecelDelay[i] -= Time.deltaTime;
                _armAngles[i] += Time.deltaTime * _armSpeeds[i];
                Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);

                _symbolAngles[i] += Time.deltaTime * symbolAngularSpeed;
                Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);

                yield return null;
            }

            // DECELERATION
            DebugLeds[i].material.color = Color.red;
            while (Mathf.Sign(_armSpeeds[i]) == Mathf.Sign(_armAcceleration[i]))
            {
                _armSpeeds[i] -= Time.deltaTime * _armAcceleration[i];
                _armAngles[i] += Time.deltaTime * _armSpeeds[i];
                Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);

                symbolAngularSpeed -= Time.deltaTime * symbolAcceleration;
                _symbolAngles[i] += Time.deltaTime * symbolAngularSpeed;
                Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);

                yield return null;
            }
        }
        _armSpeeds[i] = 0;
        _running--;
        DebugLeds[i].material.color = Color.black;
    }

    private IEnumerator showHideProtrusions(int i)
    {
        yield return new WaitForSeconds(Rnd.Range(.1f, 1.5f));

        // Show protrusions
        DebugLeds[21 + i].material.color = Color.green;
        var showAnim = runAnimation(_protrustionScalesGone[i], _protrustionScales[i], (t, s, f) => interp(easeCubic(t), s, f), scale => { ProtrusionParents[i].localScale = new Vector3(scale, scale, scale); });
        while (showAnim.MoveNext())
            yield return showAnim.Current;

        DebugLeds[21 + i].material.color = Color.blue;
        while (!_windDown)
            yield return null;

        yield return new WaitForSeconds(Rnd.Range(.1f, 1.5f));

        // Disappear the protrusions
        DebugLeds[21 + i].material.color = Color.red;
        var hideAnim = runAnimation(_protrustionScales[i], _protrustionScalesGone[i], interpolation: (t, s, f) => interp(easeCubic(t), s, f), setObjects: scale => { ProtrusionParents[i].localScale = new Vector3(scale, scale, scale); });
        while (hideAnim.MoveNext())
            yield return hideAnim.Current;
    }

    private IEnumerator spinSymbol(int i, int direction)
    {
        var speed = Rnd.Range(120f, 180f);
        while (true)
        {
            yield return null;
            _symbolAngles[i] += Time.deltaTime * speed;
            Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);
        }
    }

    private static float minAngleDistance(float a1, float a2)
    {
        var a1m = (a1 % 360 + 360) % 360;
        var a2m = (a2 % 360 + 360) % 360;
        return Mathf.Min(Mathf.Abs(a1m - a2m), Mathf.Abs(a1m + 360 - a2m), Mathf.Abs(a1m - (a2m + 360)));
    }

    private IEnumerator flipPaddle(int i, int value)
    {
        var pattern = _curPropertyValues[Property.BackSymbolPattern][i];
        var dist = new[] { .045f, .0375f, .03f }[i];
        for (int j = 0; j < 2; j++)
        {
            BackSymbols[2 * i + j].material.mainTexture = BackSymbolTextures[3 * _curPropertyValues[Property.BackSymbolColor][i] + _curPropertyValues[Property.BackSymbol][i]];
            BackSymbols[2 * i + j].transform.localPosition = new Vector3(pattern == 2 ? -dist + 2 * j * dist : 0, -.0001f, pattern == 1 ? -dist + 2 * j * dist : 0);
            BackSymbols[2 * i + j].gameObject.SetActive(j == 0 || pattern != 0);
        }

        const float flipDuration = 1.3f;
        var curPermaflipped = false;

        while (!_windDown || curPermaflipped)
        {
            var waitElapsed = 0f;
            var waitDuration = Rnd.Range(1.5f, 2f);
            while (waitElapsed < waitDuration && _permaflipped[i] == curPermaflipped && !_windDown)
            {
                yield return null;
                waitElapsed += Time.deltaTime;
            }

            // Make sure the flipping won’t crash the paddle into another one
            wait:
            yield return null;
            if (_windDown && !curPermaflipped)
                break;
            for (int j = 0; j < 3; j++)
                if (j != i)
                {
                    var relativeSpeed = _armSpeeds[j] - _armSpeeds[i];
                    if (relativeSpeed < 0)
                    {
                        var timeUntilMeet = (((_armAngles[j] - _armAngles[i]) % 360 + 360) % 360) / (-relativeSpeed);
                        if (timeUntilMeet < flipDuration * 1.5f)
                            goto wait;
                        var timeSinceLastMet = (((_armAngles[i] - _armAngles[j]) % 360 + 360) % 360) / (-relativeSpeed);
                        if (timeSinceLastMet < flipDuration * .75f)
                            goto wait;
                    }
                    else
                    {
                        var timeUntilMeet = (((_armAngles[i] - _armAngles[j]) % 360 + 360) % 360) / relativeSpeed;
                        if (timeUntilMeet < flipDuration * 1.5f)
                            goto wait;
                        var timeSinceLastMet = (((_armAngles[j] - _armAngles[i]) % 360 + 360) % 360) / relativeSpeed;
                        if (timeSinceLastMet < flipDuration * .75f)
                            goto wait;
                    }
                }

            var elapsed = 0f;
            var wantPermaflipped = _permaflipped[i] && !_windDown;
            if (wantPermaflipped != curPermaflipped || value != 2)
                while (elapsed < flipDuration)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                    _flipAngles[i] = (wantPermaflipped != curPermaflipped ? 180 : 360) * easeCubic(elapsed / flipDuration) * (value == 0 ? 1 : -1) + (curPermaflipped ? 180 : 0);
                    Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
                }
            curPermaflipped = wantPermaflipped;
        }
        _permaflipped[i] = false;
    }

    private IEnumerator flashSymbol(int i, int symbolIx, int faceColor, int flashPattern, int stripe)
    {
        var stripeTexture = StripeTextures[stripe];
        yield return new WaitForSeconds(Rnd.Range(.1f, 1.5f));

        // flash on
        Symbols[i].material.mainTexture = SymbolTextures[symbolIx];
        var blinkLengths = new[] { .1f, .4f, .25f, .25f, .4f, .1f, 0f };
        for (int j = 0; j < blinkLengths.Length; j++)
        {
            Symbols[i].gameObject.SetActive(j % 2 == 0);
            Faces[i].material.mainTexture = j % 2 == 0 ? stripeTexture : null;
            yield return new WaitForSeconds(blinkLengths[j] * .2f);
        }

        while (_faceColorFading[i])
            yield return null;

        // continuous flashing
        while (!_windDown)
        {
            yield return new WaitForSeconds(Rnd.Range(.6f, 1.2f));

            switch (flashPattern)
            {
                case 1: // on/off
                    Symbols[i].gameObject.SetActive(false);
                    break;
                case 2: // invert
                    Symbols[i].material.mainTexture = SymbolTextures[symbolIx + 27 * (faceColor + 1)];
                    Faces[i].material.color = Color.black;
                    break;
            }

            yield return new WaitForSeconds(Rnd.Range(.6f, 1.2f));

            Symbols[i].gameObject.SetActive(true);
            Symbols[i].material.mainTexture = SymbolTextures[symbolIx];
            Faces[i].material.color = FaceColors[faceColor];
        }

        // flash off
        for (int j = 0; j < blinkLengths.Length; j++)
        {
            Symbols[i].gameObject.SetActive(j % 2 != 0);
            Faces[i].material.mainTexture = j % 2 != 0 ? stripeTexture : null;
            yield return new WaitForSeconds(blinkLengths[j] * .2f);
        }
    }

    private IEnumerator runAnimation<T>(T initialValue, T finalValue, Func<float, T, T, T> interpolation, Action<T> setObjects, Action whenDone = null, bool suppressDelay = false)
    {
        if (!suppressDelay)
            yield return new WaitForSeconds(Rnd.Range(.1f, 1.5f));
        var duration = Rnd.Range(1.5f, 2.5f);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            setObjects(interpolation(elapsed / duration, initialValue, finalValue));
            yield return null;
            elapsed += Time.deltaTime;
        }
        setObjects(finalValue);
        if (whenDone != null)
            whenDone();
    }

    private IEnumerator ProcessTwitchCommand(string command)
    {
        if (command == "wd")
            _windDown = true;
        yield break;
    }
}
