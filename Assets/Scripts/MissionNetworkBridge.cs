using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class MissionNetworkBridge : MonoBehaviour
{
    [Header("Refs")]
    public MissionEngine engine;

    [Header("WebSocket")]
    [SerializeField] private WSChannel cmdChannel;
    [SerializeField] private WSChannel iaChannel;
    [SerializeField] private bool autoReconnect = true;
    public float reconnectDelaySec = 3f;

    public bool logMessages = true;

    string wsCmdUrl;
    string wsIaUrl;

    ClientWebSocket _wsCmd;
    ClientWebSocket _wsIa;
    CancellationTokenSource _ctsCmd;
    CancellationTokenSource _ctsIa;

    readonly ConcurrentQueue<Action> _main = new();
    bool _closing;

    void Start()
    {
        if (!engine) engine = GetComponent<MissionEngine>();

        RefreshUrls();

        ConnectCmd();
        ConnectIa();

        if (engine != null)
            engine.OnVisibleChanged += OnVisibleChanged;
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    void RefreshUrls()
    {
        var hub = WSConnectionsHub.Instance;
        if (hub == null)
        {
            Debug.LogError("WSConnectionsHub not found.");
            return;
        }

        wsCmdUrl = hub.GetUrl(cmdChannel);
        wsIaUrl = hub.GetUrl(iaChannel);
    }

    #region CMD SOCKET

    async void ConnectCmd()
    {
        await CloseCmd();

        _wsCmd = new ClientWebSocket();
        _ctsCmd = new CancellationTokenSource();

        try
        {
            if (logMessages) Debug.Log("[WS-cmd] Connecting " + wsCmdUrl);
            await _wsCmd.ConnectAsync(new Uri(wsCmdUrl), _ctsCmd.Token);
            if (logMessages) Debug.Log("[WS-cmd] Connected");
            _ = Task.Run(RecvLoopCmd);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WS-cmd] Connect failed: " + e.Message);
            if (autoReconnect)
                Invoke(nameof(ConnectCmd), reconnectDelaySec);
        }
    }

    async Task RecvLoopCmd()
    {
        var buf = new ArraySegment<byte>(new byte[4096]);

        while (_wsCmd != null && _wsCmd.State == WebSocketState.Open)
        {
            WebSocketReceiveResult res = null;
            var ms = new System.IO.MemoryStream();

            try
            {
                do
                {
                    res = await _wsCmd.ReceiveAsync(buf, _ctsCmd.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                        break;

                    ms.Write(buf.Array, buf.Offset, res.Count);

                } while (!res.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                _main.Enqueue(() => HandleCmd(msg));
            }
            catch
            {
                break;
            }
        }

        if (!_closing && autoReconnect)
            _main.Enqueue(() => Invoke(nameof(ConnectCmd), reconnectDelaySec));
    }

    void HandleCmd(string raw)
    {
        if (logMessages) Debug.Log("[WS-cmd] <= " + raw);

        if (raw.Trim().Equals("Mission 6", StringComparison.OrdinalIgnoreCase))
            GameObject.Find("Background Idle")?.SetActive(false);

        string lower = raw.Trim().ToLowerInvariant();

        if (lower.StartsWith("start "))
        {
            string token = raw.Substring(6).Trim();
            if (TryResolveId(token, out var id))
                engine.StartMission(id);
        }
        else if (lower.StartsWith("done "))
        {
            string token = raw.Substring(5).Trim();
            if (TryResolveId(token, out var id))
                engine.CompleteMission(id);
        }
        else if (lower == "reset")
        {
            engine.BeginPhase(0);
        }
    }

    async Task CloseCmd()
    {
        try
        {
            _closing = true;
            _ctsCmd?.Cancel();
            if (_wsCmd != null)
            {
                if (_wsCmd.State == WebSocketState.Open)
                    await _wsCmd.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);

                _wsCmd.Dispose();
            }
        }
        catch { }

        _wsCmd = null;
        _ctsCmd = null;
        _closing = false;
    }

    #endregion

    #region IA SOCKET

    async void ConnectIa()
    {
        await CloseIa();

        _wsIa = new ClientWebSocket();
        _ctsIa = new CancellationTokenSource();

        try
        {
            if (logMessages) Debug.Log("[WS-ia] Connecting " + wsIaUrl);
            await _wsIa.ConnectAsync(new Uri(wsIaUrl), _ctsIa.Token);
            if (logMessages) Debug.Log("[WS-ia] Connected");
            _ = Task.Run(RecvLoopIa);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WS-ia] Connect failed: " + e.Message);
            if (autoReconnect)
                Invoke(nameof(ConnectIa), reconnectDelaySec);
        }
    }

    async Task RecvLoopIa()
    {
        var buf = new ArraySegment<byte>(new byte[4096]);

        while (_wsIa != null && _wsIa.State == WebSocketState.Open)
        {
            WebSocketReceiveResult res = null;
            var ms = new System.IO.MemoryStream();

            try
            {
                do
                {
                    res = await _wsIa.ReceiveAsync(buf, _ctsIa.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                        break;

                    ms.Write(buf.Array, buf.Offset, res.Count);

                } while (!res.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                _main.Enqueue(() => HandleIa(msg));
            }
            catch
            {
                break;
            }
        }

        if (!_closing && autoReconnect)
            _main.Enqueue(() => Invoke(nameof(ConnectIa), reconnectDelaySec));
    }

    void HandleIa(string raw)
    {
        if (logMessages) Debug.Log("[WS-ia] <= " + raw);

        if (raw.Contains("startPhase"))
        {
            var m = Regex.Match(raw, @"\d+");
            if (m.Success && int.TryParse(m.Value, out var p))
                engine.BeginPhase(Mathf.Clamp(p - 1, 0, 1));
        }
    }

    async Task CloseIa()
    {
        try
        {
            _closing = true;
            _ctsIa?.Cancel();
            if (_wsIa != null)
            {
                if (_wsIa.State == WebSocketState.Open)
                    await _wsIa.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);

                _wsIa.Dispose();
            }
        }
        catch { }

        _wsIa = null;
        _ctsIa = null;
        _closing = false;
    }

    #endregion

    void OnVisibleChanged(System.Collections.Generic.List<string> prev, System.Collections.Generic.List<string> now)
    {
        foreach (var id in now)
        {
            if (!prev.Contains(id))
                _ = SendOut(engine.missions.FirstOrDefault(m => m.id == id)?.outMessage);
        }
    }

    async Task SendOut(string text)
    {
        if (_wsCmd == null || _wsCmd.State != WebSocketState.Open) return;

        var data = Encoding.UTF8.GetBytes(text);
        var seg = new ArraySegment<byte>(data);

        try
        {
            await _wsCmd.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
            if (logMessages) Debug.Log("[WS-out] => " + text);
        }
        catch { }
    }

    bool TryResolveId(string token, out string id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (Regex.IsMatch(token, @"^\d+$"))
        {
            id = "m" + int.Parse(token).ToString("00");
            return true;
        }

        id = engine.missions
            .FirstOrDefault(m => m.title.Equals(token, StringComparison.OrdinalIgnoreCase))?.id;

        return id != null;
    }
}