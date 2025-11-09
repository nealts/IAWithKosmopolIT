using UnityEngine;
using TMPro;

public class MatrixColumn : MonoBehaviour
{
    // externes (manager)
    private TextMeshProUGUI tmp;
    private RectTransform area;
    private string charset;
    private Vector2Int lengthRange;
    private Vector2 speedRange;
    private float refreshInterval;
    private float shuffleRatio;
    private Color headColor, tailColor;
    private float pixelPerLine;
    private bool desync;

    // état
    private RectTransform rt;
    private float speed;
    private int length;
    private float refreshTimer;
    private float heightStart, heightEnd;

    private System.Text.StringBuilder sb = new System.Text.StringBuilder(256);

    public void Setup(
        TextMeshProUGUI tmp,
        RectTransform area,
        string charset,
        Vector2Int lengthRange,
        Vector2 speedRange,
        float refreshInterval,
        float shuffleRatio,
        Color headColor,
        Color tailColor,
        float pixelPerLine,
        bool desync
    )
    {
        this.tmp = tmp;
        this.area = area;
        this.charset = string.IsNullOrEmpty(charset) ? "01" : charset;
        this.lengthRange = new Vector2Int(Mathf.Max(2, lengthRange.x), Mathf.Max(lengthRange.x + 1, lengthRange.y));
        this.speedRange = new Vector2(Mathf.Max(1f, speedRange.x), Mathf.Max(speedRange.x + 1f, speedRange.y));
        this.refreshInterval = Mathf.Clamp(refreshInterval, 0.02f, 0.5f);
        this.shuffleRatio = Mathf.Clamp01(shuffleRatio);
        this.headColor = headColor;
        this.tailColor = tailColor;
        this.pixelPerLine = Mathf.Max(1f, pixelPerLine);
        this.desync = desync;

        rt = GetComponent<RectTransform>();

        heightStart = area.rect.height * 0.5f + 100f;
        heightEnd = -area.rect.height * 0.5f - 100f;

        ResetColumn(true);
        if (desync) refreshTimer = Random.Range(0f, refreshInterval);
    }

    public void ApplyRuntime(Color head, Color tail, float interval, float ratio)
    {
        headColor = head;
        tailColor = tail;
        refreshInterval = Mathf.Clamp(interval, 0.02f, 0.5f);
        shuffleRatio = Mathf.Clamp01(ratio);
    }

    void ResetColumn(bool randomX = false)
    {
        length = Random.Range(lengthRange.x, lengthRange.y);
        speed = Random.Range(speedRange.x, speedRange.y);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, heightStart + Random.Range(0f, area.rect.height * 0.4f));
        RebuildString(true);
    }

    void Update()
    {
        // descente
        var p = rt.anchoredPosition;
        p.y -= speed * Time.deltaTime;
        rt.anchoredPosition = p;

        // boucle si sortie de l'écran
        if (p.y < heightEnd - length * pixelPerLine)
            ResetColumn();

        // rafraîchissement des caractères
        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            RebuildString(false);
        }
    }

    // Construit une string verticale avec couleurs par caractère
    void RebuildString(bool full)
    {
        if (tmp == null) return;

        // On reconstruit soit totalement, soit partiellement (shuffleRatio)
        int toReplace = full ? length : Mathf.Max(1, Mathf.RoundToInt(length * shuffleRatio));

        // Build nouvelle ligne
        sb.Clear();
        for (int i = 0; i < length; i++)
        {
            // Head = tout en haut (premier glyph)
            float t = (length <= 1) ? 1f : (1f - (i / (length - 1f)));
            Color c = Color.Lerp(tailColor, headColor, t);
            string hex = ColorUtility.ToHtmlStringRGBA(c);

            char ch = RandomGlyph();

            // <color=#RRGGBBAA>X</color>\n
            sb.Append("<color=#").Append(hex).Append('>').Append(ch).Append("</color>");
            if (i < length - 1) sb.Append('\n');
        }

        tmp.text = sb.ToString();
    }

    char RandomGlyph()
    {
        // ignore les espaces multiples pour éviter trop de trous
        char ch;
        int guard = 0;
        do
        {
            ch = charset[Random.Range(0, charset.Length)];
            guard++;
            if (guard > 16) break;
        }
        while (ch == ' ' && Random.value < 0.5f);
        return ch;
    }
}
