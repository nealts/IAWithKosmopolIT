using System;
using System.Collections.Generic;
using UnityEngine;

public enum WSChannel
{
    Missions,
    Kosmo,
    Progress,
    Alerts,
    Hippo
}

[DisallowMultipleComponent]
public class WSConnectionsHub : MonoBehaviour
{
    public static WSConnectionsHub Instance { get; private set; }

    [Header("Server")]
    [SerializeField] private bool useWss = false;
    [SerializeField] private string ip = "127.0.0.1";
    [SerializeField] private int port = 1880;

    [Header("Base path (Node-RED)")]
    [Tooltip("Ex: /ws")]
    [SerializeField] private string basePath = "/ws";

    [Header("Topics")]
    [SerializeField] private string missionsTopic = "mission";
    [SerializeField] private string kosmoTopic = "kosmo";
    [SerializeField] private string progressTopic = "dna";
    [SerializeField] private string alertsTopic = "ia";
    [SerializeField] private string hippoTopic = "hippo";

    public event Action OnConfigChanged;

    private Dictionary<WSChannel, Func<string>> _routes;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _routes = new Dictionary<WSChannel, Func<string>>
        {
            { WSChannel.Missions, () => missionsTopic },
            { WSChannel.Kosmo,    () => kosmoTopic },
            { WSChannel.Progress, () => progressTopic },
            { WSChannel.Alerts,   () => alertsTopic },
            { WSChannel.Hippo, () => hippoTopic },
        };
    }

    public string GetUrl(WSChannel channel)
    {
        var scheme = useWss ? "wss" : "ws";
        var topic = _routes[channel]?.Invoke();

        var cleanBase = basePath.StartsWith("/") ? basePath : "/" + basePath;
        cleanBase = cleanBase.EndsWith("/") ? cleanBase.TrimEnd('/') : cleanBase;

        return $"{scheme}://{ip}:{port}{cleanBase}/{topic}";
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Permet de notifier en play mode quand tu modifies l’IP/topic dans l’inspector
        if (Application.isPlaying)
            OnConfigChanged?.Invoke();
    }
#endif
}