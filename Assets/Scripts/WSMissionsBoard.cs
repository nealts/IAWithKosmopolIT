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
    bool _phase2Started = false;
    [Header("Alertes")]
    public WSAlerts wsAlerts;   // à lier dans l’inspector

    [Header("Intégration alerts")]
    public WSAlerts alerts;

    bool _hippoTriggeredOnce = false;
    bool _jumanjiTriggered = false;
    bool _jumanji1Triggered = false;

    [Header("WebSocket – Commands (start/done/reset) + outbound")]
    public string wsUrl = "ws://127.0.0.1:1880/ws/missions";
    public bool autoReconnect = true;
    public float reconnectDelaySec = 3f;

    [Header("WebSocket – IA (runtime params & mission states)")]
    public string wsUrlIa = "ws://127.0.0.1:1880/ws/ia";

    [Header("UI - Réparations en cours (4 slots max dans la scène)")]
    public TextMeshProUGUI[] ongoingSlots = new TextMeshProUGUI[4];

    [Header("UI - Missions terminées (6 max)")]
    public TextMeshProUGUI[] doneSlots = new TextMeshProUGUI[6];

    [Header("Affichage")]
    [Range(2, 4)] public int visibleActiveSlots = 4;

    [Header("Catalogue de missions")]
    public MissionDef[] missions = DefaultMissions();

    [Header("Debug")]
    public bool logMessages = true;

    bool _missionsUnlocked = false;

    // Conditions pour débloquer les missions de Phase 1
    bool _kosmoCompleted = false;
    bool _hippoAlertTriggered = false;
    public static event System.Action Jumanji1Triggered;


    enum RunState { Idle, Running, Done }

    [Serializable]
    class MissionRuntime
    {
        public string id;
        public int number;
        public bool active = true;
        public int phase = 0;          // 0 = Phase 1, 1 = Phase 2
        public RunState run = RunState.Idle;
    }

    Dictionary<string, MissionRuntime> _rt = new Dictionary<string, MissionRuntime>();

    readonly List<string> _active = new List<string>(4);   // ids visibles (ordre)
    readonly Queue<string> _queue = new Queue<string>();   // ids en attente (phase courante ONLY)
    readonly LinkedList<string> _done = new LinkedList<string>();
    const int MaxDone = 6;

    int _currentPhase = 0; // 0 = P1, 1 = P2

    ClientWebSocket _ws;
    CancellationTokenSource _cts;
    Task _recvTask;

    ClientWebSocket _wsIa;
    CancellationTokenSource _ctsIa;
    Task _recvTaskIa;

    bool _closing;
    readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();

    void Start()
    {
        int maxSlotsInScene = Mathf.Clamp(ongoingSlots?.Length ?? 0, 2, 4);
        visibleActiveSlots = Mathf.Clamp(visibleActiveSlots, 2, maxSlotsInScene);

        InitRuntimeDefaults();

        // Au démarrage : AUCUNE mission visible
        _active.Clear();
        _queue.Clear();
        _done.Clear();
        ClearAllUI();

        for (int i = visibleActiveSlots; i < (ongoingSlots?.Length ?? 0); i++)
        {
            var r = SlotRoot(ongoingSlots[i]);
            if (r) r.SetActive(false);
        }

        // Surtout : on NE fait PAS RebuildFromRuntime ici.
        ConnectCmd();
        ConnectIa();
    }

    public void UnlockMissions()
    {
        if (_missionsUnlocked) return;
        _missionsUnlocked = true;

        // On reconstruit proprement à partir de l’état runtime
        RebuildFromRuntime();
    }

    // Initialise l'état runtime par défaut : 10 missions en Phase 1, le reste en Phase 2.
    void InitRuntimeDefaults()
    {
        _rt.Clear();
        for (int i = 0; i < missions.Length; i++)
        {
            var def = missions[i];
            bool active;
            int phase;

            if (i < 5) { active = true; phase = 0; } // Phase 1
            else if (i < 10) { active = true; phase = 1; } // Phase 2
            else { active = false; phase = 0; } // désactivées

            _rt[def.id] = new MissionRuntime
            {
                id = def.id,
                number = def.number,
                active = active,
                phase = phase,
                run = RunState.Idle
            };
        }
    }


    void Update()
    {
        while (_main.TryDequeue(out var a)) a?.Invoke();
    }

    void OnApplicationQuit() => _ = CloseAllWS();

    [ContextMenu("Reset Board")]
    public void ResetBoard()
    {
        foreach (var r in _rt.Values)
            if (r.run != RunState.Done) r.run = RunState.Idle;

        _active.Clear();
        _queue.Clear();
        _done.Clear();

        RebuildFromRuntime();
    }

    // ---------- UI ----------
    GameObject SlotRoot(TextMeshProUGUI t)
    {
        if (!t) return null;
        var p = t.transform.parent as RectTransform;
        return p ? p.gameObject : t.gameObject;
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
        // Tant que les missions ne sont pas débloquées : on force tout à vide / caché
        if (!_missionsUnlocked)
        {
            ClearAllUI();
            return;
        }

        for (int i = 0; i < ongoingSlots.Length; i++)
        {
            var t = ongoingSlots[i];
            if (!t) continue;
            var root = SlotRoot(t);

            bool show = i < visibleActiveSlots && i < _active.Count;
            if (root && root.activeSelf != show) root.SetActive(show);
            t.text = show ? TitleOf(_active[i]) : "";
        }

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

        // 🔥 Vérifie si on doit déclencher Jumanji
        CheckForJumanjiTrigger();
    }

    void CheckForJumanjiTrigger()
    {
        // Déjà déclenchée -> on ne refait rien
        if (_jumanjiTriggered) return;

        // Missions pas encore débloquées -> on attend
        if (!_missionsUnlocked) return;

        // On ne veut ça qu'en Phase 1
        if (_currentPhase != 0) return;

        // On veut que l'alerte Hippo ait été déclenchée au moins une fois
        if (!_hippoTriggeredOnce) return;

        // "Pool de missions vide" = aucune mission active ni en queue
        if (_active.Count > 0) return;
        if (_queue.Count > 0) return;

        if (!wsAlerts)
        {
            if (logMessages) Debug.LogWarning("[Missions] Jumanji NON déclenchée: wsAlerts non assigné.");
            return;
        }

        _jumanjiTriggered = true;

        // On déclenche bien **jumanji1**
        wsAlerts.SendManualAlert("jumanji1");

        // On notifie l'extérieur (PhaseDebugUI par ex.)
        Jumanji1Triggered?.Invoke();

        if (logMessages)
            Debug.Log("[Missions] Alerte Jumanji1 déclenchée (Phase 1, pool vide, Hippo déjà passée).");

    }

    string TitleOf(string id) => missions.FirstOrDefault(m => m.id == id)?.title ?? id;

    // ---------- Runtime build / guards ----------
    bool IsEligibleNow(string id)
    {
        if (!_rt.TryGetValue(id, out var r)) return false;
        return r.active && r.phase == _currentPhase && r.run != RunState.Done;
    }

    void PurgeQueueForCurrentPhase()
    {
        if (_queue.Count == 0) return;
        var tmp = _queue.ToArray().ToList();
        tmp.RemoveAll(id => !IsEligibleNow(id));
        _queue.Clear();
        foreach (var id in tmp) _queue.Enqueue(id);
    }

    void RefillActiveFromQueue()
    {
        PurgeQueueForCurrentPhase();
        while (_active.Count < visibleActiveSlots && _queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (!IsEligibleNow(next)) { _rt[next].run = RunState.Idle; continue; }
            _active.Add(next);
            _rt[next].run = RunState.Running;
        }
    }

    void RebuildFromRuntime()
    {
        var prevVisible = VisibleActiveSnapshot();

        _active.Clear();
        _queue.Clear();

        // Eligible uniquement pour la phase courante
        var eligible = missions
            .Select(m => _rt[m.id])
            .Where(r => r.phase == _currentPhase && r.active && r.run != RunState.Done)
            .OrderBy(r => r.number)
            .ToList();

        foreach (var r in eligible)
        {
            if (_active.Count < visibleActiveSlots) { _active.Add(r.id); r.run = RunState.Running; }
            else _queue.Enqueue(r.id); // queue ne contient QUE la phase courante
        }

        // Ce qui n’est ni visible ni en queue redevient Idle (hors Done)
        foreach (var r in _rt.Values)
        {
            if (r.run == RunState.Done) continue;
            if (!_active.Contains(r.id) && !_queue.Contains(r.id)) r.run = RunState.Idle;
        }

        var nowVisible = VisibleActiveSnapshot();
        _ = Task.Run(() => NotifyNewlyVisible(prevVisible, nowVisible));

        UpdateUI();
    }

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

    // ---------- WebSocket (IA / params + états mission) ----------
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
                try { await Task.WhenAny(recv, Task.Delay(50)); } catch { }
            }
        }
        catch { }
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

    [Serializable] class Cmd { public string cmd; public string mission; public string id; }

    void HandleCmdMessage(string raw)
    {
        if (logMessages) Debug.Log("[WS-cmd] <= " + raw);

        string intent = null; // start|done|reset
        string token = null;

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

        if (intent == null)
        {
            var s = raw.Trim().ToLowerInvariant();
            if (s == "reset") intent = "reset";
            else if (s.StartsWith("start ")) { intent = "start"; token = raw.Substring(6).Trim(); }
            else if (s.StartsWith("done ")) { intent = "done"; token = raw.Substring(5).Trim(); }
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

    [Serializable]
    class IaJson
    {
        public int visibleActiveSlots;
        public string cmd;
        public int value;
        public int ID;
        public int Active;
        public int Phase;
        public int startPhase;
    }

    void HandleIaMessage(string raw)
    {
        if (logMessages) Debug.Log("[WS-ia] <= " + raw);
        var s = raw.Trim();

        if (s.StartsWith("{"))
        {
            try
            {
                var j = JsonUtility.FromJson<IaJson>(s);
                if (j != null)
                {
                    if (j.visibleActiveSlots >= 2 && j.visibleActiveSlots <= 4)
                    { _main.Enqueue(() => ChangeVisibleSlots(j.visibleActiveSlots)); return; }

                    if (!string.IsNullOrEmpty(j.cmd))
                    {
                        var c = j.cmd.ToLowerInvariant();
                        if (c.Contains("visible"))
                        { _main.Enqueue(() => ChangeVisibleSlots(Mathf.Clamp(j.value, 2, 4))); return; }

                        if (c.Contains("startphase"))
                        { _main.Enqueue(() => BeginPhase(Mathf.Clamp(j.value, 0, 1))); return; }
                    }

                    if (j.startPhase == 0 || j.startPhase == 1)
                    { _main.Enqueue(() => BeginPhase(j.startPhase)); return; }

                    if (j.ID > 0)
                    {
                        int idNum = j.ID;
                        int? act = (j.Active == 0 || j.Active == 1) ? j.Active : (int?)null;
                        int? ph = (j.Phase == 0 || j.Phase == 1) ? j.Phase : (int?)null;
                        _main.Enqueue(() => ApplyMissionUpdateFromNode(idNum, act, ph));
                        return;
                    }
                }
            }
            catch { }
        }

        var lower = s.ToLowerInvariant();
        if (lower.Contains("visible") && lower.Contains("slot"))
        {
            var m = Regex.Match(lower, @"\d+");
            if (m.Success && int.TryParse(m.Value, out var n))
            { _main.Enqueue(() => ChangeVisibleSlots(Mathf.Clamp(n, 2, 4))); return; }
        }

        if (lower.Contains("start") && lower.Contains("phase"))
        {
            var m = Regex.Match(lower, @"\d+");
            if (m.Success && int.TryParse(m.Value, out var p))
            { _main.Enqueue(() => BeginPhase(Mathf.Clamp(p - 1, 0, 1))); return; }
        }

        if (lower.StartsWith("mission/"))
        {
            var parts = lower.Split(new[] { ' ', '\t' });
            var topic = parts[0];
            var numMatch = Regex.Match(topic, @"mission/(\d+)/");
            if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out var idNum))
            {
                int? act = null, ph = null;
                if (topic.EndsWith("/active"))
                    act = (parts.Length > 1 && int.TryParse(parts[1], out var a)) ? a : 1;
                else if (topic.EndsWith("/phase"))
                    ph = (parts.Length > 1 && int.TryParse(parts[1], out var p2)) ? p2 : 0;

                _main.Enqueue(() => ApplyMissionUpdateFromNode(idNum, act, ph));
                return;
            }
        }
    }

    // ---------- Engine ----------
    void BeginPhase(int phase01)
    {
        _currentPhase = Mathf.Clamp(phase01, 0, 1);
        if (logMessages) Debug.Log($"[PHASE] Start Phase {(_currentPhase + 1)}");

        // on reconstruit strictement à partir de la phase courante
        RebuildFromRuntime();
    }

    void ApplyMissionUpdateFromNode(int idNum, int? active, int? phase)
    {
        string id = "m" + idNum.ToString("00");
        if (!_rt.TryGetValue(id, out var r)) return;

        // si visible à l'écran → on ignore les modifs (verrou UX)
        int visIndex = _active.IndexOf(id);
        bool isVisible = visIndex >= 0 && visIndex < visibleActiveSlots;
        if (isVisible)
        {
            if (logMessages) Debug.Log($"[IA] update ignoré pour {id} (visible).");
            return;
        }

        if (active.HasValue) r.active = (active.Value == 1);
        if (phase.HasValue) r.phase = Mathf.Clamp(phase.Value, 0, 1);

        // mise à jour stricte de la queue + refill
        PurgeQueueForCurrentPhase();

        // si éligible à la phase courante et pas déjà planifiée → ajouter
        if (IsEligibleNow(id) && !_queue.Contains(id) && !_active.Contains(id))
        {
            if (_active.Count < visibleActiveSlots) { _active.Add(id); r.run = RunState.Running; }
            else { _queue.Enqueue(id); r.run = RunState.Running; }
        }
        else if (!IsEligibleNow(id))
        {
            r.run = (r.run == RunState.Done) ? RunState.Done : RunState.Idle;
        }

        RefillActiveFromQueue();
        UpdateUI();
    }

    bool TryResolveMissionId(string token, out string id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var t = token.Trim();

        var mId = Regex.Match(t, @"^m(\d{1,2})$", RegexOptions.IgnoreCase);
        if (mId.Success && int.TryParse(mId.Groups[1].Value, out var n1))
        { if (n1 >= 1 && n1 <= missions.Length) { id = "m" + n1.ToString("00"); return true; } }

        if (Regex.IsMatch(t, @"^\d{1,2}$") && int.TryParse(t, out var n2))
        { if (n2 >= 1 && n2 <= missions.Length) { id = "m" + n2.ToString("00"); return true; } }

        string s = Slug(t);
        foreach (var m in missions)
            if (Slug(m.title) == s || Slug(m.startAlias) == s) { id = m.id; return true; }

        return false;
    }
    void OnEnable()
    {
        KosmoGameManager.GameCompleted += OnFlagsGameCompleted;
        EnergyAlertController.HippoAlertTriggered += OnHippoAlertTriggered;
        WSAlerts.StopCommandReceived += OnStopCommandReceived;
    }

    void OnDisable()
    {
        KosmoGameManager.GameCompleted -= OnFlagsGameCompleted;
        EnergyAlertController.HippoAlertTriggered -= OnHippoAlertTriggered;
        WSAlerts.StopCommandReceived -= OnStopCommandReceived;
    }

    void OnFlagsGameCompleted()
    {
        // Kosmo gagné (4/4)
        _kosmoCompleted = true;
        if (logMessages) Debug.Log("[Missions] Kosmo complété (4/4).");
        TryUnlockPhase1Missions();
    }

    void OnHippoAlertTriggered()
    {
        // On a déjà eu au moins une alerte Hippo
        _hippoAlertTriggered = true;
        if (logMessages) Debug.Log("[Missions] Alerte Hippo déclenchée.");
        // (on ne débloque pas encore ici, il faut attendre le stopAlerte à 100%)
    }

    void OnStopCommandReceived()
    {
        // 1) Tant que les missions ne sont pas débloquées,
        //    ce stop correspond à "fin Hippo → Phase 1"
        if (!_missionsUnlocked)
        {
            TryUnlockPhase1Missions();
            return;
        }

        // 2) À partir du moment où les missions sont débloquées,
        //    si on est encore en Phase 1 et que le pool est vide au moment du stopAlerte,
        //    on considère que c'est la fin de Jumanji1 => on passe en Phase 2.
        if (_missionsUnlocked && _currentPhase == 0)
        {
            bool poolVide = (_active.Count == 0 && _queue.Count == 0);
            if (poolVide)
            {
                Debug.Log("[Missions] stopAlerte avec pool Phase 1 vide → passage en Phase 2");
                BeginPhase(1); // 1 = phase 2
            }
            else
            {
                Debug.Log("[Missions] stopAlerte reçu mais pool Phase 1 NON vide → ignoré pour Phase 2");
            }
        }
    }

    void TryUnlockPhase1Missions()
    {
        if (_missionsUnlocked) return;
        if (!_kosmoCompleted) return;
        if (!_hippoAlertTriggered) return;
        if (_currentPhase != 0) return;

        Debug.Log("[Missions] Déblocage des missions de Phase 1 (conditions remplies).");
        UnlockMissions();
    }

    List<string> VisibleActiveSnapshot()
    {
        if (!_missionsUnlocked) return new List<string>();
        return _active.Take(visibleActiveSlots).ToList();
    }

    async void NotifyNewlyVisible(List<string> prev, List<string> now)
    {
        foreach (var id in now)
            if (!prev.Contains(id))
                await SendTextAsync(OutMessageFor(id));
    }

    string OutMessageFor(string id)
    {
        var m = missions.FirstOrDefault(x => x.id == id);
        var r = (m != null && _rt.TryGetValue(id, out var rr)) ? rr : null;
        if (m == null) return "Mission ?";
        string padded = (r != null ? r.number : m.number).ToString("D6");
        return string.IsNullOrWhiteSpace(m.outMessage) ? $"Mission {padded}" : m.outMessage;
    }

    void StartMission(string id)
    {
        if (_active.Contains(id) || _queue.Contains(id)) return;
        var r = _rt[id];
        if (r.run == RunState.Done) return;

        var prevVisible = VisibleActiveSnapshot();

        if (IsEligibleNow(id))
        {
            if (_active.Count < visibleActiveSlots) { _active.Add(id); r.run = RunState.Running; }
            else { _queue.Enqueue(id); r.run = RunState.Running; }
        }

        var nowVisible = VisibleActiveSnapshot();
        _ = Task.Run(() => NotifyNewlyVisible(prevVisible, nowVisible));

        UpdateUI();
    }

    void CompleteMission(string id)
    {
        var r = _rt[id];
        var prevVisible = VisibleActiveSnapshot();

        int i = _active.IndexOf(id);
        if (i >= 0) _active.RemoveAt(i);
        else
        {
            var q = _queue.ToArray().ToList();
            if (q.Remove(id)) { _queue.Clear(); foreach (var k in q) _queue.Enqueue(k); }
        }

        r.run = RunState.Done;
        if (_done.Contains(id)) _done.Remove(id);
        _done.AddFirst(id);
        while (_done.Count > MaxDone) _done.RemoveLast();

        // refill strict phase courante
        RefillActiveFromQueue();

        var nowVisible = VisibleActiveSnapshot();
        _ = Task.Run(() => NotifyNewlyVisible(prevVisible, nowVisible));

        UpdateUI();
        // ---------- Auto Jumanji1 ----------
        // Si :
        // - les missions sont débloquées
        // - on est en Phase 1 (_currentPhase == 0)
        // - plus aucune mission en cours ni en file d'attente
        // - on ne l'a pas déjà déclenchée
        if (!_jumanji1Triggered && _missionsUnlocked && _currentPhase == 0)
        {
            bool noActiveOrQueued = (_active.Count == 0 && _queue.Count == 0);
            if (noActiveOrQueued && alerts)
            {
                _jumanji1Triggered = true;

                // Lance l’alerte visuelle
                alerts.HandleMessage("jumanji1");

                // ➕ Préviens le système de debug / phases
                Jumanji1Triggered?.Invoke();

                if (logMessages)
                    Debug.Log("[Missions] Jumanji1 déclenchée (pool Phase 1 vide).");
            }
        }
    }

    void ChangeVisibleSlots(int newCount)
    {
        int maxSlotsInScene = Mathf.Clamp(ongoingSlots?.Length ?? 0, 2, 4);
        newCount = Mathf.Clamp(newCount, 2, maxSlotsInScene);

        var prevVisible = VisibleActiveSnapshot();
        visibleActiveSlots = newCount;

        RefillActiveFromQueue();

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

    // ---------- Data ----------
    [Serializable]
    public class MissionDef
    {
        public string id;
        public string title;
        public string startAlias;
        public int number = 0;
        public string outMessage;
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
                outMessage = $"Mission {num.ToString("D6")}"
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
