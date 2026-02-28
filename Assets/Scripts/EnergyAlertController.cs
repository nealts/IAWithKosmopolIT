using System;
using UnityEngine;

public class EnergyAlertController : MonoBehaviour
{
    public static event Action HippoAlertTriggered;

    [Header("Références")]
    public DNA2DAnimator dna;
    public WSAlerts wsAlerts;
    public GameObject lowEnergyBG;

    [Header("Trigger automatique")]
    [Tooltip("Quand ce GameObject se désactive, la séquence démarre automatiquement")]
    public GameObject fakeADN;

    [Header("Modules à activer")]
    [Tooltip("Sera activé quand l'énergie atteint 100% pour la première fois")]
    public WSMissionsBoard missionsBoard;

    [Header("Seuils énergie")]
    [Range(0f, 1f)] public float thresholdLow = 0.20f;
    [Range(0f, 1f)] public float thresholdZero = 0.00f;

    [Header("Blink Settings")]
    public float minBlinkSpeed = 0.2f;
    public float maxBlinkSpeed = 6.0f;
    public float alphaMin = 0.0f;
    public float alphaMax = 1.0f;

    [Header("GM - Grignotage")]
    [Tooltip("Perte d'énergie par commande 'grignotage' (en %, ex: 2 = -2%)")]
    public float grignotagePercent = 2f;

    // ---- état interne ----
    enum EnergyState { Idle, Charging, Draining }
    EnergyState _state = EnergyState.Idle;

    bool _fakeWasActive = true;
    bool _hippoAlertSent = false;
    bool _firstChargeDone = false;
    float _blinkTimer = 0f;

    CanvasGroup _bgCg;

    // -------------------------------------------------------
    void Start()
    {
        if (lowEnergyBG)
        {
            lowEnergyBG.SetActive(false);
            _bgCg = lowEnergyBG.GetComponent<CanvasGroup>();
            if (!_bgCg) _bgCg = lowEnergyBG.AddComponent<CanvasGroup>();
            _bgCg.alpha = 0f;
        }

        if (fakeADN)
        {
            _fakeWasActive = fakeADN.activeSelf;
            Debug.Log($"[Energy] Start → FakeADN actif={_fakeWasActive}");

            // Si FakeADN déjà désactivé au démarrage → on lance directement
            if (!_fakeWasActive)
                StartEnergySequence();
        }
        else
        {
            Debug.LogWarning("[Energy] Start → FakeADN NON assigné !");
        }
    }

    // -------------------------------------------------------
    // Démarre la séquence : passe en Charging, attend que les joueurs rechargent
    // -------------------------------------------------------
    public void StartEnergySequence()
    {
        if (_state != EnergyState.Idle)
        {
            Debug.LogWarning("[Energy] StartEnergySequence ignoré : séquence déjà en cours.");
            return;
        }

        if (!dna)
        {
            Debug.LogError("[Energy] DNA2DAnimator non assigné.");
            return;
        }

        dna.energy = 0f;
        _hippoAlertSent = false;
        TriggerHippoAlert();
        _state = EnergyState.Charging;
        Debug.Log("[Energy] Séquence démarrée → énergie à 0%, Hippo déclenchée, attente recharge.");
    }

    // -------------------------------------------------------
    void Update()
    {
        // Détection automatique FakeADN
        if (_state == EnergyState.Idle && fakeADN != null)
        {
            bool fakeActive = fakeADN.activeSelf;
            if (_fakeWasActive && !fakeActive)
                StartEnergySequence();
            _fakeWasActive = fakeActive;
        }

        if (!dna) return;

        switch (_state)
        {
            case EnergyState.Charging:
                UpdateCharging();
                break;

            case EnergyState.Draining:
                // Rien : l'énergie ne bouge que via grignotage
                // On surveille quand même le 0% au cas où grignotage amène à 0
                if (dna.energy <= thresholdZero && !_hippoAlertSent)
                {
                    dna.energy = 0f;
                    TriggerHippoAlert();
                    _state = EnergyState.Charging;
                    Debug.Log("[Energy] → Charging (grignotage a atteint 0%)");
                }
                break;
        }

        if (lowEnergyBG && _bgCg)
            UpdateBlinkVisual();
    }

