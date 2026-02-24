using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[DisallowMultipleComponent]
public class MatrixRainController : MonoBehaviour
{
    [Header("Canvas sorting (put matrix behind everything)")]
    [Tooltip("Create/force a local Canvas and set its sorting so the matrix renders behind your game UI.")]
    public bool useOwnCanvas = true;
    [Tooltip("Sorting order used if useOwnCanvas = true. Lower number = rendered behind.")]
    public int sortingOrder = -50;               // << behind default UI (0)

    [Header("Zone")]
    [SerializeField] private RectTransform targetArea;          // Laisse vide -> prend le RectTransform du GameObject
    [SerializeField] private bool addRectMaskToClip = true;     // Ajoute automatiquement un RectMask2D

    [Header("Police & rendu")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [Min(6)] public int baseFontSize = 22;
    [Range(0.8f, 1.6f)] public float lineSpacing = 1.05f;
    [Min(8f)] public float columnWidth = 18f;
    public float sidePadding = 8f;

    [Header("Colonnes")]
    public int columns = 0;
    [Min(1)] public int maxColumnsAuto = 160;

    [Header("Vitesses & longueurs")]
    public Vector2 speedRange = new Vector2(60f, 220f);         // px/sec
    public Vector2Int lengthRange = new Vector2Int(8, 28);      // nb de caractères par colonne

    [Header("Couleurs")]
    public Color headColor = new Color(0.8f, 1f, 0.8f, 1f);
    public Color tailColor = new Color(0.0f, 0.9f, 0.3f, 0.35f);

    [Header("Comportement")]
    [Range(0.02f, 0.5f)] public float charRefreshInterval = 0.06f;
    [Range(0f, 1f)] public float shuffleRatio = 0.35f;
    public bool desync = true;

    [Header("Jeu de caractères")]
    [TextArea(1, 3)]
    public string charset = "0123456789 ";

    [Header("Fade out")]
    public float fadeOutDuration = 3f;     // durée du Lerp jusqu'à disparition
    private bool _isFading = false;
    private CanvasGroup _canvasGroup;

    // --- internes ---
    private RectTransform rt;
    private readonly List<MatrixColumn> pool = new();
    private float pixelPerLine;

    [System.Obsolete]
    void Awake()
    {
        // Force a local Canvas with low sorting order so this effect goes behind the rest.
        if (useOwnCanvas)
        {
            var cv = GetComponent<Canvas>();
            if (!cv) cv = gameObject.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = sortingOrder;   // lower = behind
        }

        // <<< nouveau : CanvasGroup pour gérer l'alpha global
        _canvasGroup = GetComponent<CanvasGroup>();
        if (!_canvasGroup) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 1f;
        // >>>

        rt = GetComponent<RectTransform>();
        if (!targetArea) targetArea = rt;

        if (addRectMaskToClip && !GetComponent<RectMask2D>())
            gameObject.AddComponent<RectMask2D>();

        if (fontAsset == null)
            Debug.LogWarning("MatrixRainController: aucun TMP_FontAsset défini, je prends la police par défaut.");

        Build();
    }

    void OnEnable()
    {
        foreach (var c in pool) if (c) c.enabled = true;
        KosmoGameManager.GameCompleted += HandleGameCompleted;   // écoute l'event
    }

    void OnDisable()
    {
        foreach (var c in pool) if (c) c.enabled = false;
        KosmoGameManager.GameCompleted -= HandleGameCompleted;   // se désabonne
    }

    void OnDestroy()
    {
        foreach (var c in pool) if (c) Destroy(c.gameObject);
        pool.Clear();
    }

    [ContextMenu("Rebuild")]
    [System.Obsolete]
    public void Build()
    {
        foreach (var c in pool) if (c) Destroy(c.gameObject);
        pool.Clear();

        var areaWidth = targetArea.rect.width - sidePadding * 2f;
        pixelPerLine = baseFontSize * lineSpacing;

        int colCount = columns > 0 ? columns : Mathf.Clamp(Mathf.FloorToInt(areaWidth / columnWidth), 1, maxColumnsAuto);
        float x0 = -targetArea.rect.width * 0.5f + sidePadding + columnWidth * 0.5f;

        for (int i = 0; i < colCount; i++)
        {
            float x = x0 + i * columnWidth;
            var col = CreateColumn(x);
            pool.Add(col);
        }
    }

    [System.Obsolete]
    private MatrixColumn CreateColumn(float localX)
    {
        var go = new GameObject("MatrixColumn", typeof(RectTransform));
        go.transform.SetParent(targetArea, false);

        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(columnWidth, targetArea.rect.height + 200f);
        r.anchoredPosition = new Vector2(localX, Random.Range(50f, targetArea.rect.height * 0.5f));

        var tmpGo = new GameObject("TMP", typeof(TextMeshProUGUI));
        tmpGo.transform.SetParent(go.transform, false);

        var tmp = tmpGo.GetComponent<TextMeshProUGUI>();
        if (fontAsset) tmp.font = fontAsset;
        tmp.fontSize = baseFontSize;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.Top;
        tmp.raycastTarget = false;
        tmp.margin = new Vector4(0, 0, 0, 0);
        tmp.richText = true;
        tmp.text = "";

        var tmpRt = tmp.GetComponent<RectTransform>();
        tmpRt.anchorMin = tmpRt.anchorMax = new Vector2(0.5f, 1f);
        tmpRt.pivot = new Vector2(0.5f, 1f);
        tmpRt.sizeDelta = new Vector2(columnWidth, targetArea.rect.height + 400f);
        tmpRt.anchoredPosition = Vector2.zero;

        var col = go.AddComponent<MatrixColumn>();
        col.Setup(tmp, targetArea, charset, lengthRange, speedRange,
                  charRefreshInterval, shuffleRatio, headColor, tailColor,
                  pixelPerLine, desync);
        return col;
    }

    void HandleGameCompleted()
    {
        if (!_isFading)
            StartCoroutine(FadeOutAndDisable());
    }

    IEnumerator FadeOutAndDisable()
    {
        _isFading = true;

        float startAlpha = _canvasGroup ? _canvasGroup.alpha : 1f;
        float t = 0f;

        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeOutDuration);

            if (_canvasGroup)
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, k);

            yield return null;
        }

        if (_canvasGroup)
            _canvasGroup.alpha = 0f;

        // désactive tous les MatrixColumn
        foreach (var c in pool) if (c) c.enabled = false;

        // désactive aussi ce contrôleur
        this.enabled = false;
        // Si tu veux le masquer totalement :
        // gameObject.SetActive(false);
    }

    public void ApplyRuntime()
    {
        foreach (var c in pool) if (c) c.ApplyRuntime(headColor, tailColor, charRefreshInterval, shuffleRatio);
    }
}
