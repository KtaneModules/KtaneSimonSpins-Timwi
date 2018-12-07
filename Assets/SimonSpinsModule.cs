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
    public Texture[] StripeTextures;
    public Color[] FaceColors;
    public Color[] ArmFrameColors;
    public Color Grey;

    public Transform[] Paddles;
    public KMSelectable[] Heads;
    private MeshRenderer[] _headsMR;
    public MeshRenderer[] Arms1;
    public MeshRenderer[] Arms2;
    public MeshRenderer[] Arms3;
    private MeshRenderer[][] _arms;
    public MeshRenderer[] Faces;
    public MeshRenderer[] Symbols;

    enum Property
    {
        Level,  // done
        Symbol, // done
        SymbolSize, // done
        SymbolFill, // done
        SymbolSpin, // done
        SymbolFlash,    // done
        PaddleSpin, // done
        PaddleFlip, // done
        FaceColor,  // done
        FaceStripe, // done
        PaddleShape,    // done
        FrameColor, // done
        ArmColor,   // done
        BackSymbolPattern,
        BackSymbolColor,
        BackSymbol,
        ProtrusionPlacement,
        ProtrusionCount,
        ArmLength,  // done
        ArmCount
    }

    private static readonly Dictionary<Property, string[]> _propertyValueNames = new Dictionary<Property, string[]>
    {
        { Property.Level, new[] { "bottom", "middle", "top" } },
        { Property.Symbol, new[] { "I", "X", "Y" } },
        { Property.SymbolSize, new[] { "large", "medium", "small" } },
        { Property.SymbolFill, new[] { "hollow", "filled", "striped" } },
        { Property.SymbolSpin, new[] { "CCW", "none", "CW" } },
        { Property.SymbolFlash, new[] { "none", "on/off", "inverting" } },
        { Property.PaddleSpin, new[] { "CCW", "none", "cw" } },
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
    private List<Coroutine> _symbolCoroutines = new List<Coroutine>();
    private int _running;
    private readonly Dictionary<Property, int[]> _curPropertyValues = new Dictionary<Property, int[]>();
    private readonly float[] _armAngles = new float[] { 0, 120, -120 };
    private readonly float[] _armSpeeds = new float[] { 0, 0, 0 };
    private readonly float[] _armDecelDelay = new float[] { 0, 0, 0 };
    private readonly float[] _symbolAngles = new float[] { 30, 110, 190 };
    private readonly float[] _headHeights = new float[] { -.003f, -.003f, -.0025f };
    private readonly float[] _flipAngles = new float[] { 0, 0, 0 };

    const float _accelDecelDuration = 2.5f;

    private Property[] _tableProperties;
    private int[][] _tablePropertyValues;
    private int[] _rememberedValues;
    private int _subprogress;
    private int _numberOfStages;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        _arms = new[] { Arms1, Arms2, Arms3 };
        _headsMR = Heads.Select(kms => kms.GetComponent<MeshRenderer>()).ToArray();
        for (int i = 0; i < 3; i++)
        {
            Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
            Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);
            Faces[i].material.color = Grey;
            Heads[i].OnInteract = clicked(i);
        }

        // Paddles are identified by their shape (circle, pentagon, square)
        _curPropertyValues[Property.PaddleShape] = new[] { 0, 1, 2 };

        var rnd = RuleSeedable.GetRNG();
        for (var i = rnd.Next(0, 25); i >= 0; i--)
            rnd.NextDouble();

        _tableProperties = (Property[]) Enum.GetValues(typeof(Property));
        rnd.ShuffleFisherYates(_tableProperties);
        _tableProperties = _tableProperties.Take(10).ToArray();
        _tablePropertyValues = _tableProperties.Select(p => rnd.ShuffleFisherYates(Enumerable.Range(0, 3).ToArray())).ToArray();

        for (int i = 0; i < 10; i++)
            Debug.LogFormat(@"<Simon Spins #{0}> {1} = {2}", _moduleId, _tableProperties[i], _tablePropertyValues[i].Select(x => _propertyValueNames[_tableProperties[i]][x]).Join(", "));

        _numberOfStages = Rnd.Range(3, 6);
        Debug.LogFormat(@"[Simon Spins #{0}] Number of stages: {1}", _moduleId, _numberOfStages);
        StartCoroutine(Init(first: true));
    }

    private KMSelectable.OnInteractHandler clicked(int i)
    {
        return delegate
        {
            Heads[i].AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Heads[i].transform);

            if (_rememberedValues == null || _windDown)      // Module is solved or in transition
                return false;

            var lsn = Bomb.GetSerialNumberNumbers().Last();
            var lastProperty = _tableProperties[(lsn + 1 + _subprogress) % 10];
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

            return false;
        };
    }

    private IEnumerator Init(bool first)
    {
        if (_curPropertyValues.ContainsKey(Property.PaddleSpin))    // This is only false at the very start of the module
        {
            const float angleDifference = 90;
            // Determine the current position (angle) of the paddle that isn’t rotating.
            // The other paddles cannot stop anywhere within angleDifference° of that.
            var stationaryPaddle = Enumerable.Range(0, 3).First(i => _curPropertyValues[Property.PaddleSpin][i] == 1);
            var a = _armAngles[stationaryPaddle];
            var angularRegionsTaken = new AngleRegions();
            angularRegionsTaken.AddRegion(a - angleDifference, a + angleDifference);

            for (int i = 0; i < 3; i++)
                if (i != stationaryPaddle)
                {
                    // Where would this paddle end up if it were to stop now?
                    var endAngle = _armAngles[i] + _accelDecelDuration * _armSpeeds[i] / 2;
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
        var lsn = Bomb.GetSerialNumberNumbers().Last();
        if (first)
        {
            foreach (var cr in _symbolCoroutines)
            {
                StopCoroutine(cr);
                _running--;
            }
            for (int i = 0; i < 3; i++)
            {
                _headsMR[i].material.color = Grey;
                Faces[i].material.color = Grey;
                Faces[i].material.mainTexture = null;
                Symbols[i].gameObject.SetActive(false);
                foreach (var obj in _arms[i])
                    obj.material.color = Grey;
            }

            var firstProperty = _tableProperties[lsn];
            var firstPropertyValue = _tablePropertyValues[lsn][0];
            var firstPaddle = Enumerable.Range(0, 3).First(i => _curPropertyValues[firstProperty][i] == firstPropertyValue);
            _rememberedValues = new[] { _curPropertyValues[_tableProperties[(lsn + 1) % 10]][firstPaddle] };
            Debug.LogFormat(@"[Simon Spins #{0}] Stage 1: find paddle where {1} is {2} and remember that its {3} is {4}.", _moduleId, firstProperty, _propertyValueNames[firstProperty][firstPropertyValue], _tableProperties[(lsn + 1) % 10], _propertyValueNames[_tableProperties[(lsn + 1) % 10]][_rememberedValues[0]]);
        }
        else
        {
            var stage = _rememberedValues.Length;
            Array.Resize(ref _rememberedValues, stage + 1);
            var lastProperty = _tableProperties[(lsn + stage) % 10];
            var nextValue = _tablePropertyValues[(lsn + stage) % 10][(Array.IndexOf(_tablePropertyValues[(lsn + stage) % 10], _rememberedValues[stage - 1]) + 1) % 3];
            var nextPaddle = Enumerable.Range(0, 3).First(i => _curPropertyValues[lastProperty][i] == nextValue);
            _rememberedValues[stage] = _curPropertyValues[_tableProperties[(lsn + stage + 1) % 10]][nextPaddle];
            Debug.LogFormat(@"[Simon Spins #{0}] Stage {1}: find paddle where {2} is {3} and remember that its {4} is {5}.", _moduleId, stage + 1, lastProperty, _propertyValueNames[lastProperty][nextValue], _tableProperties[(lsn + stage + 1) % 10], _propertyValueNames[_tableProperties[(lsn + stage + 1) % 10]][_rememberedValues[stage]]);
        }

        _windDown = true;
        while (_running > 0)
            yield return null;
        _windDown = false;

        _symbolCoroutines.Clear();
        for (int i = 0; i < 3; i++)
            StartCoroutine(run(i));
    }

    private float easeCubic(float t) { return 3 * t * t - 2 * t * t * t; }
    private float interp(float t, float from, float to) { return t * (to - from) + from; }
    private Color interp(float t, Color from, Color to) { return new Color(interp(t, from.r, to.r), interp(t, from.g, to.g), interp(t, from.b, to.b), interp(t, from.a, to.a)); }

    private IEnumerator run(int i)
    {
        _running++;

        // Change the colors of the arms
        StartCoroutine(runAnimation(_arms[i][0].material.color, ArmFrameColors[_curPropertyValues[Property.ArmColor][i]], interp, color =>
        {
            for (int j = 0; j < _arms[i].Length; j++)
                _arms[i][j].material.color = color;
        }));

        // Change the colors of the frames
        StartCoroutine(runAnimation(_headsMR[i].material.color, ArmFrameColors[_curPropertyValues[Property.FrameColor][i]], interp, color => { _headsMR[i].material.color = color; }));

        // Change the colors of the faces
        StartCoroutine(runAnimation(Faces[i].material.color, FaceColors[_curPropertyValues[Property.FaceColor][i]], interp, color => { Faces[i].material.color = color; }));

        // Flash the symbol
        var symbolTextureIx = new[] { Property.Symbol, Property.SymbolFill, Property.SymbolSize }.Aggregate(0, (prev, next) => prev * 3 + _curPropertyValues[Property.SymbolSize][i]);
        _symbolCoroutines.Add(StartCoroutine(flashSymbol(Faces[i], Symbols[i], symbolTextureIx, _curPropertyValues[Property.FaceColor][i], _curPropertyValues[Property.SymbolFlash][i], _curPropertyValues[Property.FaceStripe][i])));

        // Move each paddle to the correct arm length
        StartCoroutine(runAnimation(Heads[i].transform.localPosition.z, .0325f + .01625f * _curPropertyValues[Property.ArmLength][i], (t, s, f) => interp(easeCubic(t), s, f), armLen =>
        {
            // Head position
            Heads[i].transform.localPosition = new Vector3(0, _headHeights[i], armLen);
            // Arms lengths
            var thickness = i == 2 ? .02f : .03f;
            for (int j = 0; j < _arms[i].Length; j++)
                _arms[i][j].transform.localScale = new Vector3(thickness, thickness, armLen * 10 - .18f);
        }));

        // Move each paddle to the correct level (vertical position)
        // Execute this here so that the continuous paddle-rotation animation doesn’t start until the paddles are at the correct height
        var e = runAnimation(Paddles[i].localPosition.y, .02f + .01f * _curPropertyValues[Property.Level][i], (t, s, f) => interp(easeCubic(t), s, f), y => { Paddles[i].localPosition = new Vector3(0, y, 0); }, suppressDelay: true);
        while (e.MoveNext())
            yield return e.Current;

        // Flip the paddle
        StartCoroutine(flipPaddle(i, _curPropertyValues[Property.PaddleFlip][i]));

        var armInitial = _armAngles[i];
        var symbolInitial = _symbolAngles[i];
        _armSpeeds[i] = (_curPropertyValues[Property.PaddleSpin][i] - 1) * Rnd.Range(40f, 60f); // degrees per second
        var symbolAngularSpeed = (_curPropertyValues[Property.SymbolSpin][i] - 1) * Rnd.Range(120f, 140f); // degrees per second

        var elapsed = 0f;
        while (elapsed < _accelDecelDuration)
        {
            // Quadratic function yielding constant acceleration such that the velocity at time d (duration) is s (speed)
            _armAngles[i] = armInitial + _armSpeeds[i] * elapsed * elapsed / _accelDecelDuration / 2;
            Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
            Symbols[i].transform.localEulerAngles = new Vector3(90, symbolInitial + symbolAngularSpeed * elapsed * elapsed / _accelDecelDuration / 2, 0);
            yield return null;
            elapsed += Time.deltaTime;
        }
        _armAngles[i] = armInitial + _armSpeeds[i] * _accelDecelDuration / 2;
        _symbolAngles[i] = symbolInitial + symbolAngularSpeed * _accelDecelDuration / 2;
        yield return null;

        while (!_windDown || _armDecelDelay[i] > 0)
        {
            if (_windDown)
                _armDecelDelay[i] -= Time.deltaTime;
            _armAngles[i] += Time.deltaTime * _armSpeeds[i];
            _symbolAngles[i] += Time.deltaTime * symbolAngularSpeed;
            Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
            Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);
            yield return null;
        }

        armInitial = _armAngles[i];
        symbolInitial = _symbolAngles[i];
        elapsed = Time.deltaTime;
        while (elapsed < _accelDecelDuration)
        {
            // Reverse of the quadratic function above
            _armAngles[i] = armInitial + _armSpeeds[i] * elapsed * (1 - elapsed / _accelDecelDuration / 2);
            Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
            Symbols[i].transform.localEulerAngles = new Vector3(90, symbolInitial + symbolAngularSpeed * elapsed * (1 - elapsed / _accelDecelDuration / 2), 0);
            elapsed += Time.deltaTime;
            yield return null;
        }
        _armAngles[i] = armInitial + _armSpeeds[i] * _accelDecelDuration / 2;
        _symbolAngles[i] = symbolInitial + symbolAngularSpeed * _accelDecelDuration / 2;
        Paddles[i].localEulerAngles = new Vector3(0, _armAngles[i], _flipAngles[i]);
        Symbols[i].transform.localEulerAngles = new Vector3(90, _symbolAngles[i], 0);
        _running--;
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
        // No flipping
        if (value == 2)
            yield break;

        _running++;

        const float duration = 1.3f;

        while (!_windDown)
        {
            yield return new WaitForSeconds(Rnd.Range(1.5f, 2f));

            // Make sure the flipping won’t crash the paddle into another one
            wait:
            yield return null;
            if (_windDown)
                break;
            for (int j = 0; j < 3; j++)
                if (j != i)
                {
                    var myDirection = _curPropertyValues[Property.PaddleSpin][i] - 1;
                    var theirDirection = _curPropertyValues[Property.PaddleSpin][j] - 1;
                    var relativeSpeed = theirDirection - myDirection;

                    if (relativeSpeed < 0)
                    {
                        var timeUntilMeet = (((_armAngles[j] - _armAngles[i]) % 360 + 360) % 360) / (-relativeSpeed) / 50;
                        if (timeUntilMeet < duration * 1.5f)
                            goto wait;
                        var timeSinceLastMet = (((_armAngles[i] - _armAngles[j]) % 360 + 360) % 360) / (-relativeSpeed) / 50;
                        if (timeSinceLastMet < duration * .75f)
                            goto wait;
                    }
                    else
                    {
                        var timeUntilMeet = (((_armAngles[i] - _armAngles[j]) % 360 + 360) % 360) / relativeSpeed / 50;
                        if (timeUntilMeet < duration * 1.5f)
                            goto wait;
                        var timeSinceLastMet = (((_armAngles[j] - _armAngles[i]) % 360 + 360) % 360) / relativeSpeed / 50;
                        if (timeSinceLastMet < duration * .75f)
                            goto wait;
                    }
                }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.deltaTime;
                // We only set the variable here; the object’s actual rotation is set in run()
                _flipAngles[i] = 360 * easeCubic(elapsed / duration) * (value == 0 ? 1 : -1);
            }
        }
        _running--;
    }

    private IEnumerator flashSymbol(MeshRenderer face, MeshRenderer symbol, int symbolIx, int faceColor, int flashPattern, int stripe)
    {
        _running++;

        var stripeTexture = StripeTextures[stripe];

        // flash on
        symbol.material.mainTexture = SymbolTextures[symbolIx];
        var blinkLengths = new[] { .1f, .4f, .25f, .25f, .4f, .1f, 0f };
        for (int j = 0; j < blinkLengths.Length; j++)
        {
            symbol.gameObject.SetActive(j % 2 == 0);
            face.material.mainTexture = j % 2 == 0 ? stripeTexture : null;
            yield return new WaitForSeconds(blinkLengths[j] * .5f);
        }

        while (!_windDown)
        {
            yield return new WaitForSeconds(Rnd.Range(.6f, 1.2f));

            switch (flashPattern)
            {
                case 1: // on/off
                    symbol.gameObject.SetActive(false);
                    break;
                case 2: // invert
                    symbol.material.mainTexture = SymbolTextures[symbolIx + 27 * (faceColor + 1)];
                    face.material.color = Color.black;
                    break;
            }

            yield return new WaitForSeconds(Rnd.Range(.6f, 1.2f));

            symbol.gameObject.SetActive(true);
            symbol.material.mainTexture = SymbolTextures[symbolIx];
            face.material.color = FaceColors[faceColor];
        }

        // flash off
        for (int j = 0; j < blinkLengths.Length; j++)
        {
            symbol.gameObject.SetActive(j % 2 != 0);
            face.material.mainTexture = j % 2 != 0 ? stripeTexture : null;
            yield return new WaitForSeconds(blinkLengths[j] * .5f);
        }

        _running--;
    }

    private IEnumerator runAnimation<T>(T initialValue, T finalValue, Func<float, T, T, T> interpolation, Action<T> setObjects, Action whenDone = null, bool suppressDelay = false)
    {
        if (!suppressDelay)
            yield return new WaitForSeconds(Rnd.Range(.1f, 1.5f));
        var duration = Rnd.Range(1f, 1.5f);
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
