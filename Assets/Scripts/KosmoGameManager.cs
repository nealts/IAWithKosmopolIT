using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

[System.Serializable]
public class FlagSeries
{
    public string[] options = new string[6];
    public int correctIndex = 0;
    [HideInInspector] public bool solved = false;
}

public class KosmoGameManager : MonoBehaviour
{
    public static event Action<int, int> OnKosmoProgress;
    [Header("UI refs (6 chacun)")]
    public RawImage[] slots = new RawImage[6];
    public Button[] buttons = new Button[6];

    [Header("Feedback")]
    public Sprite failSprite;   // Resources/Images/CroixRouge
    public Sprite winSprite;    // Resources/Images/CocheVerte
    public Color correctTint = new Color(0.75f, 1f, 0.75f, 1f);
    public float feedbackDelay = 3f;

    [Header("Progress")]
    public Image progressFill;                   // cercle (Image fill)
    public Slider progressSlider;                // optionnel
    public TextMeshProUGUI progressPercentTMP;   // affiche % (optionnel)
    public int progressMaxWins = 4;
    public float progressAnimTime = 0.6f;        // lissage
    public float progressHideDelay = 1.5f;       // délai après 100%
    public float progressAutoHideSeconds = 0f;   // 0 = jamais

    [Header("Décor / Bandeau & cadres")]
    public Vector2 backdropOffset = new Vector2(0f, -350f); // réglable dans l’inspector
    [Range(0.05f, 1f)] public float backdropHeightPercent = 0.33f;
    public Vector2 backdropScale = new Vector2(1f, 1f);
    public Vector2 innerScale = new Vector2(0.88f, 0.88f);  // bord noir (proche du drapeau)
    public Vector2 outerScale = new Vector2(1.04f, 1.04f);  // bord couleur (éloigné du drapeau)
    public float outerCornerRadius = 0f;                     // 0 = rectangulaire

    [Header("Séries (laisser vide pour utiliser l’intégré)")]
    public List<FlagSeries> series = new List<FlagSeries>();

    // --- état
    public static event Action GameCompleted;
    int _currentSeriesIndex = -1;
    bool _busy = false;
    bool _readyForQueuedSwitch = false;
    int? _queuedNextSeriesOneBased = null;
    bool _completionNotified = false;

    // overlays
    GameObject _bigCrossGO;
    GameObject _bigCheckGO;

    // décor
    Image _backdrop;
    readonly Image[] _inner = new Image[6]; // bord noir (près du drapeau)
    readonly Image[] _outer = new Image[6]; // bord couleur (loin du drapeau)

    // progress
    int _progressWins = 0;
    Coroutine _progressAnimCo;
    Coroutine _hideProgressCoroutine;
    float _smoothFill = 0f;

    // Props anims
    public GameObject dna2D;
    public GameObject fakeDNA;
    public GameObject ProgressRoot;
    // =================== LIFECYCLE ===================

    void Awake()
    {
        EnsureSeries();
        if (dna2D) dna2D.SetActive(false);
        // branchement des boutons
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            if (buttons[i] != null) buttons[i].onClick.AddListener(() => OnLocalPick(idx));
        }

        if (!failSprite) failSprite = Resources.Load<Sprite>("Images/CroixRouge");
        if (!winSprite) winSprite = Resources.Load<Sprite>("Images/CocheVerte");

        EnsureBackdropAndFrames();

        SetSlotsVisible(false);
        SetButtonsInteractable(false);
        ResetProgressUI();
        ApplyProgressUI(false);

