using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MissionEngine : MonoBehaviour
{
    public enum RunState { Idle, Running, Done }

    [Serializable]
    public class MissionDef
    {
        public string id;
        public string title;
        public string startAlias;
        public int number;
        public string outMessage;

        public bool actif = true;   // activée ou non
        public bool phase;          // false = Phase 1, true = Phase 2
    }

    [Serializable]
    class MissionRuntime
    {
        public string id;
        public int number;
        public bool active;
        public int phase;
        public RunState run = RunState.Idle;
    }

    [Header("Config")]
    public MissionDef[] missions;
    [Range(2, 4)] public int visibleActiveSlots = 4;

    public int CurrentPhase => _currentPhase;

    public event Action<List<string>, List<string>> OnVisibleChanged;
    public event Action<string> OnMissionCompleted;
    public event Action<int> OnPhaseChanged;

    Dictionary<string, MissionRuntime> _rt = new();
    List<string> _active = new();
    Queue<string> _queue = new();
    LinkedList<string> _done = new();

    const int MaxDone = 6;
    int _currentPhase = 0;

    void Awake()
    {
        InitRuntime();
        Rebuild();
    }

    void InitRuntime()
    {
        _rt.Clear();

        foreach (var def in missions)
        {
            _rt[def.id] = new MissionRuntime
            {
                id = def.id,
                number = def.number,
                active = def.actif,
                phase = def.phase ? 1 : 0,
                run = RunState.Idle
            };
        }
    }

    bool IsEligible(string id)
    {
        var r = _rt[id];
        return r.active && r.phase == _currentPhase && r.run != RunState.Done;
    }

    public void BeginPhase(int phase01)
    {
        _currentPhase = Mathf.Clamp(phase01, 0, 1);
        Rebuild();
        OnPhaseChanged?.Invoke(_currentPhase);
    }

    public void StartMission(string id)
    {
        if (!_rt.ContainsKey(id)) return;

        var r = _rt[id];
        if (r.run == RunState.Done) return;

        var prev = VisibleSnapshot();

        if (IsEligible(id))
        {
            if (_active.Count < visibleActiveSlots)
            {
                _active.Add(id);
                r.run = RunState.Running;
            }
            else
            {
                _queue.Enqueue(id);
                r.run = RunState.Running;
            }
        }

        NotifyVisibleChanged(prev);
    }

    public void CompleteMission(string id)
    {
        if (!_rt.ContainsKey(id)) return;

        var prev = VisibleSnapshot();

        _active.Remove(id);

        var q = _queue.ToList();
        if (q.Remove(id))
        {
            _queue.Clear();
            foreach (var k in q)
                _queue.Enqueue(k);
        }

        var r = _rt[id];
        r.run = RunState.Done;

        if (_done.Contains(id)) _done.Remove(id);
        _done.AddFirst(id);
        while (_done.Count > MaxDone)
            _done.RemoveLast();

        RefillFromQueue();

        OnMissionCompleted?.Invoke(id);
        NotifyVisibleChanged(prev);
    }

    void RefillFromQueue()
    {
        while (_active.Count < visibleActiveSlots && _queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (!IsEligible(next)) continue;

            _active.Add(next);
            _rt[next].run = RunState.Running;
        }
    }

    void Rebuild()
    {
        var prev = VisibleSnapshot();

        _active.Clear();
        _queue.Clear();

        var eligible = missions
            .Select(m => _rt[m.id])
            .Where(r => r.active && r.phase == _currentPhase && r.run != RunState.Done)
            .OrderBy(r => r.number);

        foreach (var r in eligible)
        {
            if (_active.Count < visibleActiveSlots)
            {
                _active.Add(r.id);
                r.run = RunState.Running;
            }
            else
            {
                _queue.Enqueue(r.id);
                r.run = RunState.Running;
            }
        }

        NotifyVisibleChanged(prev);
    }

    List<string> VisibleSnapshot()
    {
        return _active.Take(visibleActiveSlots).ToList();
    }

    void NotifyVisibleChanged(List<string> prev)
    {
        var now = VisibleSnapshot();
        OnVisibleChanged?.Invoke(prev, now);
    }

    public List<string> GetVisible() => VisibleSnapshot();
    public List<string> GetDone() => _done.ToList();
}