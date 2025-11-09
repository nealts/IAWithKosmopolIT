using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class KosmoNodeRedBridge : MonoBehaviour
{
    [Header("WebSocket")]
    public string wsUrl = "ws://127.0.0.1:1880/ws/kosmo";   // ton endpoint Node-RED
    public bool autoReconnect = true;
    public float reconnectDelaySec = 2f;

    [Header("Cible")]
    public KosmoGameManager game;

    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    bool _closing;

    void Start()
    {
        if (!game) game = FindAnyObjectByType<KosmoGameManager>();
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
        if (string.IsNullOrWhiteSpace(msg)) return;
        msg = msg.Trim();
        var lower = msg.ToLower();

        // formats acceptés :
        // "son 3" / "Son 3"
        // "success 4" / "fail"
        // JSON minimal: {"cmd":"son","n":3} | {"cmd":"success","n":4} | {"cmd":"fail"}
        string cmd = null;
        int n = -1;

        // tentative JSON
        try
        {
            var j = JsonUtility.FromJson<JsonWrap>(msg);
            if (j != null && !string.IsNullOrEmpty(j.cmd))
            {
                cmd = j.cmd.ToLower();
                n = j.n;
            }
        }
        catch { /* on passera en parsing texte */ }

        if (cmd == null)
        {
            // parsing texte
            var parts = lower.Split(new[] { ' ', '\t', ':', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                cmd = parts[0];
                if (parts.Length >= 2 && int.TryParse(parts[1], out var nn)) n = nn;
            }
        }

        // dispatch sur le main thread
        if (cmd == "son")
        {
            int oneBased = Mathf.Clamp(n, 1, 10);
            _main.Enqueue(() => game?.OnSon(oneBased));
        }
        else if (cmd == "success")
        {
            int oneBased = Mathf.Clamp(n, 1, 6);
            _main.Enqueue(() => game?.OnExternalSuccess(oneBased));
        }
        else if (cmd == "fail")
        {
            _main.Enqueue(() => game?.OnExternalFail());
        }
        else
        {
            Debug.Log("[KosmoWS] Unknown msg: " + msg);
        }
    }

    [Serializable] class JsonWrap { public string cmd; public int n; }

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