        Debug.Log("[Kosmo] Ready. Waiting for 'Son X'.");
    }

    // =================== ADAPTERS pour KosmoNodeRedBridge ===================
    // -> Tu peux garder le bridge tel quel.
    public void OnOutcome(bool win, int? selectedOneBased)
    {
        if (win) OnExternalSuccess(selectedOneBased ?? 0);
        else OnExternalFail();
    }

    public void QueueNextSeries(int oneBased)
    {
        OnSon(oneBased);
    }

    public void OnPick(int optionIndex)
    {
        OnLocalPick(optionIndex);
    }

    // =================== API 'externe' (Bridge) ===================
    public void OnSon(int oneBased)
    {
        int clamped = Mathf.Clamp(oneBased, 1, Mathf.Max(1, series.Count));
        Debug.Log($"[Kosmo] Son received: {clamped}");

        if (!_busy && (_currentSeriesIndex < 0 || _readyForQueuedSwitch))
        {
            _readyForQueuedSwitch = false;
            _queuedNextSeriesOneBased = null;
            ShowSeries(clamped - 1);
        }
        else
        {
            _queuedNextSeriesOneBased = clamped;
            Debug.Log("[Kosmo] Son queued.");
        }
    }

    public void OnExternalSuccess(int oneBased)
    {
        if (_busy || _currentSeriesIndex < 0) return;

        var s = series[_currentSeriesIndex];
        int idx = Mathf.Clamp((oneBased > 0) ? oneBased - 1 : s.correctIndex, 0, 5);

        if (IsSlotValid(idx) && slots[idx]) slots[idx].color = correctTint;
        s.solved = true;
        _progressWins = Mathf.Min(progressMaxWins, _progressWins + 1);
        ApplyProgressUI(true);
        OnKosmoProgress?.Invoke(_progressWins, progressMaxWins);
        if (!_completionNotified && _progressWins >= progressMaxWins)
        {
            _completionNotified = true;
            GameCompleted?.Invoke();
        }
        PlaceBigGreenCheck();

        SetButtonsInteractable(false);
        StartCoroutine(FeedbackThenWait(feedbackDelay));
    }

    public void OnExternalFail()
    {
        if (_busy || _currentSeriesIndex < 0) return;

        PlaceBigRedCross();
        SetButtonsInteractable(false);
        StartCoroutine(FeedbackThenWait(feedbackDelay));
    }

    // =================== Clic local (UI) ===================
    void OnLocalPick(int optionIndex)
    {
        if (_busy || _currentSeriesIndex < 0) return;

        var s = series[_currentSeriesIndex];
        bool win = (optionIndex == s.correctIndex);

        if (win)
        {
            if (IsSlotValid(optionIndex) && slots[optionIndex]) slots[optionIndex].color = correctTint;
            s.solved = true;
            _progressWins = Mathf.Min(progressMaxWins, _progressWins + 1);
            ApplyProgressUI(true);
            OnKosmoProgress?.Invoke(_progressWins, progressMaxWins);
            if (!_completionNotified && _progressWins >= progressMaxWins)
            {
                _completionNotified = true;
                GameCompleted?.Invoke();
            }
            PlaceBigGreenCheck();
        }
        else
        {
            if (IsSlotValid(optionIndex)) PlaceRedCross(slots[optionIndex]);
            PlaceBigRedCross();
        }

        SetButtonsInteractable(false);
        StartCoroutine(FeedbackThenWait(feedbackDelay));
    }

    // =================== Séries ===================
    void ShowSeries(int sIndex)
    {
        if (series == null || series.Count == 0) { Debug.LogError("[Kosmo] No series."); return; }
        sIndex = Mathf.Clamp(sIndex, 0, series.Count - 1);
        var s = series[sIndex];
        if (!IsSeriesValid(s)) { Debug.LogError("[Kosmo] Invalid series."); return; }

        _currentSeriesIndex = sIndex;

        SetSlotsVisible(true);
        ClearFeedback();
        LayoutBackdropAndFrames();

        for (int i = 0; i < 6; i++)
        {
            if (!IsSlotValid(i) || slots[i] == null) continue;
            string name = (s.options[i] ?? "").Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var spr = Resources.Load<Sprite>("Flags/" + name);
            if (spr) { slots[i].texture = spr.texture; continue; }
            var tex = Resources.Load<Texture2D>("Flags/" + name);
            if (tex) { slots[i].texture = tex; continue; }

            Debug.LogError($"[Kosmo] Missing flag: {name}");
        }

        SetButtonsInteractable(true);
        Debug.Log($"[Kosmo] Show series #{sIndex + 1}");
    }

    IEnumerator FeedbackThenWait(float sec)
    {
        _busy = true;
        _readyForQueuedSwitch = false;
        yield return new WaitForSeconds(sec);
        _busy = false;

        if (_queuedNextSeriesOneBased.HasValue)
        {
            int next = Mathf.Clamp(_queuedNextSeriesOneBased.Value - 1, 0, series.Count - 1);
            _queuedNextSeriesOneBased = null;
            ShowSeries(next);
        }
        else
        {
            _readyForQueuedSwitch = true;
            SetSlotsVisible(false);
            Debug.Log("[Kosmo] Waiting for next Son...");
        }
    }

    // =================== Feedback visuel ===================
    void PlaceRedCross(RawImage target)
    {
        if (!target) return;
        var go = new GameObject("X", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(target.transform, false);
        var img = go.GetComponent<Image>();
        img.sprite = failSprite;
        img.color = Color.white;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }



    void PlaceBigRedCross()
    {
        DestroyBigOverlays();
        _bigCrossGO = CreateCenteredOverlay(failSprite, Color.white);
    }

    void PlaceBigGreenCheck()
    {
        DestroyBigOverlays();
        _bigCheckGO = CreateCenteredOverlay(winSprite, Color.white);
    }

    GameObject CreateCenteredOverlay(Sprite sprite, Color color)
    {
        Transform parent = (slots != null && slots.Length > 0 && slots[0]) ? slots[0].transform.parent : this.transform;
        var go = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.preserveAspect = true;
        img.raycastTarget = false;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(400f, 400f);
        rt.anchoredPosition = Vector2.zero;
        rt.anchoredPosition = new Vector2(0f, 155f);
        go.transform.SetAsLastSibling();
        return go;
    }

    void DestroyBigOverlays()
    {
        if (_bigCrossGO) { Destroy(_bigCrossGO); _bigCrossGO = null; }
        if (_bigCheckGO) { Destroy(_bigCheckGO); _bigCheckGO = null; }
    }

    void ClearFeedback()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!IsSlotValid(i) || slots[i] == null) continue;
            slots[i].color = Color.white;
            var x = slots[i].transform.Find("X");
            if (x) Destroy(x.gameObject);
        }
        DestroyBigOverlays();
    }

    // =================== Décor ===================
    void EnsureBackdropAndFrames()
    {
        Transform parent = (slots != null && slots.Length > 0 && slots[0]) ? slots[0].transform.parent : this.transform;

        // Backdrop
        if (_backdrop == null)
        {
            var go = new GameObject("FlagsBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _backdrop = go.GetComponent<Image>();
            _backdrop.color = new Color32(0x1D, 0x1D, 0x1D, 255); // opaque
            _backdrop.raycastTarget = false;
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
        }

        // cadres inner/outer
        for (int i = 0; i < 6; i++)
        {
            if (_inner[i] == null && IsSlotValid(i) && slots[i] != null)
            {
                var innerGo = new GameObject($"Inner{i + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var img = innerGo.GetComponent<Image>();
                img.color = Color.black; // bord noir
                img.raycastTarget = false;
                innerGo.transform.SetParent(parent, false);
                _inner[i] = img;
            }

            if (_outer[i] == null && IsSlotValid(i) && slots[i] != null)
            {
                var outerGo = new GameObject($"Outer{i + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var img = outerGo.GetComponent<Image>();
                img.color = (i == 5) ? Color.black : GetButtonColor(i); // dernier = blanc
                img.raycastTarget = false;
                outerGo.transform.SetParent(parent, false);
                _outer[i] = img;
            }
        }

        LayoutBackdropAndFrames();
    }

    void LayoutBackdropAndFrames()
    {
        Transform parent = (slots != null && slots.Length > 0 && slots[0]) ? slots[0].transform.parent : this.transform;
        RectTransform prt = parent as RectTransform;
        float parentH = prt ? prt.rect.height : Screen.height;

        // Backdrop
        if (_backdrop)
        {
            var rt = _backdrop.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, Mathf.Round(parentH * Mathf.Clamp01(backdropHeightPercent)));
            rt.anchoredPosition = backdropOffset;
            rt.localScale = new Vector3(backdropScale.x, backdropScale.y, 1f);
            _backdrop.color = new Color32(0x1D, 0x1D, 0x1D, 255);
            _backdrop.transform.SetAsFirstSibling();
        }

        // cadres : OUTER (couleur) plus grand, INNER (noir) plus petit, puis FLAG
        for (int i = 0; i < 6; i++)
        {
            if (!IsSlotValid(i) || slots[i] == null || _outer[i] == null || _inner[i] == null) continue;

            var srt = slots[i].rectTransform;

            var ort = _outer[i].rectTransform;
            ort.anchorMin = srt.anchorMin;
            ort.anchorMax = srt.anchorMax;
            ort.pivot = srt.pivot;
            ort.anchoredPosition = srt.anchoredPosition;
            ort.sizeDelta = srt.sizeDelta;
            ort.localScale = new Vector3(outerScale.x, outerScale.y, 1f);

            var irt = _inner[i].rectTransform;
            irt.anchorMin = srt.anchorMin;
            irt.anchorMax = srt.anchorMax;
            irt.pivot = srt.pivot;
            irt.anchoredPosition = srt.anchoredPosition;
            irt.sizeDelta = srt.sizeDelta;
            irt.localScale = new Vector3(innerScale.x, innerScale.y, 1f);

            // <<< COULEURS >>>
            if (i == 5)       // 6e drapeau : inverse
            {
                _outer[i].color = Color.black;
                _inner[i].color = Color.white;
            }
            else              // les 5 premiers comme avant
            {
                _outer[i].color = GetButtonColor(i);
                _inner[i].color = Color.black;
            }

            int flagSibling = slots[i].transform.GetSiblingIndex();
            _outer[i].transform.SetSiblingIndex(Mathf.Max(0, flagSibling - 2));
            _inner[i].transform.SetSiblingIndex(Mathf.Max(0, flagSibling - 1));
        }
    }

    Color GetButtonColor(int index)
    {
        // Rouge, Bleu, Vert, Jaune, Blanc, Noir
        //Blanc Color.white, Jaune, Vert, Rouge, Bleu, Noir Color.black
        switch (index)
        {

            case 0: return Color.white; // white
            case 1: return new Color(0.98f, 0.85f, 0.15f, 1f); // Jaune
            case 2: return new Color(0.15f, 0.80f, 0.25f, 1f); // vert
            case 3: return new Color(0.10f, 0.45f, 0.95f, 1f); // Bleu
            case 4: return new Color(0.90f, 0.10f, 0.10f, 1f); // Rouge
            default: return Color.black; // Noir
        }
    }

    // =================== Utils & Progress ===================
    void SetSlotsVisible(bool v)
    {
        if (_backdrop) _backdrop.enabled = v;
        for (int i = 0; i < _outer.Length; i++) if (_outer[i]) _outer[i].enabled = v;
        for (int i = 0; i < _inner.Length; i++) if (_inner[i]) _inner[i].enabled = v;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i]) continue;
            slots[i].enabled = v;
            if (!v)
            {
                slots[i].texture = null;
                slots[i].color = Color.white;
                var x = slots[i].transform.Find("X");
                if (x) Destroy(x.gameObject);

            }
        }
        if (!v) DestroyBigOverlays();
    }

    void SetButtonsInteractable(bool v)
    {
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i]) buttons[i].interactable = v;
    }

    bool IsSlotValid(int i) => slots != null && i >= 0 && i < slots.Length;

    void EnsureSeries()
    {
        bool valid = series != null && series.Count == 10;
        if (valid) foreach (var s in series) if (!IsSeriesValid(s)) { valid = false; break; }
        if (!valid) series = GetBuiltinSeries();
    }

    bool IsSeriesValid(FlagSeries s)
    {
        if (s == null || s.options == null || s.options.Length < 6) return false;
        for (int i = 0; i < 6; i++) if (string.IsNullOrWhiteSpace(s.options[i])) return false;
        return s.correctIndex >= 0 && s.correctIndex < 6;
    }

    List<FlagSeries> GetBuiltinSeries()
    {
        return new List<FlagSeries>
        {
            new FlagSeries { options = new [] { "Islande", "Pologne", "Turquie", "Estonie", "Kenya", "Indonesie" }, correctIndex = 1 },
            new FlagSeries { options = new [] { "Turquie", "Hongrie", "Finlande", "Vietnam", "Indonesie", "Grece" }, correctIndex = 4 },
            new FlagSeries { options = new [] { "Islande", "Pologne", "Finlande", "Estonie", "Indonesie", "Vietnam" }, correctIndex = 0 },
            new FlagSeries { options = new [] { "Estonie", "Vietnam", "Indonesie", "Turquie", "Kenya", "Hongrie" }, correctIndex = 5 },
            new FlagSeries { options = new [] { "Grece", "Hongrie", "Finlande", "Estonie", "Pologne", "Kenya" }, correctIndex = 2 },
            new FlagSeries { options = new [] { "Pologne", "Finlande", "Islande", "Kenya", "Grece", "Vietnam" }, correctIndex = 3 },
            new FlagSeries { options = new [] { "Hongrie", "Turquie", "Estonie", "Kenya", "Vietnam", "Islande" }, correctIndex = 1 },
            new FlagSeries { options = new [] { "Indonesie", "Grece", "Islande", "Kenya", "Vietnam", "Turquie" }, correctIndex = 4 },
            new FlagSeries { options = new [] { "Estonie", "Finlande", "Islande", "Pologne", "Hongrie", "Grece" }, correctIndex = 0 },
            new FlagSeries { options = new [] { "Pologne", "Finlande", "Grece", "Turquie", "Indonesie", "Hongrie" }, correctIndex = 2 }
        };
    }

    void ResetProgressUI()
    {
        _smoothFill = 0f;
        if (progressFill) progressFill.fillAmount = 0f;
        if (progressSlider) progressSlider.value = 0f;
        if (progressPercentTMP) { progressPercentTMP.text = "0%"; progressPercentTMP.enabled = false; }
    }

    void ApplyProgressUI(bool animate)
    {
        float target = (progressMaxWins > 0) ? Mathf.Clamp01((float)_progressWins / progressMaxWins) : 0f;

        if (_progressAnimCo != null) StopCoroutine(_progressAnimCo);
        _progressAnimCo = StartCoroutine(AnimateProgressTo(target));

        // cache auto après 100%
        if (Mathf.Approximately(target, 1f) && progressAutoHideSeconds > 0f)
        {
            if (_hideProgressCoroutine != null) StopCoroutine(_hideProgressCoroutine);
            _hideProgressCoroutine = StartCoroutine(HideProgressAfterDelay(progressAutoHideSeconds));
        }
        if (_progressWins >= progressMaxWins && dna2D)
        {
            dna2D.SetActive(true);
            fakeDNA.SetActive(false);
            ProgressRoot.SetActive(false);
        }
    }

    IEnumerator AnimateProgressTo(float target)
    {
        float t0 = _smoothFill;
        float t = 0f;

        // afficher le % seulement a partir de la 1ere victoire
        if (progressPercentTMP) progressPercentTMP.enabled = (_progressWins > 0);

        // push une valeur initiale au hub d effets
        GlitchGroupManager.Instance?.ApplyProgress01(_smoothFill);

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.001f, progressAnimTime);
            _smoothFill = Mathf.Lerp(t0, target, Mathf.SmoothStep(0f, 1f, t));

            if (progressFill) progressFill.fillAmount = _smoothFill;
            if (progressSlider) progressSlider.value = _smoothFill;
            if (progressPercentTMP)
            {
                int pct = Mathf.RoundToInt(_smoothFill * 100f);
                progressPercentTMP.text = pct + "%";
            }

            // >>> notifie le hub d effets en continu
            GlitchGroupManager.Instance?.ApplyProgress01(_smoothFill);

            yield return null;
        }

        _smoothFill = target;
        if (progressFill) progressFill.fillAmount = _smoothFill;
        if (progressSlider) progressSlider.value = _smoothFill;
        if (progressPercentTMP) progressPercentTMP.text = Mathf.RoundToInt(_smoothFill * 100f) + "%";

        // un dernier push pour la valeur finale (utile pour 100%)
        GlitchGroupManager.Instance?.ApplyProgress01(_smoothFill);
    }

    public void ForceWin()
    {
        // On force le compteur à la valeur max
        _progressWins = progressMaxWins;
        ApplyProgressUI(true);

        // Si tu utilises déjà le système de "4/4 atteint" (GameCompleted),
        // on le déclenche ici aussi pour avoir exactement le même comportement.
#if UNITY_EDITOR || true
        try
        {
            // Si tu as ajouté dans un précédent message :
            // public static event Action GameCompleted;
            // bool _completionNotified;
            var field = typeof(KosmoGameManager).GetField("_completionNotified",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ev = typeof(KosmoGameManager).GetField("GameCompleted",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (field != null && ev != null)
            {
                bool already = (bool)field.GetValue(this);
                if (!already)
                {
                    field.SetValue(this, true);
                    var del = (System.Delegate)ev.GetValue(null);
                    del?.DynamicInvoke();
                }
            }
        }
        catch
        {
            // Si tu n'as pas encore mis en place GameCompleted/_completionNotified,
            // ça ne casse rien, on ignore.
        }
#endif
    }
    IEnumerator HideProgressAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // met tous les effets en etat 100% (la plupart des presets les coupent a 100)
        GlitchGroupManager.Instance?.ApplyProgress01(1f);

        if (progressPercentTMP) progressPercentTMP.enabled = false;
        // si tu veux aussi cacher le fill/slider, de-commente:
        // if (progressFill) progressFill.enabled = false;
        // if (progressSlider) progressSlider.gameObject.SetActive(false);
    }
}
