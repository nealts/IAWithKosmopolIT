using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Reçoit des commandes depuis Node-RED via WebSocket (via Hub) et pilote DNA2DAnimator.
/// Messages JSON attendus:
/// { "cmd":"increase", "value":5 }     // +5%
/// { "cmd":"decrease", "value":3 }     // -3%
/// { "cmd":"set",      "value":37 }    // 37% (0..1 accepté aussi)
/// Optionnel: { "cmd":"ping" } -> répond "pong"
/// </summary>
public class WSProgressBridge : MonoBehaviour
{
    [Header("WebSocket Hub")]
    public KosmoWebSocketHub hub;
    [Tooltip("Nom d'endpoint dans le Hub (ex: dna)")]
    public string endpoint = "dna";

    [Header("Cible")]
    public DNA2DAnimator animator;          // drag & drop ton composant
    [Tooltip("Si 'value' non fourni dans le message, on utilise ce pas (en %)")]
    public float defaultStepPercent = 5f;

    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    void Start()
    {
        if (!animator) animator = FindAnyObjectByType<DNA2DAnimator>();
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    // ---------- WebSocket Hub bindings ----------
    void EnsureHub()
    {
        if (hub == null) hub = FindAnyObjectByType<KosmoWebSocketHub>();
    }

    void OnEnable()
    {
        EnsureHub();
        if (hub != null) hub.Message += OnHubMessage;
    }

    void OnDisable()
    {
        if (hub != null) hub.Message -= OnHubMessage;
    }

    void OnHubMessage(string ep, string raw)
    {
        if (!string.Equals(ep, endpoint, StringComparison.OrdinalIgnoreCase)) return;
        HandleMessage(raw);
    }

    void HandleMessage(string msg)
    {
        // tolère du json simple { "cmd":"increase", "value":5 }
        string cmd = null;
        float? val = null;

        try
        {
            var j = JsonUtility.FromJson<Msg>(Wrap(msg));
            cmd = j.cmd;
            val = j.value;
        }
        catch { /* ignore → on teste aussi du texte brut */ }

        if (string.IsNullOrEmpty(cmd))
        {
            // accepte aussi "increase 5", "set 37" etc.
            var parts = msg.Trim().ToLower().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) cmd = parts[0];
            if (parts.Length > 1 && float.TryParse(parts[1].Replace("%", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                val = f;
        }

        float step = val.HasValue ? val.Value : defaultStepPercent;

        _main.Enqueue(() =>
        {
            if (animator == null) return;

            switch (cmd)
            {
                case "increase":
                case "inc":
                case "+":
                    animator.IncreaseBy(step);
                    break;

                case "decrease":
                case "dec":
                case "-":
                    animator.DecreaseBy(step);
                    break;

                case "set":
                    animator.SetPercent(step);
                    break;

                case "ping":
                    _ = SendText("pong");
                    break;

                default:
                    Debug.Log("[WS] Unknown cmd: " + cmd + " | raw: " + msg);
                    break;
            }
        });
    }

    // envoie une petite réponse (debug)
    public async Task SendText(string text)
    {
        EnsureHub();
        if (hub == null) return;
        await hub.Send(endpoint, text);
    }

    // ----- helpers JSON -----
    [Serializable] class Msg { public string cmd; public float value; }
    string Wrap(string s)
    {
        if (s.TrimStart().StartsWith("{")) return s;
        return s;
    }
}