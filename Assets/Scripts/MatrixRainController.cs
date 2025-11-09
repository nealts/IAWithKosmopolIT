using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MatrixRainController : MonoBehaviour
{
    [Header("Zone")]
    [SerializeField] private RectTransform targetArea;          // Laisse vide -> prend le RectTransform du GameObject
    [SerializeField] private bool addRectMaskToClip = true;     // Ajoute automatiquement un RectMask2D

    [Header("Police & rendu")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [Min(6)] public int baseFontSize = 22;
    [Tooltip("Espacement vertical entre deux glyphes (1 = collé)")]
    [Range(0.8f, 1.6f)] public float lineSpacing = 1.05f;
    [Tooltip("Largeur d'une colonne en pixels (≈ largeur du glyphe)")]
    [Min(8f)] public float columnWidth = 18f;
    [Tooltip("Marge latérale intérieure en pixels")]
    public float sidePadding = 8f;

    [Header("Colonnes")]
    [Tooltip("Nombre de colonnes. 0 = auto selon la largeur")]
    public int columns = 0;
    [Tooltip("Nombre maximum de colonnes si 'auto'")]
    [Min(1)] public int maxColumnsAuto = 160;

    [Header("Vitesses & longueurs")]
    public Vector2 speedRange = new Vector2(60f, 220f);         // px/sec
    public Vector2Int lengthRange = new Vector2Int(8, 28);      // nb de caractères par colonne

    [Header("Couleurs")]
    public Color headColor = new Color(0.8f, 1f, 0.8f, 1f);
    public Color tailColor = new Color(0.0f, 0.9f, 0.3f, 0.35f);

    [Header("Comportement")]
    [Tooltip("Fréquence de rafraîchissement des caractères de chaque colonne")]
    [Range(0.02f, 0.5f)] public float charRefreshInterval = 0.06f;
    [Tooltip("Taux de randomisation partielle (0 = fixe, 1 = remplace tout à chaque tick)")]
    [Range(0f, 1f)] public float shuffleRatio = 0.35f;
    [Tooltip("Décaler les vitesses pour éviter l'uniformité")]
    public bool desync = true;

    [Header("Jeu de caractères")]
    [TextArea(1, 3)]
    public string charset = "01 2345 6789 ABCDEF ";

    // --- internes ---
    private RectTransform rt;
    private readonly List<MatrixColumn> pool = new();
    private float pixelPerLine;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        if (!targetArea) targetArea = rt;

        if (addRectMaskToClip && !GetComponent<RectMask2D>())
            gameObject.AddComponent<RectMask2D>();

        if (fontAsset == null)
        {
            Debug.LogWarning("MatrixRainController: aucun TMP_FontAsset défini, je prends la police par défaut.");
        }

        Build();
    }

    void OnEnable()
    {
        foreach (var c in pool) if (c) c.enabled = true;
    }

    void OnDisable()
    {
        foreach (var c in pool) if (c) c.enabled = false;
    }

    void OnDestroy()
    {
        foreach (var c in pool)
        {
            if (c) Destroy(c.gameObject);
        }
        pool.Clear();
    }

    [ContextMenu("Rebuild")]
    public void Build()
    {
        // nettoyage
        foreach (var c in pool) if (c) Destroy(c.gameObject);
        pool.Clear();

        // calculs géométrie
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

    private MatrixColumn CreateColumn(float localX)
    {
        var go = new GameObject("MatrixColumn", typeof(RectTransform));
        go.transform.SetParent(targetArea, false);

        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(columnWidth, targetArea.rect.height + 200f); // un peu plus haut pour spawner offscreen
        r.anchoredPosition = new Vector2(localX, Random.Range(50f, targetArea.rect.height * 0.5f));

        var tmpGo = new GameObject("TMP", typeof(TextMeshProUGUI));
        tmpGo.transform.SetParent(go.transform, false);

        var tmp = tmpGo.GetComponent<TextMeshProUGUI>();
        if (fontAsset) tmp.font = fontAsset;
        tmp.fontSize = baseFontSize;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;   // (optionnel) évite la coupure
        tmp.alignment = TextAlignmentOptions.Top;
        tmp.raycastTarget = false;
        tmp.margin = new Vector4(0, 0, 0, 0);
        tmp.richText = true; // on colore chaque char
        tmp.text = "";

        var tmpRt = tmp.GetComponent<RectTransform>();
        tmpRt.anchorMin = tmpRt.anchorMax = new Vector2(0.5f, 1f);
        tmpRt.pivot = new Vector2(0.5f, 1f);
        tmpRt.sizeDelta = new Vector2(columnWidth, targetArea.rect.height + 400f);
        tmpRt.anchoredPosition = Vector2.zero;

        var col = go.AddComponent<MatrixColumn>();
        col.Setup(
            tmp,
            targetArea,
            charset,
            lengthRange,
            speedRange,
            charRefreshInterval,
            shuffleRatio,
            headColor,
            tailColor,
            pixelPerLine,
            desync
        );

        return col;
    }

    // Appelle ça si tu modifies des paramètres en Play (ex: couleurs, vitesse)
    public void ApplyRuntime()
    {
        foreach (var c in pool) if (c) c.ApplyRuntime(headColor, tailColor, charRefreshInterval, shuffleRatio);
    }
}
