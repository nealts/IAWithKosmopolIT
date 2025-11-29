using System.Collections;
using UnityEngine;
using TMPro;

public class PhaseDebugUI : MonoBehaviour
{
    [Header("Références")]
    public TextMeshProUGUI debugText;

    [Header("Paramètres")]
    public float delayBeforePhase1 = 2f;

    Coroutine _phaseCo;

    bool _phase1Started = false;
    bool _jumanjiRunning = false;

    void OnEnable()
    {
        if (debugText)
            debugText.text = "Phase : Kosmo 0/4";

        // Événements Kosmo
        KosmoGameManager.OnKosmoProgress += OnKosmoProgress;
        KosmoGameManager.GameCompleted += OnKosmoGameCompleted;

        // Events énergie / alertes
        EnergyAlertController.HippoAlertTriggered += OnHippoAlertTriggered;
        WSAlerts.StopCommandReceived += OnAlertStopped;

        // Jumanji1 (via WSMissionsBoard)
        WSMissionsBoard.Jumanji1Triggered += OnJumanji1Triggered;
        WSMissionsBoard.Jumanji2Triggered += OnJumanji2Triggered;

    }

    void OnDisable()
    {
        KosmoGameManager.OnKosmoProgress -= OnKosmoProgress;
        KosmoGameManager.GameCompleted -= OnKosmoGameCompleted;

        EnergyAlertController.HippoAlertTriggered -= OnHippoAlertTriggered;
        WSAlerts.StopCommandReceived -= OnAlertStopped;

        WSMissionsBoard.Jumanji1Triggered -= OnJumanji1Triggered;
        WSMissionsBoard.Jumanji2Triggered -= OnJumanji2Triggered;

    }

    void OnJumanji2Triggered()
    {
        SetText("Alerte Jumanji #2");
        StopPhaseRoutine();
    }


    // --------- KOSMO ---------

    void OnKosmoProgress(int wins, int maxWins)
    {
        // Tant qu'on est dans la phase Kosmo, on met à jour
        SetText($"Phase : Kosmo {wins}/{maxWins}");
    }

    void OnKosmoGameCompleted()
    {
        // Kosmo terminé (4/4) → on attend l'alerte Hippo
        SetText("Attente alerte Hippo");
        StopPhaseRoutine();
    }

    // --------- HIPPO / ÉNERGIE ---------

    void OnHippoAlertTriggered()
    {
        // Hippo active (énergie 0%) → on attend la recharge
        SetText("Attente recharge energie");
        StopPhaseRoutine();
    }

    // --------- JUMANJI ---------

    void OnJumanji1Triggered()
    {
        _jumanjiRunning = true;
        SetText("Alerte Jumanji #1");
        StopPhaseRoutine();
    }

    // --------- STOP ALERT (HIPPO ou JUMANJI) ---------

    void OnAlertStopped()
    {
        // Cas 1 : on n'a pas encore commencé la Phase 1
        // => c'est le stop après HIPPO -> "Chargement des missions" -> Phase 1
        if (!_phase1Started && !_jumanjiRunning)
        {
            StopPhaseRoutine();
            _phaseCo = StartCoroutine(Phase1Sequence());
            _phase1Started = true;
            return;
        }

        // Cas 2 : on était en plein Jumanji1
        // => stopAlerte après Jumanji1 -> on passe en Phase 2
        if (_jumanjiRunning)
        {
            _jumanjiRunning = false;
            StopPhaseRoutine();
            SetText("Phase 2");
        }

        // Si on voulait gérer d'autres cas plus tard, on pourrait les rajouter ici.
    }

    IEnumerator Phase1Sequence()
    {
        SetText("Chargement des missions");
        yield return new WaitForSeconds(delayBeforePhase1);
        SetText("Phase 1");
    }

    // --------- Utils ---------

    void SetText(string msg)
    {
        if (debugText) debugText.text = msg;
    }

    void StopPhaseRoutine()
    {
        if (_phaseCo != null)
        {
            StopCoroutine(_phaseCo);
            _phaseCo = null;
        }
    }
}
