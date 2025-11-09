using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class FlagSeries
{
    [Tooltip("6 noms de fichiers (sans extension) dans Resources/Flags/")]
    public string[] options = new string[6];
}

public class KosmoGameManager : MonoBehaviour
{
    [Header("UI (6 éléments chacun, gauche→droite)")]
    public RawImage[] slots = new RawImage[6];
    public Button[] buttons = new Button[6];      // optionnel : pour tests locaux

    [Header("Couleurs / Durées")]
    public Color correctTint = new Color(0.7f, 1f, 0.7f, 1f); // vert doux
    public Color grayTint = new Color(0.35f, 0.35f, 0.35f, 1f);
    public float feedbackDuration = 4f;

    [Header("Séries (Son 1 → Son 10)")]
    public List<FlagSeries> series = new List<FlagSeries>();

    [Header("Progress Bar Circulaire (au centre)")]
    public GameObject progressRoot;    // un GameObject parent du module (fond + remplissage)
    public Image progressFill;         // Image type Filled, Fill Method = Radial360
    public Image progressBack;         // (optionnel) fond circulaire derrière
    public float progressAnimDuration = 0.6f;  // animation d’un palier (0.6 s)
    public float progressCompleteHold = 3f;   // temps à 100% avant disparition

    // état interne
    int _currentSonIndex = -1;      // 0..9, -1 = rien affiché
    bool _busy = false;             // évite double anim de feedback
    int _score = 0;                 // 0..4
    Coroutine _progressCo;          // anim en cours
    bool _progressHidden = true;

    void Awake()
    {
        // Pré-remplissage depuis ta PJ (noms sans accents)
        series = new List<FlagSeries>
        {
            new FlagSeries { options = new [] { "Islande", "Pologne", "Turquie", "Estonie", "Kenya", "Indonesie" } },
            new FlagSeries { options = new [] { "Turquie", "Hongrie", "Finlande", "Vietnam", "Indonesie", "Grece" } },
            new FlagSeries { options = new [] { "Islande", "Pologne", "Finlande", "Estonie", "Indonesie", "Vietnam" } },
            new FlagSeries { options = new [] { "Estonie", "Vietnam", "Indonesie", "Turquie", "Kenya", "Hongrie" } },
            new FlagSeries { options = new [] { "Grece", "Hongrie", "Finlande", "Estonie", "Pologne", "Kenya" } },
            new FlagSeries { options = new [] { "Pologne", "Finlande", "Islande", "Kenya", "Grece", "Vietnam" } },
            new FlagSeries { options = new [] { "Hongrie", "Turquie", "Estonie", "Kenya", "Vietnam", "Islande" } },
            new FlagSeries { options = new [] { "Indonesie", "Grece", "Islande", "Kenya", "Vietnam", "Turquie" } },
            new FlagSeries { options = new [] { "Estonie", "Finlande", "Islande", "Pologne", "Hongrie", "Grece" } },
            new FlagSeries { options = new [] { "Pologne", "Finlande", "Grece", "Turquie", "Indonesie", "Hongrie" } },
        };

        // tests locaux (clic = success)
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            if (buttons[i] != null)
                buttons[i].onClick.AddListener(() => OnExternalSuccess(idx + 1));
        }

