using System;
using TMPro;
using UnityEngine;

public class WSMissionsBoard : MonoBehaviour
{
    [Header("WebSocket Hub")]
    public KosmoWebSocketHub hub;
    public string endpointIa = "ia";
    public string endpointCmd = "missions";

    [Header("UI")]
    public TextMeshProUGUI[] ongoingSlots;

    [Header("Debug")]
    public bool logMessages = true;

    bool _moduleActif = false;

    bool _kosmoCompleted = false;
    bool _hippoReceived = false;
    bool _stopAlerteReceived = false;

    void Start()
    {
        ClearUI();

        if (hub == null)
            hub = FindAnyObjectByType<KosmoWebSocketHub>();

        if (hub != null)
            hub.Message += OnHubMessage;
    }

    void OnEnable()
    {
        KosmoGameManager.GameCompleted += OnKosmoCompleted;
    }

    void OnDisable()
    {
        KosmoGameManager.GameCompleted -= OnKosmoCompleted;

        if (hub != null)
            hub.Message -= OnHubMessage;
    }

    // ----------- CONDITION 1 : Kosmo completed -----------
    void OnKosmoCompleted()
    {
        _kosmoCompleted = true;

        if (logMessages)
            Debug.Log("[Missions] Kosmo complété.");

        CheckActivation();
    }

    // ----------- WS RECEIVER -----------
    void OnHubMessage(string endpoint, string raw)
    {
        if (logMessages)
            Debug.Log($"[WS:{endpoint}] <= {raw}");

        if (endpoint == endpointIa)
        {
            var msg = raw.Trim().ToLower();

            if (msg == "hippo")
            {
                _hippoReceived = true;
                Debug.Log("[Missions] Hippo reçu.");
                CheckActivation();
            }

            if (msg == "stopalerte")
            {
                _stopAlerteReceived = true;
                Debug.Log("[Missions] stopAlerte reçu.");
                CheckActivation();
            }
        }

        // Si module actif → on affiche les messages missions
        if (_moduleActif && endpoint == endpointCmd)
        {
            if (ongoingSlots != null && ongoingSlots.Length > 0)
                ongoingSlots[0].text = raw;
        }
    }

    // ----------- CHECK GLOBAL -----------
    void CheckActivation()
    {
        if (_moduleActif) return;

        if (_kosmoCompleted && _hippoReceived && _stopAlerteReceived)
        {
            _moduleActif = true;
            Debug.Log("Module de mission maintenant actif");
        }
    }

    void ClearUI()
    {
        if (ongoingSlots == null) return;

        foreach (var t in ongoingSlots)
            if (t) t.text = "";
    }
}