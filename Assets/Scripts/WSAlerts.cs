using System;
using UnityEngine;

public class WSAlerts : MonoBehaviour
{
    public static event Action HippoAlertStopped;
    public static event Action StopCommandReceived;

    [Header("WebSocket Hub")]
    public KosmoWebSocketHub hub;
    [Tooltip("Nom d'endpoint dans le Hub (ex: ia)")]
    public string endpoint = "ia";
    public bool logMessages = true;

    [Header("Alert GameObjects")]
    public GameObject alertHippo;    // Alerte-hg
    public GameObject alertJumanji;  // Alerte-j (utilisée pour jumanji1 + jumanji2)

    [Header("Blink settings")]
    [Range(0.1f, 5f)] public float blinkIntervalSec = 1f;
    [Range(0f, 1f)] public float dimAlpha = 0.5f;

    Coroutine _blinkCo;
    CanvasGroup _currentCg;

    [Serializable]
    class AlertJson { public string alert; public string cmd; }

    void Start()
    {
        SetActive(alertHippo, false);
        SetActive(alertJumanji, false);

        EnsureHub();
        if (hub != null) hub.Message += OnHubMessage;
    }

    void OnDisable()
    {
        if (hub != null) hub.Message -= OnHubMessage;
    }

    void OnApplicationQuit()
    {
        if (hub != null) hub.Message -= OnHubMessage;
    }

    void EnsureHub()
    {
        if (hub == null) hub = FindAnyObjectByType<KosmoWebSocketHub>();
    }

    void OnHubMessage(string ep, string raw)
    {
        if (!string.Equals(ep, endpoint, StringComparison.OrdinalIgnoreCase)) return;
        if (logMessages) Debug.Log("[WS:" + endpoint + "] <= " + raw);
        HandleMessage(raw);
    }

    // ✅ Gardé public (WSMissionsBoard l'utilise)
    public void SendManualAlert(string token)
    {
        switch ((token ?? "").ToLowerInvariant())
        {
            case "hippo":
                ShowBlink(alertHippo);
                break;

            case "jumanji1":
            case "jumanji2":
                ShowBlink(alertJumanji);
                break;
        }
    }

    // ✅ Gardé public (WSMissionsBoard l'utilise)
    public void HandleMessage(string raw)
    {
        string token = (raw ?? "").Trim().ToLowerInvariant();

        // Support JSON simple { "alert":"hippo" } / { "cmd":"hippo" }
        if (token.StartsWith("{") && token.EndsWith("}"))
        {
            try
            {
                var j = JsonUtility.FromJson<AlertJson>(raw);
                if (j != null)
                {
                    var v = (j.alert ?? j.cmd ?? "").Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(v)) token = v;
                    else return; // JSON sans alert/cmd => ignore
                }
            }
            catch { /* ignore */ }
        }

        switch (token)
        {
            case "hippo":
                ShowBlink(alertHippo);
                break;

            case "jumanji1":
            case "jumanji2":
                ShowBlink(alertJumanji);
                break;

            case "stopalerte":
            case "stopalert":
            case "stop":
                StopAllAlerts();
                StopCommandReceived?.Invoke();
                break;

            default:
                if (logMessages) Debug.Log("[WSAlerts] unknown token: " + token);
                break;
        }
    }

    void StopAllAlerts()
    {
        if (_blinkCo != null)
        {
            StopCoroutine(_blinkCo);
            _blinkCo = null;
        }
        _currentCg = null;

        SetActive(alertHippo, false);
        SetActive(alertJumanji, false);

        HippoAlertStopped?.Invoke();
    }

    void ShowBlink(GameObject go)
    {
        StopAllAlerts();

        if (!go) return;
        SetActive(go, true);

        _currentCg = go.GetComponent<CanvasGroup>();
        if (!_currentCg) _currentCg = go.AddComponent<CanvasGroup>();

        _blinkCo = StartCoroutine(BlinkRoutine(_currentCg));
    }

    System.Collections.IEnumerator BlinkRoutine(CanvasGroup cg)
    {
        bool dim = false;
        while (true)
        {
            dim = !dim;
            if (cg) cg.alpha = dim ? dimAlpha : 1f;
            yield return new WaitForSeconds(blinkIntervalSec);
        }
    }

    public void ForceStopAlert() => StopAllAlerts();

    // Si certains scripts appellent ForceStopAlert(bool)
    // Compat totale avec anciens appels
    public void ForceStopAlert(bool asCommand = false)
    {
        StopAllAlerts();

        if (asCommand)
            StopCommandReceived?.Invoke();
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go && go.activeSelf != active) go.SetActive(active);
    }
}