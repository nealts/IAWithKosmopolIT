using System.Linq;
using TMPro;
using UnityEngine;

public class MissionUIController : MonoBehaviour
{
    [Header("Refs")]
    public MissionEngine engine;

    [Header("UI - Réparations en cours (4 slots max dans la scène)")]
    public TextMeshProUGUI[] ongoingSlots = new TextMeshProUGUI[4];

    [Header("UI - Missions terminées (6 max)")]
    public TextMeshProUGUI[] doneSlots = new TextMeshProUGUI[6];

    void Awake()
    {
        if (!engine) engine = GetComponent<MissionEngine>();
    }

    void OnEnable()
    {
        if (engine != null)
            engine.OnVisibleChanged += OnVisibleChanged;

        // au cas où on active en plein milieu
        RefreshAll();
    }

    void OnDisable()
    {
        if (engine != null)
            engine.OnVisibleChanged -= OnVisibleChanged;
    }

    void OnVisibleChanged(System.Collections.Generic.List<string> prev, System.Collections.Generic.List<string> now)
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (engine == null) return;

        var visible = engine.GetVisible();
        var done = engine.GetDone().ToArray();

        // Ongoing
        for (int i = 0; i < ongoingSlots.Length; i++)
        {
            var t = ongoingSlots[i];
            if (!t) continue;

            var root = SlotRoot(t);
            bool show = i < visible.Count;

            if (root && root.activeSelf != show) root.SetActive(show);
            t.text = show ? TitleOf(visible[i]) : "";
        }

        // Done
        for (int i = 0; i < doneSlots.Length; i++)
        {
            var t = doneSlots[i];
            if (!t) continue;

            var root = t.transform.parent ? t.transform.parent.gameObject : t.gameObject;
            bool show = i < done.Length;

            if (root && root.activeSelf != show) root.SetActive(show);
            t.text = show ? TitleOf(done[i]) : "";
        }
    }

    string TitleOf(string id)
    {
        if (engine == null || engine.missions == null) return id;
        return engine.missions.FirstOrDefault(m => m.id == id)?.title ?? id;
    }

    GameObject SlotRoot(TextMeshProUGUI t)
    {
        if (!t) return null;
        var p = t.transform.parent as RectTransform;
        return p ? p.gameObject : t.gameObject;
    }
}