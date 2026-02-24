using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Hub unique pour gérer plusieurs WebSockets (Node-RED).
/// - Configure host/port une seule fois
/// - Plusieurs endpoints nommés (ia, missions, kosmo, dna...)
/// - Auto-reconnect
/// - Dispatch des messages sur le main thread Unity
/// </summary>
public class KosmoWebSocketHub : MonoBehaviour
{
    [Serializable]
    public class Endpoint
    {
        [Tooltip("Nom logique: ia, missions, kosmo, dna ...")]
        public string name = "ia";

        [Tooltip("Chemin WS (ex: /ws/ia). Ignoré si overrideUrl est rempli.")]
        public string path = "/ws/ia";

        [Tooltip("URL complète si tu veux bypass host/port/path (ex: ws://192.168.1.50:1880/ws/ia)")]
        public string overrideUrl = "";

        public bool autoReconnect = true;
        public float reconnectDelaySec = 3f;

        [NonSerialized] public ClientWebSocket ws;
        [NonSerialized] public CancellationTokenSource cts;
        [NonSerialized] public bool closing;
    }

    [Header("Server")]
    public string host = "127.0.0.1";
    public int port = 1880;
    [Tooltip("ws ou wss")]
    public string scheme = "ws";

    [Header("Endpoints")]
    public List<Endpoint> endpoints = new List<Endpoint>
    {
        new Endpoint{ name="ia",       path="/ws/ia" },
        new Endpoint{ name="missions", path="/ws/missions" },
        new Endpoint{ name="kosmo",    path="/ws/kosmo" },
        new Endpoint{ name="dna",      path="/ws/dna" },
    };

    [Header("Debug")]
    public bool log = true;

    /// <summary>(endpointName, rawMessage)</summary>
    public event Action<string, string> Message;

    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    readonly Dictionary<string, Endpoint> _map = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);

    void Awake()
    {
        _map.Clear();
        foreach (var e in endpoints)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.name)) continue;
            _map[e.name.Trim()] = e;
        }
    }

    void Start()
    {
        foreach (var e in endpoints) _ = Connect(e);
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    void OnApplicationQuit()
    {
        foreach (var e in endpoints) _ = Close(e);
    }

    public string GetUrl(string endpointName)
    {
        if (!_map.TryGetValue(endpointName, out var e)) return null;
        if (!string.IsNullOrWhiteSpace(e.overrideUrl)) return e.overrideUrl.Trim();
        return $"{scheme}://{host}:{port}{e.path}";
    }

    public bool IsConnected(string endpointName)
    {
        return _map.TryGetValue(endpointName, out var e)
               && e.ws != null
               && e.ws.State == WebSocketState.Open;
    }

    public async Task Send(string endpointName, string text)
    {
        if (!_map.TryGetValue(endpointName, out var e)) return;
        if (e.ws == null || e.ws.State != WebSocketState.Open) return;

        var data = Encoding.UTF8.GetBytes(text ?? "");
        try
        {
            await e.ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
            if (log) Debug.Log($"[WS:{endpointName}] => {text}");
        }
        catch (Exception ex)
        {
            if (log) Debug.LogWarning($"[WS:{endpointName}] send error: {ex.Message}");
        }
    }

    async Task Connect(Endpoint e)
    {
        if (e == null) return;

        await Close(e);

        e.ws = new ClientWebSocket();
        e.cts = new CancellationTokenSource();

        var url = !string.IsNullOrWhiteSpace(e.overrideUrl)
            ? e.overrideUrl.Trim()
            : $"{scheme}://{host}:{port}{e.path}";

        try
        {
            if (log) Debug.Log($"[WS:{e.name}] Connecting {url}");
            await e.ws.ConnectAsync(new Uri(url), e.cts.Token);
            if (log) Debug.Log($"[WS:{e.name}] Connected");
            _ = Task.Run(() => RecvLoop(e));
        }
        catch (Exception ex)
        {
            if (log) Debug.LogWarning($"[WS:{e.name}] connect failed: {ex.Message}");
            if (e.autoReconnect) _main.Enqueue(() => Invoke(nameof(RetryAll), e.reconnectDelaySec));
        }
    }

    void RetryAll()
    {
        foreach (var e in endpoints)
        {
            if (e == null) continue;
            if (e.ws == null || e.ws.State != WebSocketState.Open)
                _ = Connect(e);
        }
    }

    async Task RecvLoop(Endpoint e)
    {
        var buf = new ArraySegment<byte>(new byte[4096]);

        while (e.ws != null && e.ws.State == WebSocketState.Open && !e.cts.IsCancellationRequested)
        {
            WebSocketReceiveResult res = null;
            var ms = new System.IO.MemoryStream();
            try
            {
                do
                {
                    res = await e.ws.ReceiveAsync(buf, e.cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        await e.ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        break;
                    }
                    ms.Write(buf.Array, buf.Offset, res.Count);
                } while (!res.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                if (log) Debug.Log($"[WS:{e.name}] <= {msg}");

                _main.Enqueue(() => Message?.Invoke(e.name, msg));
            }
            catch (Exception ex)
            {
                if (log) Debug.LogWarning($"[WS:{e.name}] recv error: {ex.Message}");
                break;
            }
            finally { ms.Dispose(); }
        }

        if (!e.closing && e.autoReconnect)
            _main.Enqueue(() => Invoke(nameof(RetryAll), e.reconnectDelaySec));
    }

    async Task Close(Endpoint e)
    {
        try
        {
            if (e == null) return;
            e.closing = true;
            e.cts?.Cancel();
            if (e.ws != null)
            {
                if (e.ws.State == WebSocketState.Open)
                    await e.ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                e.ws.Dispose();
            }
        }
        catch { }
        finally
        {
            if (e != null)
            {
                e.ws = null;
                e.cts = null;
                e.closing = false;
            }
        }
    }
}