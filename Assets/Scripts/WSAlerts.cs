using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class WSAlerts : MonoBehaviour
{
    [Header("WebSocket")]
    [Tooltip("Endpoint Node-RED: ws://host:port/ws/ia")]
    public string wsUrl = "ws://127.0.0.1:1880/ws/ia";
    public bool autoReconnect = true;
    public float reconnectDelaySec = 3f;
    public bool logMessages = true;

    [Header("Alert GameObjects")]
    public GameObject alertHippo;
    public GameObject alertJumanji1;
    public GameObject alertJumanji2;

    [Header("Blink settings")]
    [Range(0.1f, 5f)] public float blinkIntervalSec = 1f;
    [Range(0f, 1f)] public float dimAlpha = 0.5f;

    // --- runtime ---
    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    Task _recvTask;
    bool _closing;
    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    Coroutine _blinkCo;
    CanvasGroup _currentCg;

    [Serializable] class AlertJson { public string alert; public string cmd; }

    void Start()
    {
        // désactive tous les alerts au lancement
        SetActive(alertHippo, false);
        SetActive(alertJumanji1, false);
        SetActive(alertJumanji2, false);
        Connect();
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    void OnApplicationQuit() => _ = CloseWS();

    // ------------- WS infra -------------
    async void Connect()
    {
        await CloseWS();
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        try
        {
            if (logMessages) Debug.Log("[WS] Connecting " + wsUrl);
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            if (logMessages) Debug.Log("[WS] Connected");
            _recvTask = Task.Run(RecvLoop);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WS] Connect failed: " + e.Message);
            if (autoReconnect) Invoke(nameof(RetryConnect), reconnectDelaySec);
        }
    }

    void RetryConnect()
    {
        if (!_closing) Connect();
    }

    async Task RecvLoop()
    {
        var buf = new ArraySegment<byte>(new byte[4096]);

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
                } while (!res.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                if (logMessages) Debug.Log("[WS] <= " + msg);
                _main.Enqueue(() => HandleMessage(msg));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WS] Recv error: " + e.Message);
                break;
            }
            finally { ms.Dispose(); }
        }

        if (!_closing && autoReconnect)
            _main.Enqueue(() => Invoke(nameof(RetryConnect), reconnectDelaySec));
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
        finally { _ws = null; _cts = null; _recvTask = null; _closing = false; }
    }

    // ------------- Messages -------------
    void HandleMessage(string raw)
    {
        string token = raw.Trim().ToLowerInvariant();

        // support JSON simple { "alert":"hippo" } / { "cmd":"hippo" }
        if (token.StartsWith("{") && token.EndsWith("}"))
        {
            try
            {
                var j = JsonUtility.FromJson<AlertJson>(raw);
                if (j != null)
                {
                    var v = (j.alert ?? j.cmd ?? "").Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(v)) token = v;
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
                ShowBlink(alertJumanji1);
                break;
            case "jumanji2":
                ShowBlink(alertJumanji2);
                break;
            case "stopalerte":
            case "stopalert":
            case "stop":
                StopAllAlerts();
                break;
            default:
                if (logMessages) Debug.Log("[WS] unknown token: " + token);
                break;
        }
    }

    // ------------- Blink / UI -------------
    void StopAllAlerts()
    {
        if (_blinkCo != null) { StopCoroutine(_blinkCo); _blinkCo = null; }
        _currentCg = null;

        SetActive(alertHippo, false);
        SetActive(alertJumanji1, false);
        SetActive(alertJumanji2, false);
    }

    void ShowBlink(GameObject go)
    {
        if (!go) return;

        // désactive les autres, arrête l'ancien clignotement
        StopAllAlerts();

        // active le bon et lance le blink
        SetActive(go, true);
        _currentCg = EnsureCanvasGroup(go);
        _currentCg.alpha = 1f;
        _blinkCo = StartCoroutine(BlinkRoutine(_currentCg));
    }

    System.Collections.IEnumerator BlinkRoutine(CanvasGroup cg)
    {
        if (!cg) yield break;
        float a1 = 1f;
        float a2 = Mathf.Clamp01(dimAlpha);

        while (cg && cg.gameObject.activeInHierarchy)
        {
            cg.alpha = (cg.alpha > (a1 + a2) * 0.5f) ? a2 : a1;
            yield return new WaitForSeconds(blinkIntervalSec);
        }
    }

    // helpers
    static void SetActive(GameObject go, bool v)
    {
        if (go && go.activeSelf != v) go.SetActive(v);
        var cg = go ? go.GetComponent<CanvasGroup>() : null;
        if (v && cg) cg.alpha = 1f;
    }

    static CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }
}