    // -------------------------------------------------------
    void UpdateCharging()
    {
        // À 100% → stoppe Hippo, débloque missions si première fois
        if (dna.energy >= 0.999f)
        {
            dna.energy = 1f;
            StopHippoAlert();

            if (!_firstChargeDone)
            {
                _firstChargeDone = true;
                if (missionsBoard != null)
                {
                    missionsBoard.enableMissionModule = true;
                    Debug.Log("[Energy] → Missions débloquées (premier 100%).");
                }
            }

            _state = EnergyState.Draining;
            Debug.Log("[Energy] → Draining (100% atteint, en attente de grignotage)");
        }
    }

    // -------------------------------------------------------
    // API Game Master
    // -------------------------------------------------------

    /// <summary>Force énergie à 0% + déclenche alerte Hippo</summary>
    public void GM_AlerteHippo()
    {
        dna.energy = 0f;
        _hippoAlertSent = false;
        TriggerHippoAlert();
        _state = EnergyState.Charging;
        Debug.Log("[Energy] GM → AlerteHippo forcée.");
    }

    /// <summary>Force énergie à 100% + stoppe alerte Hippo</summary>
    public void GM_HippoFull()
    {
        if (!dna) return;

        dna.energy = 1f;
        StopHippoAlert();

        if (!_firstChargeDone)
        {
            _firstChargeDone = true;
            if (missionsBoard != null)
            {
                missionsBoard.enableMissionModule = true;
                Debug.Log("[Energy] GM → Missions débloquées (premier 100% forcé).");
            }
        }

        _state = EnergyState.Draining;
        Debug.Log("[Energy] GM → HippoFull (100% forcé, state=Draining).");
    }

    /// <summary>Retire grignotagePercent% d'énergie</summary>
    public void GM_Grignotage()
    {
        Debug.Log($"[Energy] GM_Grignotage() appelé | dna={dna} | state={_state} | energy avant={dna?.energy}");
        if (!dna) { Debug.LogError("[Energy] GM_Grignotage : dna est NULL !"); return; }
        float before = dna.energy;
        dna.DecreaseBy(grignotagePercent);
        Debug.Log($"[Energy] GM → Grignotage -{grignotagePercent}% | {before:P0} → {dna.energy:P0}");
    }

    // -------------------------------------------------------
    void TriggerHippoAlert()
    {
        if (_hippoAlertSent) return;
        _hippoAlertSent = true;

        if (wsAlerts) wsAlerts.SendManualAlert("hippo");
        HippoAlertTriggered?.Invoke();
        Debug.Log("[Energy] Alerte Hippo déclenchée.");
    }

    void StopHippoAlert()
    {
        _hippoAlertSent = false;
        if (wsAlerts) wsAlerts.ForceStopAlert(asCommand: true);
        Debug.Log("[Energy] Alerte Hippo stoppée.");
    }

    // -------------------------------------------------------
    void UpdateBlinkVisual()
    {
        float e = Mathf.Clamp01(dna.energy);

        if (e < thresholdLow && e > thresholdZero)
        {
            if (!lowEnergyBG.activeSelf) lowEnergyBG.SetActive(true);
            float t = Mathf.InverseLerp(thresholdLow, thresholdZero, e);
            float speed = Mathf.Lerp(minBlinkSpeed, maxBlinkSpeed, t);
            _blinkTimer += Time.deltaTime * speed;
            _bgCg.alpha = Mathf.Lerp(alphaMin, alphaMax, Mathf.Sin(_blinkTimer) * 0.5f + 0.5f);
        }
        else if (e <= thresholdZero)
        {
            if (!lowEnergyBG.activeSelf) lowEnergyBG.SetActive(true);
            _bgCg.alpha = 1f;
        }
        else
        {
            lowEnergyBG.SetActive(false);
            _bgCg.alpha = 0f;
        }
    }
}