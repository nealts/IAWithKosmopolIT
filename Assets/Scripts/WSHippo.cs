using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class WSHippo : MonoBehaviour
{
    [SerializeField] private WSChannel channel = WSChannel.Hippo;
    [SerializeField] private bool autoReconnect = true;
    public float reconnectDelaySec = 3f;

    string wsUrl;

    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    bool _closing;

    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    WSAlerts _alerts;
    WSProgressBridge _progress;

    void Start()
    {
        _alerts = FindAnyObjectByType<WSAlerts>();
        _progress = FindAnyObjectByType<WSProgressBridge>();

        if (WSConnectionsHub.Instance == null)
        {
            Debug.LogError("WSConnectionsHub not found.");
            return;
        }

        wsUrl = WSConnectionsHub.Instance.GetUrl(channel);
        WSConnectionsHub.Instance.OnConfigChanged += HandleConfigChanged;

        Connect();
    }

    void HandleConfigChanged()
    {
        wsUrl = WSConnectionsHub.Instance.GetUrl(channel);
        if (!autoReconnect) return;

        _main.Enqueue(async () =>
        {
            await CloseWS();
            Connect();
        });
    }

    void OnDestroy()
    {
        if (WSConnectionsHub.Instance != null)
            WSConnectionsHub.Instance.OnConfigChanged -= HandleConfigChanged;
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    async void Connect()
    {
        await CloseWS();

        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        try
        {
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            _ = Task.Run(RecvLoop);
        }
        catch
        {
            if (autoReconnect)
                Invoke(nameof(Connect), reconnectDelaySec);
        }
    }

    async Task RecvLoop()
    {
        var buf = new ArraySegment<byte>(new byte[2048]);

        while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult res;

            do
            {
                res = await _ws.ReceiveAsync(buf, _cts.Token);
                ms.Write(buf.Array, buf.Offset, res.Count);
            }
            while (!res.EndOfMessage);

            var msg = Encoding.UTF8.GetString(ms.ToArray()).Trim().ToLowerInvariant();
            Debug.LogWarning("[WS-Hippo] " + msg);

            if (msg == "hippofull")
            {
                _main.Enqueue(() => _alerts?.TriggerHippoFull());
            }
            if (msg == "alertehippo")
            {
                _main.Enqueue(() =>
                {
                    if (_progress != null && _progress.animator != null)
                    {
                        _progress.animator.SetPercent(0f);
                    }
                });
            }

            ms.Dispose();
        }
    }

    async Task CloseWS()
    {
        try
        {
            _closing = true;
            _cts?.Cancel();

            if (_ws != null && _ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
        catch { }
        finally
        {
            _ws = null;
            _cts = null;
            _closing = false;
        }
    }
}