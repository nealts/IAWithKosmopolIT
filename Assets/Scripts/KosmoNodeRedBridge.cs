using System;
using System.Collections.Concurrent;
using UnityEngine;

public class KosmoNodeRedBridge : MonoBehaviour
{
    [Header("WebSocket Hub")]
    public KosmoWebSocketHub hub;
    [Tooltip("Nom d'endpoint dans le Hub (ex: kosmo)")]
    public string endpoint = "kosmo";

    [Header("Target")]
    public KosmoGameManager gameManager;

    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    void Start()
    {
        if (!gameManager) gameManager = FindAnyObjectByType<KosmoGameManager>();

        EnsureHub();
        if (hub != null) hub.Message += OnHubMessage;
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
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
        Handle(raw);
    }

    void Handle(string msg)
    {
        msg = (msg ?? "").Trim();
        if (msg.Length == 0) return;

        Debug.Log("[Bridge] RX: " + msg);

        // --- JSON ? ---
        try
        {
            var j = JsonUtility.FromJson<JsonWrap>(EnsureJson(msg));
            if (j != null)
            {
                if (j.forceWin)
                {
                    _main.Enqueue(() => gameManager?.ForceWin());
                    Debug.Log("[Bridge] -> ForceWin()");
                    return;
                }
                if (j.fail)
                {
                    _main.Enqueue(() => gameManager?.OnOutcome(false, null));
                    Debug.Log("[Bridge] -> OnOutcome(false)");
                    return;
                }
                if (j.success > 0)
                {
                    int b = Mathf.Clamp(j.success, 1, 6);
                    _main.Enqueue(() => gameManager?.OnOutcome(true, b));
                    Debug.Log("[Bridge] -> OnOutcome(true," + b + ")");
                    return;
                }
                if (j.son > 0)
                {
                    _main.Enqueue(() => gameManager?.QueueNextSeries(j.son));
                    Debug.Log("[Bridge] -> QueueNextSeries(" + j.son + ")");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log("[Bridge] JSON parse fail: " + ex.Message);
        }

        // --- Texte brut ---
        var lower = msg.ToLowerInvariant();

        if (lower.StartsWith("forcewin"))
        {
            _main.Enqueue(() => gameManager?.ForceWin());
            Debug.Log("[Bridge] -> ForceWin() (text)");
            return;
        }

        if (lower.StartsWith("success"))
        {
            int num = ExtractInt(lower);
            int? maybe = (num > 0) ? num : (int?)null;
            _main.Enqueue(() => gameManager?.OnOutcome(true, maybe));
            Debug.Log("[Bridge] -> OnOutcome(true," + (maybe.HasValue ? maybe.Value.ToString() : "null") + ")");
            return;
        }

        if (lower.StartsWith("fail"))
        {
            _main.Enqueue(() => gameManager?.OnOutcome(false, null));
            Debug.Log("[Bridge] -> OnOutcome(false)");
            return;
        }

        if (lower.StartsWith("son"))
        {
            int n = ExtractInt(lower);
            if (n > 0)
            {
                _main.Enqueue(() => gameManager?.QueueNextSeries(n));
                Debug.Log("[Bridge] -> QueueNextSeries(" + n + ")");
            }
            else Debug.LogWarning("[Bridge] 'son' without number.");
            return;
        }

        // option test: "1" .. "6" => clique local
        if (int.TryParse(lower, out var btn) && btn >= 1 && btn <= 6)
        {
            int zero = btn - 1;
            _main.Enqueue(() => gameManager?.OnPick(zero));
            Debug.Log("[Bridge] -> OnPick(" + zero + ")");
            return;
        }

        Debug.Log("[Bridge] Unknown message pattern.");
    }

    int ExtractInt(string s)
    {
        foreach (var tok in s.Split(' ', '\t', ':', ';', ','))
            if (int.TryParse(tok, out var n)) return n;
        return 0;
    }

    [Serializable] class JsonWrap { public bool fail; public int success; public int son; public bool forceWin; }

    string EnsureJson(string s)
    {
        s = s.Trim();
        if (s.StartsWith("{")) return s;

        var low = s.ToLowerInvariant();
        if (low.StartsWith("success")) { var n = ExtractInt(low); return "{\"success\":" + n + "}"; }
        if (low.StartsWith("fail")) return "{\"fail\":true}";
        if (low.StartsWith("son")) { var n = ExtractInt(low); return "{\"son\":" + n + "}"; }
        if (low.StartsWith("forcewin")) return "{\"forceWin\":true}";

        return "{}";
    }
}