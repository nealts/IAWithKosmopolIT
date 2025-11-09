using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Bridge WebSocket minimal : reçoit le numéro de bouton (1..6)
/// Formats acceptés :
///  - "1", "2", ... "6"
///  - "btn 3", "pick 2"
///  - JSON: {"btn":3} ou {"pick":2}
/// </summary>
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
        int? btn = null;

        // 1) JSON
        try
        {
            var j = JsonUtility.FromJson<JsonWrap>(msg);
            if (j != null)
            {
                if (j.btn > 0) btn = j.btn;
                else if (j.pick > 0) btn = j.pick;
            }
        }
        catch { /* on tentera le texte brut */ }

        // 2) Texte brut: "3" ou "btn 3" / "pick 2"
        if (!btn.HasValue)
        {
            var lower = msg.ToLower();
            var parts = lower.Split(new[] { ' ', '\t', ':', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && int.TryParse(parts[0], out var n)) btn = n;
            else if (parts.Length >= 2 && int.TryParse(parts[1], out var m)) btn = m;
        }

        if (btn.HasValue)
        {
            int oneBased = Mathf.Clamp(btn.Value, 1, 6);
            _main.Enqueue(() => gameManager?.OnExternalSuccess(oneBased));
        }
        else
        {
            Debug.Log("[KosmoWS] Unknown msg: " + msg);
        }
    }

    [Serializable] class JsonWrap { public int btn; public int pick; }

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
