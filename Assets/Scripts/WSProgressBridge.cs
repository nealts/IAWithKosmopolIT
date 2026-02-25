using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Reçoit des commandes depuis Node-RED via WebSocket et pilote DNA2DAnimator.
/// Messages JSON attendus:
/// { "cmd":"increase", "value":5 }     // +5%
/// { "cmd":"decrease", "value":3 }     // -3%
/// { "cmd":"set",      "value":37 }    // 37% (0..1 accepté aussi)
/// Optionnel: { "cmd":"ping" } -> répond "pong"
/// </summary>
public class WSProgressBridge : MonoBehaviour
{
    [Header("WebSocket")]
    [Tooltip("Exemple: ws://127.0.0.1:1880/ws/dna")]
    public string wsUrl = "ws://127.0.0.1:1880/ws/dna";
    public bool autoReconnect = true;
    public float reconnectDelaySec = 3f;

    [Header("Cible")]
    public DNA2DAnimator animator;          // drag & drop ton composant
    [Tooltip("Si 'value' non fourni dans le message, on utilise ce pas (en %)")]
    public float defaultStepPercent = 5f;

    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
    Task _recvTask;
    bool _closing;

    void Start()
    {
        if (!animator) animator = FindAnyObjectByType<DNA2DAnimator>();
        Connect();
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
            Debug.Log("[WS] Connecting " + wsUrl);
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            Debug.Log("[WS] Connected");
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
                }
                while (!res.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                HandleMessage(msg);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WS] Recv error: " + e.Message);
                break;
            }
            finally { ms.Dispose(); }
        }

        if (!_closing && autoReconnect) _main.Enqueue(() => Invoke(nameof(RetryConnect), reconnectDelaySec));
    }

    void HandleMessage(string msg)
    {
        // tolère du json simple { "cmd":"increase", "value":5 }
        string cmd = null;
        float? val = null;

        try
        {
            // mini parseur sans dépendance
            var j = JsonUtility.FromJson<Msg>(Wrap(msg)); // Wrap: JsonUtility veut un root unique
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
                    animator.IncreaseBy(step);   // accepte % ou 0..1
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
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var data = Encoding.UTF8.GetBytes(text);
        var seg = new ArraySegment<byte>(data);
        try { await _ws.SendAsync(seg, WebSocketMessageType.Text, true, _cts.Token); }
        catch (Exception e) { Debug.LogWarning("[WS] Send error: " + e.Message); }
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

    async void OnApplicationQuit() { await CloseWS(); }

    // ----- helpers JSON -----
    // JsonUtility nécessite un objet root ; on enveloppe si besoin.
    [Serializable] class Msg { public string cmd; public float value; }
    string Wrap(string s)
    {
        // si s commence par { "cmd": … } c'est déjà bon ; sinon on essaie tel quel
        if (s.TrimStart().StartsWith("{")) return s;
        return s; // on laisse la voie texte libre ; le try/catch gère
    }
}
