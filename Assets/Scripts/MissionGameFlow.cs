using UnityEngine;

public class MissionGameFlow : MonoBehaviour
{
    [Header("Refs")]
    public MissionEngine engine;
    public WSAlerts alerts;

    bool _missionsUnlocked = false;
    bool _kosmoCompleted = false;
    bool _hippoTriggered = false;

    bool _jumanji1Triggered = false;
    bool _jumanji2Triggered = false;

    void Awake()
    {
        if (!engine) engine = GetComponent<MissionEngine>();
    }

    void OnEnable()
    {
        KosmoGameManager.GameCompleted += OnKosmoCompleted;
        EnergyAlertController.HippoAlertTriggered += OnHippoTriggered;
        WSAlerts.StopCommandReceived += OnStopAlert;

        if (engine != null)
            engine.OnMissionCompleted += OnMissionCompleted;
    }

    void OnDisable()
    {
        KosmoGameManager.GameCompleted -= OnKosmoCompleted;
        EnergyAlertController.HippoAlertTriggered -= OnHippoTriggered;
        WSAlerts.StopCommandReceived -= OnStopAlert;

        if (engine != null)
            engine.OnMissionCompleted -= OnMissionCompleted;
    }

    void OnKosmoCompleted()
    {
        _kosmoCompleted = true;
        TryUnlockPhase1();
    }

    void OnHippoTriggered()
    {
        _hippoTriggered = true;
    }

    void OnStopAlert()
    {
        // Si Phase 1 et pool vide → Phase 2
        if (_missionsUnlocked && engine.CurrentPhase == 0)
        {
            bool poolEmpty = engine.GetVisible().Count == 0;

            if (poolEmpty)
            {
                engine.BeginPhase(1);
            }
        }
    }

    void TryUnlockPhase1()
    {
        if (_missionsUnlocked) return;
        if (!_kosmoCompleted) return;
        if (!_hippoTriggered) return;

        _missionsUnlocked = true;

        engine.BeginPhase(0);
    }

    void OnMissionCompleted(string id)
    {
        // ----- Jumanji Phase 1 -----
        if (!_jumanji1Triggered && engine.CurrentPhase == 0)
        {
            if (engine.GetVisible().Count == 0)
            {
                _jumanji1Triggered = true;

                if (alerts)
                    alerts.HandleMessage("jumanji1");
            }
        }

        // ----- Jumanji Phase 2 -----
        if (!_jumanji2Triggered && engine.CurrentPhase == 1)
        {
            if (engine.GetVisible().Count == 0)
            {
                _jumanji2Triggered = true;

                if (alerts)
                    alerts.HandleMessage("jumanji2");
            }
        }
    }
}