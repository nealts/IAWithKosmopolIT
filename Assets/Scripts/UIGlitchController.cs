using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class UIGlitchController : MonoBehaviour
{
    [Header("Material (auto if empty)")]
    [SerializeField] private Shader glitchShader;

    [Header("Glitch Settings (base values)")]
    [Range(0f, 1f)] public float intensity = 0.35f;
    [Range(0f, 5f)] public float timeScale = 1.0f;
    [Range(0f, 50f)] public float horizontalJitter = 10f;
    [Range(1f, 512f)] public float blockSize = 64f;
    [Range(0f, 5f)] public float rgbSplit = 1.0f;
    [Range(0f, 1f)] public float scanline = 0.15f;
    public float seed = 0f;

    [Header("Runtime options")]
    public bool useUnscaledTime = false;   // pour UI pause/menu
    public bool applyEveryFrame = true;    // sinon, appeler ApplyParams() manuellement

    [Header("Auto-drive (optional)")]
    public bool autoDrive = false;
    public float cycleDuration = 2f;            // durée d’un cycle d’animation
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 0.2f, 1, 0.6f);
    public AnimationCurve jitterCurve = AnimationCurve.Linear(0, 0.4f, 1, 1f);
    public AnimationCurve rgbSplitCurve = AnimationCurve.EaseInOut(0, 0.2f, 1, 0.8f);
    [Range(0f, 3f)] public float intensityMul = 1f;
    [Range(0f, 3f)] public float jitterMul = 1f;
    [Range(0f, 3f)] public float rgbSplitMul = 1f;

    [Header("Random flickers (optional)")]
    public bool randomFlickers = false;
    [Range(0f, 1f)] public float flickerChancePerSec = 0.5f; // proba/seconde
    public Vector2 intensityFlickerRange = new Vector2(0.5f, 1f);
    public Vector2 durationFlickerRange = new Vector2(0.05f, 0.2f);

    [Header("Presets (per image)")]
    public List<Preset> presets = new List<Preset>();

    // --- Internal ---
    private Image img;
    private Material runtimeMat;
    private float cycleT;
    private Coroutine burstCo;
    private Coroutine lerpCo;
    private bool frozen;

    // Property IDs
    static readonly int PID_Intensity = Shader.PropertyToID("_Intensity");
    static readonly int PID_TimeScale = Shader.PropertyToID("_TimeScale");
    static readonly int PID_Jitter = Shader.PropertyToID("_Jitter");
    static readonly int PID_BlockSize = Shader.PropertyToID("_BlockSize");
    static readonly int PID_RGBSplit = Shader.PropertyToID("_RGBSplit");
    static readonly int PID_Scanline = Shader.PropertyToID("_Scanline");
    static readonly int PID_Seed = Shader.PropertyToID("_Seed");

    #region Unity
    void Awake()
    {
        img = GetComponent<Image>();

        if (glitchShader == null)
            glitchShader = Shader.Find("UI/Glitch");

        if (glitchShader == null)
        {
            Debug.LogError("UI/Glitch shader introuvable. Vérifie que GlitchUI.shader est dans le projet.");
            enabled = false;
            return;
        }

        runtimeMat = new Material(glitchShader) { name = "UI-Glitch (Instance)" };
        img.material = runtimeMat;

        ApplyParams();
    }

    void OnDestroy()
    {
        if (runtimeMat != null) Destroy(runtimeMat);
    }

    void Update()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (autoDrive && cycleDuration > 0f)
        {
            cycleT += dt / cycleDuration;
            float p = Mathf.Repeat(cycleT, 1f);

            // on anime quelques paramètres par courbes (multiplicateurs inclus)
            float kI = intensityCurve.Evaluate(p) * intensityMul;
            float kJ = jitterCurve.Evaluate(p) * jitterMul;
            float kR = rgbSplitCurve.Evaluate(p) * rgbSplitMul;

            // on injecte sur les valeurs de base (non destructif)
            runtimeMat.SetFloat(PID_Intensity, Mathf.Clamp01(intensity * kI));
            runtimeMat.SetFloat(PID_Jitter, horizontalJitter * kJ);
            runtimeMat.SetFloat(PID_RGBSplit, rgbSplit * kR);
        }

        if (randomFlickers)
            MaybeDoRandomFlicker(dt);

        if (applyEveryFrame)
            ApplyParams(); // garde tout synchro si l’utilisateur bouge les sliders en live
    }
    #endregion

    #region API publique (pratique à appeler depuis des Events/Button/Timeline)

    /// <summary>Applique les valeurs courantes aux propriétés du material.</summary>
    public void ApplyParams()
    {
        if (runtimeMat == null) return;

        runtimeMat.SetFloat(PID_Intensity, intensity);
        runtimeMat.SetFloat(PID_TimeScale, frozen ? 0f : timeScale);
        runtimeMat.SetFloat(PID_Jitter, horizontalJitter);
        runtimeMat.SetFloat(PID_BlockSize, blockSize);
        runtimeMat.SetFloat(PID_RGBSplit, rgbSplit);
        runtimeMat.SetFloat(PID_Scanline, scanline);
        runtimeMat.SetFloat(PID_Seed, seed);
    }

    /// <summary>Déclenche un burst court (0 -> pic -> retour).</summary>
    public void TriggerBurst(float peak = 0.85f, float duration = 0.3f)
    {
        if (burstCo != null) StopCoroutine(burstCo);
        burstCo = StartCoroutine(BurstRoutine(peak, duration));
    }

    /// <summary>Gèle/relance l’animation du glitch (arrête le temps dans le shader).</summary>
    public void Freeze(bool on)
    {
        frozen = on;
        ApplyParams();
    }

    /// <summary>Randomise complètement le seed.</summary>
    [ContextMenu("Randomize Seed")]
    public void RandomizeSeed()
    {
        seed = UnityEngine.Random.Range(0f, 99999f);
        ApplyParams();
    }

    /// <summary>Bouge légèrement le seed (petit “nudge”).</summary>
    public void NudgeSeed(float delta = 1f)
    {
        seed += delta;
        ApplyParams();
    }

    /// <summary>Change la taille des blocs par paliers (pratique pour switch d’un style à l’autre).</summary>
    public void SetBlockSize(float value)
    {
        blockSize = Mathf.Clamp(value, 1f, 512f);
        ApplyParams();
    }

    /// <summary>Charge un preset par index (dans la liste 'presets').</summary>
    public void ApplyPreset(int index)
    {
        if (index < 0 || index >= presets.Count) return;
        var p = presets[index];
        SetFromPreset(p);
        ApplyParams();
    }

    /// <summary>Interpole en douceur vers un preset.</summary>
    public void LerpToPreset(int index, float duration = 0.5f, AnimationCurve ease = null)
    {
        if (index < 0 || index >= presets.Count) return;
        if (lerpCo != null) StopCoroutine(lerpCo);
        lerpCo = StartCoroutine(LerpToPresetRoutine(presets[index], duration, ease));
    }

    /// <summary>Active/désactive l’auto-drive en live.</summary>
    public void SetAutoDrive(bool on)
    {
        autoDrive = on;
    }

    /// <summary>Petit “impact” : hausse brève du jitter + split.</summary>
    public void NudgeImpact(float factor = 1.8f, float time = 0.1f)
    {
        TriggerParamNudge(factor, time);
    }

    #endregion

    #region Internes

    private IEnumerator BurstRoutine(float peak, float duration)
    {
        peak = Mathf.Clamp01(peak);
        duration = Mathf.Max(0.01f, duration);

        float startI = intensity;
        float half = duration * 0.5f;
        float t = 0f;

        // montée
        while (t < half)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = t / half;
            intensity = Mathf.Lerp(startI, peak, k);
            ApplyParams();
            yield return null;
        }

        // descente
        t = 0f;
        while (t < half)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = t / half;
            intensity = Mathf.Lerp(peak, startI, k);
            ApplyParams();
            yield return null;
        }

        intensity = startI;
        ApplyParams();
        burstCo = null;
    }

    private IEnumerator LerpToPresetRoutine(Preset target, float duration, AnimationCurve ease)
    {
        duration = Mathf.Max(0.01f, duration);
        ease ??= AnimationCurve.EaseInOut(0, 0, 1, 1);

        // snapshot
        float i0 = intensity, ts0 = timeScale, j0 = horizontalJitter, b0 = blockSize, r0 = rgbSplit, s0 = scanline, sd0 = seed;
        float t = 0f;
        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = ease.Evaluate(Mathf.Clamp01(t / duration));

            intensity = Mathf.Lerp(i0, target.intensity, k);
            timeScale = Mathf.Lerp(ts0, target.timeScale, k);
            horizontalJitter = Mathf.Lerp(j0, target.horizontalJitter, k);
            blockSize = Mathf.Lerp(b0, target.blockSize, k);
            rgbSplit = Mathf.Lerp(r0, target.rgbSplit, k);
            scanline = Mathf.Lerp(s0, target.scanline, k);
            seed = Mathf.Lerp(sd0, target.seed, k);

            ApplyParams();
            yield return null;
        }
        SetFromPreset(target);
        ApplyParams();
        lerpCo = null;
    }

    private void SetFromPreset(Preset p)
    {
        intensity = p.intensity;
        timeScale = p.timeScale;
        horizontalJitter = p.horizontalJitter;
        blockSize = p.blockSize;
        rgbSplit = p.rgbSplit;
        scanline = p.scanline;
        seed = p.seed;
    }

    private void MaybeDoRandomFlicker(float dt)
    {
        float prob = 1f - Mathf.Exp(-flickerChancePerSec * dt); // proba discrète ≈ taux/seconde
        if (UnityEngine.Random.value < prob)
        {
            float target = UnityEngine.Random.Range(intensityFlickerRange.x, intensityFlickerRange.y);
            float dur = UnityEngine.Random.Range(durationFlickerRange.x, durationFlickerRange.y);
            StartCoroutine(FlickerIntensity(target, dur));
        }
    }

    private IEnumerator FlickerIntensity(float target, float duration)
    {
        float start = intensity;
        float t = 0f;
        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = t / duration;
            intensity = Mathf.Lerp(start, target, k);
            ApplyParams();
            yield return null;
        }
        intensity = start;
        ApplyParams();
    }

    private void TriggerParamNudge(float factor, float time)
    {
        StartCoroutine(NudgeRoutine(factor, time));
    }

    private IEnumerator NudgeRoutine(float factor, float time)
    {
        float j0 = horizontalJitter;
        float r0 = rgbSplit;
        float t = 0f;
        while (t < time)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = 1f - (t / time);
            horizontalJitter = Mathf.Lerp(j0 * factor, j0, 1f - k);
            rgbSplit = Mathf.Lerp(r0 * factor, r0, 1f - k);
            ApplyParams();
            yield return null;
        }
        horizontalJitter = j0;
        rgbSplit = r0;
        ApplyParams();
    }

    #endregion

    [Serializable]
    public struct Preset
    {
        public string name;
        [Range(0f, 1f)] public float intensity;
        [Range(0f, 5f)] public float timeScale;
        [Range(0f, 50f)] public float horizontalJitter;
        [Range(1f, 512f)] public float blockSize;
        [Range(0f, 5f)] public float rgbSplit;
        [Range(0f, 1f)] public float scanline;
        public float seed;
    }
}
