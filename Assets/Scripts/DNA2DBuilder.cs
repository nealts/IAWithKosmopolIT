using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Construit une double hélice en UI (2 colonnes de pastilles).
/// A placer sur un GameObject (RectTransform) sous ton Canvas.
/// </summary>
[ExecuteAlways]
public class DNA2DBuilder : MonoBehaviour
{
    [Header("Cible")]
    [Tooltip("Container UI (RectTransform) qui contiendra les pastilles. Laisser vide = ce GameObject.")]
    public RectTransform targetParent;

    [Header("Géométrie")]
    [Tooltip("Nombre de niveaux verticaux (lignes de pastilles de bas en haut)")]
    public int levels = 28;
    [Tooltip("Espacement vertical en px entre niveaux")]
    public float stepY = 22f;
    [Tooltip("Rayon horizontal (écart max depuis l'axe central)")]
    public float radiusPx = 28f;
    [Tooltip("Décalage horizontal global (px)")]
    public float centerOffsetX = 0f;

    [Header("Aspect des pastilles")]
    [Tooltip("Diamètre moyen des pastilles (px)")]
    public float dotSizePx = 16f;
    [Tooltip("Pastille quand 'devant' (x grand) : facteur de taille (1 = normal)")]
    public float sizeFrontMul = 1.15f;
    [Tooltip("Pastille quand 'derrière' (x petit) : facteur de taille")]
    public float sizeBackMul = 0.9f;
    [Tooltip("Alpha mini quand 'derrière'")]
    [Range(0f, 1f)] public float alphaBack = 0.65f;

    [Header("Courbe hélice")]
    [Tooltip("Tours (cycles) le long de la hauteur totale")]
    public float turns = 6.0f;
    [Tooltip("Angle de départ (degrés) – sert à décaler le motif")]
    public float phaseDeg = 0f;

    [Header("Sprites & Couleurs")]
    [Tooltip("Sprite rond (facultatif). Si nul, cercle blanc généré")]
    public Sprite circleSprite;
    public Color baseColor = new Color(0.85f, 0.95f, 0.95f, 1f); // gris/blanc doux par défaut

    [Header("Options")]
    public bool buildOnEnable = true;
    public bool clearOnRebuild = true;

    [Header("Contour des pastilles")]
    public bool addOutline = true;
    [Range(0f, 6f)] public float outlinePx = 2f;
    public Color outlineColor = Color.white;


    // cache
    private RectTransform _parentRT;
    private readonly List<Image> _strandA = new();
    private readonly List<Image> _strandB = new();
    private Sprite _fallbackCircle;

    void OnEnable()
    {
        if (buildOnEnable) Rebuild();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        EnsureParent();

        if (clearOnRebuild)
        {
            for (int i = _parentRT.childCount - 1; i >= 0; i--)
            {
#if UNITY_EDITOR
                if (Application.isPlaying) Destroy(_parentRT.GetChild(i).gameObject);
                else DestroyImmediate(_parentRT.GetChild(i).gameObject);
#else
                Destroy(_parentRT.GetChild(i).gameObject);
#endif
            }
            _strandA.Clear();
            _strandB.Clear();
        }

        EnsureFallbackSprite();

        // conteneurs propres
        var a = CreateGroup("Strand_A");
        var b = CreateGroup("Strand_B");

        float totalHeight = (levels - 1) * stepY;
        float startY = -totalHeight * 0.5f;
        float phaseRad = phaseDeg * Mathf.Deg2Rad;

        for (int i = 0; i < levels; i++)
        {
            float t = (levels <= 1) ? 0f : (float)i / (levels - 1);      // 0..1
            float y = startY + i * stepY;

            // angle le long de la hauteur
            float ang = (t * turns * Mathf.PI * 2f) + phaseRad;

            // projection 2D : deux points opposés (cos décalé de 180°)
            float xA = Mathf.Cos(ang) * radiusPx + centerOffsetX;
            float xB = -Mathf.Cos(ang) * radiusPx + centerOffsetX;

            // taille/alpha selon "proximité caméra" (x grand => devant)
            float frontnessA = Mathf.InverseLerp(0f, radiusPx, Mathf.Abs(xA));
            float frontnessB = Mathf.InverseLerp(0f, radiusPx, Mathf.Abs(xB));

            var imgA = CreateDot(a, new Vector2(xA, y), SizeFor(frontnessA), ColorFor(frontnessA));
            var imgB = CreateDot(b, new Vector2(xB, y), SizeFor(frontnessB), ColorFor(frontnessB));

            _strandA.Add(imgA);
            _strandB.Add(imgB);
        }
    }

    // --- Helpers ---

    RectTransform CreateGroup(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_parentRT, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    Image CreateDot(RectTransform parent, Vector2 pos, float size, Color col)
    {
        var go = new GameObject("Base", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        var img = go.GetComponent<Image>();
        if (addOutline && outlinePx > 0f)
        {
            var ol = go.GetComponent<Outline>();
            if (!ol) ol = go.AddComponent<Outline>();
            ol.effectColor = outlineColor;
            // épaisseur uniformisée (Outline dessine déjà dans 4 directions)
            ol.effectDistance = new Vector2(outlinePx, -outlinePx);
            ol.useGraphicAlpha = true;
        }
        go.transform.SetParent(parent, false);

        img.raycastTarget = false;
        img.color = col;
        img.sprite = circleSprite ? circleSprite : _fallbackCircle;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        return img;
    }

    float SizeFor(float frontness01)
    {
        // 0 => back, 1 => front
        float k = Mathf.Lerp(sizeBackMul, sizeFrontMul, frontness01);
        return dotSizePx * k;
    }

    Color ColorFor(float frontness01)
    {
        var c = baseColor;
        c.a = Mathf.Lerp(alphaBack, 1f, frontness01);
        return c;
    }

    void EnsureParent()
    {
        if (!targetParent) _parentRT = GetComponent<RectTransform>();
        else _parentRT = targetParent;

        if (_parentRT == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (!canvas) { Debug.LogError("[DNA2D] Aucun Canvas trouvé."); return; }
            var go = new GameObject("DNA2D", typeof(RectTransform));
            _parentRT = go.GetComponent<RectTransform>();
            _parentRT.SetParent(canvas.transform, false);
            _parentRT.anchorMin = _parentRT.anchorMax = new Vector2(0.5f, 0.5f);
            _parentRT.anchoredPosition = Vector2.zero;
            _parentRT.sizeDelta = new Vector2(180, 800);
        }
    }

    void EnsureFallbackSprite()
    {
        if (_fallbackCircle != null) return;

        int w = 64, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false) { name = "DNA2D_Circle" };
        var clear = new Color32(255, 255, 255, 0);
        var white = new Color32(255, 255, 255, 255);
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float r = Mathf.Min(cx, cy) - 1f; float r2 = r * r;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - cx, dy = y - cy;
                tex.SetPixel(x, y, (dx * dx + dy * dy <= r2) ? white : clear);
            }
        tex.Apply();
        _fallbackCircle = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // --- Accès pour l'anim ---
    public IReadOnlyList<Image> GetStrandA() => _strandA;
    public IReadOnlyList<Image> GetStrandB() => _strandB;
}
