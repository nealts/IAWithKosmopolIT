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

    [Header("Références")]
    public EnergyAlertController energyController;

    EnergyAlertController _energyController;

    void Start()
    {
        _energyController = energyController
                            ? energyController
                            : FindAnyObjectByType<EnergyAlertController>();

        if (!_energyController)
            Debug.LogError("[WS-Hippo] EnergyAlertController introuvable ! Assigne-le dans l'Inspector.");
        else
            Debug.Log($"[WS-Hippo] EnergyAlertController trouvé : {_energyController.gameObject.name}");

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
            try
            {
                WebSocketReceiveResult res;
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

                var msg = Encoding.UTF8.GetString(ms.ToArray()).Trim().ToLowerInvariant();
                Debug.Log("[WS-Hippo] reçu: " + msg);

                switch (msg)
                {
                    case "startenergy":
                        _main.Enqueue(() => _energyController?.StartEnergySequence());
                        break;

                    case "alertehippo":
                        _main.Enqueue(() => _energyController?.GM_AlerteHippo());
                        break;

                    case "hippofull":
                        _main.Enqueue(() => _energyController?.GM_HippoFull());
                        break;

                    case "grignotage":
                        _main.Enqueue(() => _energyController?.GM_Grignotage());
                        break;

                    default:
                        Debug.Log("[WS-Hippo] message inconnu: " + msg);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WS-Hippo] Recv error: " + e.Message);
                break;
            }
            finally { ms.Dispose(); }
        }

        if (!_closing && autoReconnect)
            _main.Enqueue(() => Invoke(nameof(Connect), reconnectDelaySec));
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