using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class KosmoInputBridge : MonoBehaviour
{
    [Header("WebSocket")]
    public string wsUrl = "ws://127.0.0.1:1880/ws/kosmo";
    public bool autoReconnect = true;
    public float reconnectDelaySec = 2f;

    [Header("Cible")]
    public KosmoGameManager gameManager;

    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    bool _closing;

    void Start()
    {
        if (!gameManager) gameManager = FindAnyObjectByType<KosmoGameManager>();
        _ = Connect();
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    async Task Connect()
    {
        await CloseWS();
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        try
        {
            Debug.Log("[KosmoWS] Connecting " + wsUrl);
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            Debug.Log("[KosmoWS] Connected");
            _ = Task.Run(RecvLoop);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[KosmoWS] Connect failed: " + e.Message);
            if (autoReconnect) Invoke(nameof(RetryConnect), reconnectDelaySec);
        }
    }

    void RetryConnect()
    {
        if (!_closing) _ = Connect();
    }

    async Task RecvLoop()
    {
        var buf = new ArraySegment<byte>(new byte[2048]);

        while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            WebSocketReceiveResult res = null;
            var ms = new System.IO.MemoryStream();

            try
            {
                do
                {
                    res = await _ws.ReceiveAsync(buf, _cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", _cts.Token);
                        break;
                    }
                    ms.Write(buf.Array, buf.Offset, res.Count);
                }
                while (!res.EndOfMessage);

                var raw = Encoding.UTF8.GetString(ms.ToArray());
                Handle(raw);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KosmoWS] Recv error: " + e.Message);
                break;
            }
            finally { ms.Dispose(); }
        }

        if (!_closing && autoReconnect) _main.Enqueue(() => Invoke(nameof(RetryConnect), reconnectDelaySec));
    }

    void Handle(string msg)
    {
        msg = msg.Trim();
        // Debug.Log("[KosmoWS] " + msg);

        // ---- JSON ? ----
        try
        {
            var j = JsonUtility.FromJson<JsonWrap>(EnsureJson(msg));
            if (j != null)
            {
                if (j.fail) { _main.Enqueue(() => gameManager?.OnOutcome(false, null)); return; }
                if (j.success > 0) { int b = Mathf.Clamp(j.success, 1, 6); _main.Enqueue(() => gameManager?.OnOutcome(true, b)); return; }
                if (j.son > 0) { _main.Enqueue(() => gameManager?.QueueNextSeries(j.son)); return; }
            }
        }
        catch { /* ignore, essaie en texte */ }

        // ---- Texte brut ----
        var lower = msg.ToLower();
        // success 3 | success
        if (lower.StartsWith("success"))
        {
            int num = ExtractInt(lower);
            if (num <= 0) num = 0; // pas obligatoire, on peut laisser null -> correctIndex
            int? maybe = (num > 0) ? num : (int?)null;
            _main.Enqueue(() => gameManager?.OnOutcome(true, maybe));
            return;
        }

        // fail
        if (lower.StartsWith("fail"))
        {
            _main.Enqueue(() => gameManager?.OnOutcome(false, null));
            return;
        }

        // son 7
        if (lower.StartsWith("son"))
        {
            int n = ExtractInt(lower);
            if (n > 0) _main.Enqueue(() => gameManager?.QueueNextSeries(n));
            return;
        }

        // optionnel: compat boutons "1..6" pour tests
        if (int.TryParse(lower, out var btn) && btn >= 1 && btn <= 6)
        {
            _main.Enqueue(() => gameManager?.OnPick(btn - 1));
            return;
        }

        Debug.Log("[KosmoWS] Unknown msg: " + msg);
    }

    int ExtractInt(string s)
    {
        foreach (var tok in s.Split(' ', '\t', ':', ';', ','))
            if (int.TryParse(tok, out var n)) return n;
        return 0;
    }

    [Serializable] class JsonWrap { public bool fail; public int success; public int son; }

    string EnsureJson(string s)
    {
        s = s.Trim();
        if (s.StartsWith("{")) return s;
        // Mini heuristique: transforme "success 3" en JSON simple
        if (s.ToLower().StartsWith("success")) { var n = ExtractInt(s.ToLower()); return "{\"success\":" + n + "}"; }
        if (s.ToLower().StartsWith("fail")) { return "{\"fail\":true}"; }
        if (s.ToLower().StartsWith("son")) { var n = ExtractInt(s.ToLower()); return "{\"son\":" + n + "}"; }
        return "{}";
    }

    async Task CloseWS()
    {
        try
        {
            _closing = true;
            _cts?.Cancel();
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                _ws.Dispose();
            }
        }
        catch { }
        finally { _ws = null; _cts = null; _closing = false; }
    }

    async void OnApplicationQuit() { await CloseWS(); }
}
