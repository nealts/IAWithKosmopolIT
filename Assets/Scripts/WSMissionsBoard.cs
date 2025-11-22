using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class WSMissionsBoard : MonoBehaviour
{
    [Header("WebSocket – Commands (start/done/reset) + outbound")]
    [Tooltip("Ex: ws://127.0.0.1:1880/ws/missions")]
    public string wsUrl = "ws://127.0.0.1:1880/ws/missions";
    public bool autoReconnect = true;
    public float reconnectDelaySec = 3f;

    [Header("WebSocket – IA (runtime params)")]
    [Tooltip("Ex: ws://127.0.0.1:1880/ws/ia")]
    public string wsUrlIa = "ws://127.0.0.1:1880/ws/ia";

    [Header("UI - Réparations en cours (4 slots max dans la scène)")]
    public TextMeshProUGUI[] ongoingSlots = new TextMeshProUGUI[4];

    [Header("UI - Missions terminées (6 max)")]
    public TextMeshProUGUI[] doneSlots = new TextMeshProUGUI[6];

    [Header("Affichage")]
    [Tooltip("Nombre de missions affichées simultanément (2..4)")]
    [Range(2, 4)] public int visibleActiveSlots = 4;

    [Header("Catalogue de missions")]
    public MissionDef[] missions = DefaultMissions();

    [Header("Debug")]
    public bool logMessages = true;

    // Etat
    readonly List<string> _active = new List<string>(4);          // visibles (ordre d’affichage)
    readonly Queue<string> _queue = new Queue<string>();          // en attente
    readonly LinkedList<string> _done = new LinkedList<string>(); // terminées (tête = plus récent)
    const int MaxDone = 6;

    // WS infra (commands/outbound)
    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    Task _recvTask;

    // WS infra (IA)
    ClientWebSocket _wsIa;
    CancellationTokenSource _ctsIa;
    Task _recvTaskIa;

    bool _closing;
    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    // ---------- Unity ----------
    void Start()
    {
        // clamp par sécurité selon les slots réellement assignés
        int maxSlotsInScene = Mathf.Clamp(ongoingSlots?.Length ?? 0, 2, 4);
        visibleActiveSlots = Mathf.Clamp(visibleActiveSlots, 2, maxSlotsInScene);

        ClearAllUI();

        // force OFF les slots au-delà de la capacité choisie (ex: 2 => cache 3 & 4)
        for (int i = visibleActiveSlots; i < (ongoingSlots?.Length ?? 0); i++)
        {
            var r = SlotRoot(ongoingSlots[i]);
            if (r) r.SetActive(false);
        }

        ConnectCmd();
        ConnectIa();
    }

    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    void OnApplicationQuit() => _ = CloseAllWS();

    [ContextMenu("Reset Board")]
    public void ResetBoard()
    {
        _active.Clear();
        _queue.Clear();
        _done.Clear();
        UpdateUI();
    }

    // ---------- Helpers UI ----------
    GameObject SlotRoot(TextMeshProUGUI t)
    {
        if (!t) return null;
        var p = t.transform.parent as RectTransform;
        return p ? p.gameObject : t.gameObject; // parent (image + déco) si présent, sinon le TMP lui-même
    }

    void ClearAllUI()
    {
        foreach (var t in ongoingSlots)
        {
            if (!t) continue;
            t.text = "";
            var root = SlotRoot(t);
            if (root) root.SetActive(false);
        }
        foreach (var t in doneSlots)
        {
            if (!t) continue;
            t.text = "";
            var root = t.transform.parent ? t.transform.parent.gameObject : t.gameObject;
            if (root) root.SetActive(false);
        }
    }

    void UpdateUI()
    {
        // En cours — n’affiche que jusqu’à visibleActiveSlots
        for (int i = 0; i < ongoingSlots.Length; i++)
        {
            var t = ongoingSlots[i];
            if (!t) continue;
            var root = SlotRoot(t);

            bool show = i < visibleActiveSlots && i < _active.Count;
            if (root && root.activeSelf != show) root.SetActive(show);
            t.text = show ? TitleOf(_active[i]) : "";
        }

        // Terminées (plus récent en haut)
        var doneArr = _done.ToArray();
        for (int i = 0; i < doneSlots.Length; i++)
        {
            var t = doneSlots[i];
            if (!t) continue;
            var root = t.transform.parent ? t.transform.parent.gameObject : t.gameObject;

            bool show = i < doneArr.Length;
            if (root && root.activeSelf != show) root.SetActive(show);
            t.text = show ? TitleOf(doneArr[i]) : "";
        }
    }

    string TitleOf(string id) => missions.FirstOrDefault(m => m.id == id)?.title ?? id;

    // ---------- WebSocket (commands/outbound) ----------
    async void ConnectCmd()
    {
        await CloseWS(_ws, _cts, _recvTask);
        _ws = null; _cts = null; _recvTask = null;

        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        try
        {
            if (logMessages) Debug.Log("[WS-cmd] Connecting " + wsUrl);
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            if (logMessages) Debug.Log("[WS-cmd] Connected");
            _recvTask = Task.Run(RecvLoopCmd);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WS-cmd] Connect failed: " + e.Message);
            if (autoReconnect) Invoke(nameof(ConnectCmd), reconnectDelaySec);
        }
    }

    async Task RecvLoopCmd()
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
                HandleCmdMessage(msg);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WS-cmd] Recv error: " + e.Message);
                break;
            }
            finally { ms.Dispose(); }
        }
        if (autoReconnect && !_closing) _main.Enqueue(() => Invoke(nameof(ConnectCmd), reconnectDelaySec));
    }

    // ---------- WebSocket (IA / params runtime) ----------
    async void ConnectIa()
    {
        await CloseWS(_wsIa, _ctsIa, _recvTaskIa);
        _wsIa = null; _ctsIa = null; _recvTaskIa = null;

        _wsIa = new ClientWebSocket();
        _ctsIa = new CancellationTokenSource();
        try
        {
            if (logMessages) Debug.Log("[WS-ia] Connecting " + wsUrlIa);
            await _wsIa.ConnectAsync(new Uri(wsUrlIa), _ctsIa.Token);
            if (logMessages) Debug.Log("[WS-ia] Connected");
            _recvTaskIa = Task.Run(RecvLoopIa);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WS-ia] Connect failed: " + e.Message);
            if (autoReconnect) Invoke(nameof(ConnectIa), reconnectDelaySec);
        }
    }

    async Task RecvLoopIa()
    {
        var buf = new ArraySegment<byte>(new byte[4096]);
        while (_wsIa != null && _wsIa.State == WebSocketState.Open && !_ctsIa.IsCancellationRequested)
        {
            WebSocketReceiveResult res = null;
            var ms = new System.IO.MemoryStream();
            try
            {
                do
                {
                    res = await _wsIa.ReceiveAsync(buf, _ctsIa.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        await _wsIa.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", _ctsIa.Token);
                        break;
                    }
                    ms.Write(buf.Array, buf.Offset, res.Count);
                } while (!res.EndOfMessage);

                var msg = Encoding.UTF8.GetString(ms.ToArray());
                HandleIaMessage(msg);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WS-ia] Recv error: " + e.Message);
                break;
            }
            finally { ms.Dispose(); }
        }
        if (autoReconnect && !_closing) _main.Enqueue(() => Invoke(nameof(ConnectIa), reconnectDelaySec));
    }

    // ---------- Fermeture WS (sans ref) ----------
    private async Task CloseWS(ClientWebSocket ws, CancellationTokenSource cts, Task recv)
    {
        try
        {
            cts?.Cancel();
            if (ws != null)
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                ws.Dispose();
            }
            if (recv != null)
            {
                try { await Task.WhenAny(recv, Task.Delay(50)); } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }

    private async Task CloseAllWS()
    {
        _closing = true;

        await CloseWS(_ws, _cts, _recvTask);
        _ws = null; _cts = null; _recvTask = null;

        await CloseWS(_wsIa, _ctsIa, _recvTaskIa);
        _wsIa = null; _ctsIa = null; _recvTaskIa = null;

        _closing = false;
    }

    // ---------- Envoi vers wsUrl (missions) ----------
    async Task SendTextAsync(string text)
    {
        try
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                if (logMessages) Debug.Log("[WS-out] => " + text);
            }
            else if (logMessages) Debug.LogWarning("[WS-out] skipped (not connected): " + text);
        }
        catch (Exception e) { Debug.LogWarning("[WS-out] send failed: " + e.Message); }
    }

    // ---------- Parsing protocole v1 (commands) ----------
    [Serializable] class Cmd { public string cmd; public string mission; public string id; }

    void HandleCmdMessage(string raw)
    {
        if (logMessages) Debug.Log("[WS-cmd] <= " + raw);

        string intent = null; // start|done|reset
        string token = null;

        // JSON strict
        try
        {
            var j = JsonUtility.FromJson<Cmd>(raw);
            if (j != null && !string.IsNullOrEmpty(j.cmd))
            {
                var c = j.cmd.Trim().ToLowerInvariant();
                if (c == "reset") intent = "reset";
                else if (c == "start" || c == "done") { intent = c; token = j.mission ?? j.id; }
            }
        }
        catch { }

        // Texte strict
        if (intent == null)
        {
            var s = raw.Trim();
            var lower = s.ToLowerInvariant();
            if (lower == "reset") intent = "reset";
            else if (lower.StartsWith("start ")) { intent = "start"; token = s.Substring(6).Trim(); }
            else if (lower.StartsWith("done ")) { intent = "done"; token = s.Substring(5).Trim(); }
        }

        _main.Enqueue(() =>
        {
            switch (intent)
            {
                case "reset": ResetBoard(); break;
                case "start":
                    if (TryResolveMissionId(token, out var idStart)) StartMission(idStart);
                    else Debug.LogWarning("[WS-cmd] start: mission introuvable pour token='" + token + "'");
                    break;
                case "done":
                    if (TryResolveMissionId(token, out var idDone)) CompleteMission(idDone);
                    else Debug.LogWarning("[WS-cmd] done: mission introuvable pour token='" + token + "'");
                    break;
                default:
                    Debug.Log("[WS-cmd] Message ignoré (format invalide)."); break;
            }
        });
    }

    // ---------- Parsing IA (Visible Active Slots N) ----------
    [Serializable] class IaJson { public int visibleActiveSlots; public string cmd; public int value; }

    void HandleIaMessage(string raw)
    {
        if (logMessages) Debug.Log("[WS-ia] <= " + raw);
        var s = raw.Trim().ToLowerInvariant();
        int n = 0;

        // JSON toléré
        if (s.StartsWith("{"))
        {
            try
            {
                var j = JsonUtility.FromJson<IaJson>(raw);
                if (j != null)
                {
                    if (j.visibleActiveSlots > 0) n = j.visibleActiveSlots;
                    else if ((j.cmd ?? "").ToLowerInvariant().Contains("visible")) n = j.value;
                }
            }
            catch { }
        }

        // Texte : “Visible Active Slots 3”
        if (n == 0 && s.Contains("visible") && s.Contains("slot"))
        {
            var m = Regex.Match(s, @"\d+");
            if (m.Success) int.TryParse(m.Value, out n);
        }

        if (n >= 2 && n <= 4)
            _main.Enqueue(() => ChangeVisibleSlots(n));
    }

    // ---------- Résolution / moteur ----------
    bool TryResolveMissionId(string token, out string id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var t = token.Trim();
        var byId = missions.FirstOrDefault(m => string.Equals(m.id, t, StringComparison.OrdinalIgnoreCase));
        if (byId != null) { id = byId.id; return true; }

        foreach (var m in missions)
            if (Slug(m.title) == Slug(t) || Slug(m.startAlias) == Slug(t)) { id = m.id; return true; }

        return false;
    }

    List<string> VisibleActiveSnapshot() => _active.Take(visibleActiveSlots).ToList();

    async void NotifyNewlyVisible(List<string> prev, List<string> now)
    {
        foreach (var id in now)
            if (!prev.Contains(id))
                await SendTextAsync(OutMessageFor(id));   // ok si doublon, demandé
    }

    string OutMessageFor(string id)
    {
        var m = missions.FirstOrDefault(x => x.id == id);
        if (m == null) return "Mission ?";
        return string.IsNullOrWhiteSpace(m.outMessage) ? $"Mission {m.number}" : m.outMessage;
    }

    void ChangeVisibleSlots(int newCount)
    {
        int maxSlotsInScene = Mathf.Clamp(ongoingSlots?.Length ?? 0, 2, 4);
        newCount = Mathf.Clamp(newCount, 2, maxSlotsInScene);

        var prevVisible = VisibleActiveSnapshot();
        visibleActiveSlots = newCount;

        // si on augmente la capacité, remplir depuis la queue
        while (_active.Count < visibleActiveSlots && _queue.Count > 0)
            _active.Add(_queue.Dequeue());

        // ON/OFF des racines au-dessus/la-dessous du cap
        for (int i = 0; i < (ongoingSlots?.Length ?? 0); i++)
        {
            var root = SlotRoot(ongoingSlots[i]);
            if (!root) continue;
            bool show = (i < visibleActiveSlots) && (i < _active.Count);
            if (root.activeSelf != show) root.SetActive(show);
        }

        var nowVisible = VisibleActiveSnapshot();
        _ = Task.Run(() => NotifyNewlyVisible(prevVisible, nowVisible));

        UpdateUI();
        if (logMessages) Debug.Log($"[IA] visibleActiveSlots = {visibleActiveSlots}");
    }

    void StartMission(string id)
    {
        if (_active.Contains(id) || _queue.Contains(id)) return;

        var prevVisible = VisibleActiveSnapshot();

        if (_active.Count < visibleActiveSlots) _active.Add(id);
        else _queue.Enqueue(id);

        var nowVisible = VisibleActiveSnapshot();
        _ = Task.Run(() => NotifyNewlyVisible(prevVisible, nowVisible));

        UpdateUI();
    }

    void CompleteMission(string id)
    {
        var prevVisible = VisibleActiveSnapshot();

        int i = _active.IndexOf(id);
        if (i >= 0)
        {
            _active.RemoveAt(i);
            if (_queue.Count > 0 && _active.Count < visibleActiveSlots)
                _active.Add(_queue.Dequeue());
        }
        else
        {
            var q = _queue.ToArray().ToList();
            if (q.Remove(id)) { _queue.Clear(); foreach (var k in q) _queue.Enqueue(k); }
        }

        if (_done.Contains(id)) _done.Remove(id);
        _done.AddFirst(id);
        while (_done.Count > MaxDone) _done.RemoveLast();

        var nowVisible = VisibleActiveSnapshot();
        _ = Task.Run(() => NotifyNewlyVisible(prevVisible, nowVisible));

        UpdateUI();
    }

    // ---------- Data ----------
    [Serializable]
    public class MissionDef
    {
        public string id;           // m01...
        public string title;        // libellé
        public string startAlias;   // slug d’écoute (ex: hippo_glouton)
        public int number = 0;      // X pour "Mission X"
        public string outMessage;   // si vide => "Mission X"
    }

    public static MissionDef[] DefaultMissions()
    {
        string[] names = {
            "Qui est ce ?","Crime City","Mystère de Pékin","Hippo Glouton","Kosmopolite",
            "Dr Maboul","Loup Garou de Thiercelieux","Time line","Bomb busters","Photo Party",
            "Mission 11","Mission 12","Mission 13","Mission 14","Mission 15",
            "Mission 16","Mission 17","Mission 18","Mission 19","Mission 20"
        };
        var list = new List<MissionDef>();
        for (int i = 0; i < 20; i++)
        {
            var id = "m" + (i + 1).ToString("00");
            var title = names[i];
            var num = i + 1;
            list.Add(new MissionDef
            {
                id = id,
                title = title,
                startAlias = Slug(title),
                number = num,
                outMessage = $"Mission {num}"
            });
        }
        return list.ToArray();
    }

    static string Slug(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant()
             .Replace("?", "").Replace("'", "").Replace(":", "")
             .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("à", "a").Replace("ù", "u")
             .Replace("ï", "i").Replace("î", "i").Replace("ô", "o").Replace("ö", "o").Replace("ç", "c");
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('_');
        var flat = sb.ToString();
        while (flat.Contains("__")) flat = flat.Replace("__", "_");
        return flat.Trim('_');
    }
}
