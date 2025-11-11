using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Hub unique pour piloter les effets glitch UI.
/// Tu peux glisser n'importe quel GameObject (Image, TMP, etc.) : le script
/// va trouver/ajouter un composant nommé "UIGlitchController".
public class GlitchGroupManager : MonoBehaviour
{
    public static GlitchGroupManager Instance { get; private set; }

    [Serializable]
    public class TierPresets
    {
        [Range(0, 1)] public float intensity = 0f;
        [Range(0, 1)] public float distortion = 0f;
        [Range(0, 1)] public float flicker = 0f;
    }

    [Serializable]
    public class Target
    {
        public string name = "Target";
        public Component any;                 // glisse ici n'importe quel GO/Component
        public bool disableAt100 = true;
        public float lerpDuration = 0.25f;

        public TierPresets P0 = new TierPresets { intensity = 0.40f, distortion = 0.25f, flicker = 0.20f };
        public TierPresets P25 = new TierPresets { intensity = 0.30f, distortion = 0.18f, flicker = 0.15f };
        public TierPresets P50 = new TierPresets { intensity = 0.20f, distortion = 0.12f, flicker = 0.10f };
        public TierPresets P75 = new TierPresets { intensity = 0.10f, distortion = 0.06f, flicker = 0.05f };
        public TierPresets P100 = new TierPresets { intensity = 0f, distortion = 0f, flicker = 0f };

        [NonSerialized] public MonoBehaviour controller;
        [NonSerialized] public Coroutine tweenCo;
    }

    [Header("Cibles à piloter")]
    public List<Target> targets = new();

    [Header("Options globales")]
    public bool interpolateBetweenTiers = true;

    void Awake()
    {
        Instance = this;

        foreach (var t in targets)
        {
            if (t == null) continue;
            var go = t.any ? t.any.gameObject : null;
            if (!go) continue;

            // trouve/ajoute un composant nommé "UIGlitchController"
            var ctrl = go.GetComponent("UIGlitchController") as MonoBehaviour;
            if (!ctrl)
            {
                var type = Type.GetType("UIGlitchController");
                if (!ctrl && type != null) ctrl = go.GetComponent(type) as MonoBehaviour;
                if (!ctrl && type != null) ctrl = go.AddComponent(type) as MonoBehaviour;
            }
            t.controller = ctrl;
        }
    }

    /// t = 0..1
    public void ApplyProgress01(float t)
    {
        int fromTier = Mathf.Clamp(Mathf.FloorToInt(t * 4f) * 25, 0, 100);
        int toTier = Mathf.Clamp(fromTier + 25, 0, 100);

        float tierStart = fromTier / 100f;
        float tierEnd = toTier / 100f;
        float localT = (tierEnd > tierStart) ? Mathf.InverseLerp(tierStart, tierEnd, t) : 0f;

        foreach (var target in targets)
        {
            if (target == null || target.controller == null) continue;

            if (t >= 0.999f && target.disableAt100)
            {
                SetAll(target.controller, 0f, 0f, 0f);
                continue;
            }

            var a = GetTier(target, fromTier);
            var b = GetTier(target, toTier);
            float k = interpolateBetweenTiers ? localT : 0f;

            float intensity = Mathf.Lerp(a.intensity, b.intensity, k);
            float distort = Mathf.Lerp(a.distortion, b.distortion, k);
            float flicker = Mathf.Lerp(a.flicker, b.flicker, k);

            // >>> stop/start sur le manager (pas sur Target)
            if (target.tweenCo != null) StopCoroutine(target.tweenCo);
            target.tweenCo = StartCoroutine(TweenTo(target.controller, intensity, distort, flicker, target.lerpDuration));
        }
    }

    // --------- helpers

    static TierPresets GetTier(Target t, int tier)
    {
        return tier switch
        {
            0 => t.P0,
            25 => t.P25,
            50 => t.P50,
            75 => t.P75,
            100 => t.P100,
            _ => t.P100
        };
    }

    static void SetAll(MonoBehaviour controller, float intensity, float distortion, float flicker)
    {
        if (controller == null) return;
        var type = controller.GetType();

        TrySetFloat(type, controller, "intensity", intensity);
        TrySetFloat(type, controller, "distortion", distortion);
        TrySetFloat(type, controller, "flicker", flicker);

        TrySetFloat(type, controller, "Intensity", intensity);
        TrySetFloat(type, controller, "Distortion", distortion);
        TrySetFloat(type, controller, "Flicker", flicker);

        var mi = type.GetMethod("SetAll", new[] { typeof(float), typeof(float), typeof(float) });
        if (mi != null) mi.Invoke(controller, new object[] { intensity, distortion, flicker });
    }

    static IEnumerator TweenTo(MonoBehaviour controller, float intensity, float distortion, float flicker, float dur)
    {
        if (controller == null) yield break;
        var type = controller.GetType();

        float GetF(string n)
        {
            if (TryGetFloat(type, controller, n, out float v)) return v;
            if (TryGetFloat(type, controller, FirstUpper(n), out v)) return v;
            return 0f;
        }

        float i0 = GetF("intensity");
        float d0 = GetF("distortion");
        float f0 = GetF("flicker");

        dur = Mathf.Max(0.0001f, dur);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.SmoothStep(0f, 1f, t);
            SetAll(controller,
                Mathf.Lerp(i0, intensity, k),
                Mathf.Lerp(d0, distortion, k),
                Mathf.Lerp(f0, flicker, k));
            yield return null;
        }
        SetAll(controller, intensity, distortion, flicker);
    }

    static bool TryGetFloat(Type type, object obj, string name, out float value)
    {
        var f = type.GetField(name);
        if (f != null && f.FieldType == typeof(float))
        {
            value = (float)f.GetValue(obj);
            return true;
        }
        var p = type.GetProperty(name);
        if (p != null && p.PropertyType == typeof(float) && p.CanRead)
        {
            value = (float)p.GetValue(obj);
            return true;
        }
        value = 0f; return false;
    }

    static bool TrySetFloat(Type type, object obj, string name, float v)
    {
        var f = type.GetField(name);
        if (f != null && f.FieldType == typeof(float)) { f.SetValue(obj, v); return true; }
        var p = type.GetProperty(name);
        if (p != null && p.PropertyType == typeof(float) && p.CanWrite) { p.SetValue(obj, v); return true; }
        return false;
    }

    static string FirstUpper(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
