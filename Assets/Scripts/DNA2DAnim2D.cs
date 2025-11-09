using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Anime la double hélice 2D:
/// - scroll (défilement) : décale la phase pour créer l'effet "hélice vivante"
/// - energy (0..1) : colore en vert depuis le bas un pourcentage des pastilles
/// - breathing (léger scale, optionnel)
/// + API progression: Increase/Decrease en %
/// </summary>
[RequireComponent(typeof(DNA2DBuilder))]
public class DNA2DAnimator : MonoBehaviour
{
    [Header("Défilement hélice")]
    [Tooltip("Tours par seconde (défilement vertical)")]
    public float scrollTurnsPerSec = 0.35f;

    [Header("Énergie (coloration depuis le bas)")]
    [Range(0f, 1f)] public float energy = 0.10f;   // démarre à 10%
    public Color energyColor = new Color(0.2f, 1f, 0.3f, 1f);
    [Tooltip("Couleur par défaut si pas d'énergie (hérite de DNA2DBuilder.baseColor si laissé à transparent)")]
    public Color baseColorOverride = new Color(0, 0, 0, 0); // 0 = ignore
    [Tooltip("Douceur du blend énergie/base")]
    [Range(0f, 1f)] public float energyBlend = 1f;

    [Header("Breathing (optionnel)")]
    public bool breathing = false;
    public float breatheSpeed = 1.0f;
    public float breatheMin = 0.95f;
    public float breatheMax = 1.05f;

    [Header("Progression (boutons)")]
    [Tooltip("Pas d'augmentation/diminution en % (ex: 5 = ±5%)")]
    public float stepPercent = 5f;

    private DNA2DBuilder _builder;
    private IReadOnlyList<Image> A;
    private IReadOnlyList<Image> B;
    private readonly List<Vector2> _restSizesA = new();
    private readonly List<Vector2> _restSizesB = new();

    void Awake()
    {
        _builder = GetComponent<DNA2DBuilder>();
    }

    void OnEnable()
    {
        CacheRefs();
    }

    void CacheRefs()
    {
        _builder.Rebuild(); // s'assure que la géométrie existe
        A = _builder.GetStrandA();
        B = _builder.GetStrandB();

        _restSizesA.Clear();
        _restSizesB.Clear();
        foreach (var img in A) _restSizesA.Add(img.rectTransform.sizeDelta);
        foreach (var img in B) _restSizesB.Add(img.rectTransform.sizeDelta);
    }

    void Update()
    {
        // 1) Scroll : fait tourner la phase de la courbe → motif qui “monte/descend”
        if (Mathf.Abs(scrollTurnsPerSec) > 1e-4f)
        {
            _builder.phaseDeg += scrollTurnsPerSec * 360f * Time.deltaTime;
            _builder.Rebuild();
            // après rebuild, recache les listes/sizes
            CacheRefs();
        }

        if (A == null || B == null) return;

        // 2) Energy fill depuis le bas
        Color baseCol = (baseColorOverride.a <= 0.001f) ? _builder.baseColor : baseColorOverride;
        int litCount = Mathf.RoundToInt(A.Count * Mathf.Clamp01(energy)); // par brin
        LitFromBottom(A, litCount, baseCol);
        LitFromBottom(B, litCount, baseCol);

        // 3) Breathing
        if (breathing)
        {
            float t = Time.time * (Mathf.PI * 2f) * breatheSpeed;
            for (int i = 0; i < A.Count; i++)
            {
                float s = (Mathf.Sin(t + i * 0.15f) * 0.5f + 0.5f);
                float k = Mathf.Lerp(breatheMin, breatheMax, s);
                A[i].rectTransform.sizeDelta = _restSizesA[i] * k;
            }
            for (int i = 0; i < B.Count; i++)
            {
                float s = (Mathf.Sin(t + i * 0.15f + 1.57f) * 0.5f + 0.5f);
                float k = Mathf.Lerp(breatheMin, breatheMax, s);
                B[i].rectTransform.sizeDelta = _restSizesB[i] * k;
            }
        }
    }

    void LitFromBottom(IReadOnlyList<Image> strand, int litN, Color baseCol)
    {
        int n = strand.Count;
        for (int i = 0; i < n; i++)
        {
            var img = strand[i];
            if (!img) continue;
            // i=0 en bas → on part du bas
            bool lit = i < litN;
            Color target = lit ? energyColor : baseCol;
            img.color = Color.Lerp(img.color, target, energyBlend);
        }
    }

    // ===== API pratique (à appeler depuis tes boutons) =====

    /// <summary>Fixe l'énergie (accepte 0..1 ou 0..100)</summary>
    public void SetPercent(float value01or100)
    {
        float v = (value01or100 > 1f) ? value01or100 / 100f : value01or100;
        energy = Mathf.Clamp01(v);
    }

    /// <summary>Augmente de 'stepPercent' (ex: 5 = +5%)</summary>
    public void Increase() => IncreaseBy(stepPercent);

    /// <summary>Diminue de 'stepPercent' (ex: 5 = -5%)</summary>
    public void Decrease() => DecreaseBy(stepPercent);

    /// <summary>Augmente d'une valeur donnée (accepte % ou 0..1)</summary>
    public void IncreaseBy(float delta01or100)
    {
        float d = (delta01or100 > 1f) ? delta01or100 / 100f : delta01or100;
        energy = Mathf.Clamp01(energy + d);
    }

    /// <summary>Diminue d'une valeur donnée (accepte % ou 0..1)</summary>
    public void DecreaseBy(float delta01or100)
    {
        float d = (delta01or100 > 1f) ? delta01or100 / 100f : delta01or100;
        energy = Mathf.Clamp01(energy - d);
    }

    // API existante
    public void SetEnergy01(float e) => energy = Mathf.Clamp01(e);
    public void Nudge(float deltaTurns) => _builder.phaseDeg += deltaTurns * 360f;
}