        HideAll();
        SetupProgressModule();
        SetProgress01(0f, immediate: true);
        HideProgress(immediate: true);
    }

    // ============================ API appelée par le Bridge ============================

    /// <summary>Affiche la série demandée par "Son &lt;n&gt;" (n = 1..10)</summary>
    public void OnSon(int oneBasedNumber)
    {
        if (_busy) return; // on ignore si feedback en cours

        if (_score >= 4)
        {
            // Nouvelle partie : reset score → barre à 0 et réaffiche le module
            _score = 0;
            ShowProgress();
            SetProgress01(0f, immediate: true);
        }
        else
        {
            // si la barre est cachée (début de partie), on la montre
            ShowProgress();
        }

        int idx = Mathf.Clamp(oneBasedNumber - 1, 0, series.Count - 1);
        _currentSonIndex = idx;
        ShowSeries(idx);
    }

    /// <summary>Message "success &lt;n&gt;" → vert sur le drapeau n, autres N&B, +25% de progression.</summary>
    public void OnExternalSuccess(int oneBasedFlagIndex)
    {
        if (_busy || _currentSonIndex < 0) return;
        if (_score >= 4) return; // partie déjà finie

        int zero = Mathf.Clamp(oneBasedFlagIndex - 1, 0, 5);

        for (int i = 0; i < 6; i++)
            if (slots[i]) slots[i].color = (i == zero) ? correctTint : grayTint;

        _score = Mathf.Min(_score + 1, 4);
        AnimateProgressToScore();

        StartCoroutine(ClearAfter(feedbackDuration));
    }

    /// <summary>Message "fail" → tout en N&B, progression inchangée.</summary>
    public void OnExternalFail()
    {
        if (_busy || _currentSonIndex < 0) return;
        if (_score >= 4) return;

        for (int i = 0; i < 6; i++)
            if (slots[i]) slots[i].color = grayTint;

        StartCoroutine(ClearAfter(feedbackDuration));
    }

    // ============================ Progress UI ============================

    void SetupProgressModule()
    {
        if (progressFill)
        {
            // Assure-toi que l'Image est bien en Filled/Radial360 dans l’inspecteur
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Radial360;
            progressFill.fillOrigin = (int)Image.Origin360.Top;
            progressFill.fillClockwise = true;
        }
    }

    void ShowProgress()
    {
        if (!progressRoot) return;
        if (!_progressHidden) return;
        progressRoot.SetActive(true);
        _progressHidden = false;
    }

    void HideProgress(bool immediate = false)
    {
        if (!progressRoot) return;
        if (_progressHidden) return;

        if (_progressCo != null) StopCoroutine(_progressCo);
        if (immediate) progressRoot.SetActive(false);
        else StartCoroutine(HideProgressDelayed(0f));
        _progressHidden = true;
    }

    IEnumerator HideProgressDelayed(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (progressRoot) progressRoot.SetActive(false);
    }

    void AnimateProgressToScore()
    {
        float target01 = Mathf.Clamp01(_score / 4f);
        SetProgress01(target01, immediate: false);

        if (_score >= 4)
        {
            // à 100% : maintenir 3 s puis masquer
            StartCoroutine(HoldThenHideProgress(progressCompleteHold));
        }
    }

    void SetProgress01(float target01, bool immediate)
    {
        if (!progressFill) return;

        if (_progressCo != null) StopCoroutine(_progressCo);
        if (immediate)
        {
            progressFill.fillAmount = target01;
        }
        else
        {
            _progressCo = StartCoroutine(AnimateFill(progressFill.fillAmount, target01, progressAnimDuration));
        }
    }

    IEnumerator AnimateFill(float from, float to, float dur)
    {
        // easing doux (easeInOutCubic)
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = (k < 0.5f) ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
            progressFill.fillAmount = Mathf.LerpUnclamped(from, to, k);
            yield return null;
        }
        progressFill.fillAmount = to;
        _progressCo = null;
    }

    IEnumerator HoldThenHideProgress(float hold)
    {
        yield return new WaitForSeconds(hold);
        HideProgress(immediate: false); // masque le module (SetActive false)
    }

    // ============================ Affichage des drapeaux ============================

    void SetVisible(RawImage img, bool on)
    {
        if (!img) return;
        img.enabled = on;             // vrai invisible (sinon carré blanc)
        img.raycastTarget = on;
    }

    void ShowSeries(int sIndex)
    {
        var s = series[sIndex];

        for (int i = 0; i < 6; i++)
        {
            var img = slots[i];
            if (!img) continue;

            var name = s.options[i];
            var tex = Resources.Load<Texture2D>("Flags/" + name);

            img.texture = tex;
            img.color = Color.white;
            SetVisible(img, tex != null);
        }

        SetButtonsInteractable(false); // on attend l’input externe
    }

    IEnumerator ClearAfter(float sec)
    {
        _busy = true;
        yield return new WaitForSeconds(sec);
        HideAll();
        _currentSonIndex = -1; // attend un prochain "Son <n>"
        _busy = false;
    }

    void HideAll()
    {
        for (int i = 0; i < 6; i++)
        {
            var img = slots[i];
            if (!img) continue;

            img.texture = null;
            img.color = Color.white;
            SetVisible(img, false);   // pas de carré blanc
        }
        SetButtonsInteractable(false);
    }

    void SetButtonsInteractable(bool on)
    {
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i]) buttons[i].interactable = on;
    }

    // ---------- compat avec ancien bridge ----------
    public void OnRemotePick(int oneBasedButton) => OnExternalSuccess(oneBasedButton);
}
