using System;
using UnityEngine;

public class EnergyAlertController : MonoBehaviour
{
    public static event Action HippoAlertTriggered;
    [Header("Références")]
    public DNA2DAnimator dna;
    public WSAlerts wsAlerts;
    public GameObject lowEnergyBG;

    [Header("Seuils énergie")]
    [Range(0f, 1f)] public float thresholdLow = 0.20f;
    [Range(0f, 1f)] public float thresholdZero = 0.00f;

    [Header("Blink Settings")]
    public float minBlinkSpeed = 0.2f;   // lent à 20%
    public float maxBlinkSpeed = 6.0f;   // rapide vers 0%
    public float alphaMin = 0.0f;
    public float alphaMax = 1.0f;

    CanvasGroup bgCg;
    bool hippoAlertSent = false;
    float blinkTimer = 0f;
    float currentEnergy;

    bool stopAlertSentAtFull = false;

    void Start()
    {
        if (!lowEnergyBG) return;

        lowEnergyBG.SetActive(false);

        bgCg = lowEnergyBG.GetComponent<CanvasGroup>();
        if (!bgCg) bgCg = lowEnergyBG.AddComponent<CanvasGroup>();
        bgCg.alpha = 0f;
    }

    void Update()
    {
        if (!dna || !lowEnergyBG) return;

        currentEnergy = Mathf.Clamp01(dna.energy);
        if (currentEnergy >= 0.999f)
        {
            if (!stopAlertSentAtFull)
            {
                stopAlertSentAtFull = true;

                if (wsAlerts)
                    wsAlerts.ForceStopAlert(asCommand: true);
            }
        }
        else
        {
            stopAlertSentAtFull = false;
        }
        // ============================
        // 1) Blink dynamique (lent → rapide)
        // ============================
        if (currentEnergy < thresholdLow && currentEnergy > thresholdZero)
        {
            if (!lowEnergyBG.activeSelf)
                lowEnergyBG.SetActive(true);

            // t = 0 → 20% → blink lent
            // t = 1 → 0%  → blink rapide
            float t = Mathf.InverseLerp(thresholdLow, thresholdZero, currentEnergy);

            float blinkSpeed = Mathf.Lerp(minBlinkSpeed, maxBlinkSpeed, t);

            blinkTimer += Time.deltaTime * blinkSpeed;

            float alpha = Mathf.Lerp(alphaMin, alphaMax, (Mathf.Sin(blinkTimer) * 0.5f + 0.5f));
            bgCg.alpha = alpha;
        }
        else if (currentEnergy <= thresholdZero)
        {
            // ============================
            // 2) 0% = fixe 100%
            // ============================
            if (!lowEnergyBG.activeSelf)
                lowEnergyBG.SetActive(true);

            bgCg.alpha = 1f;
        }
        else
        {
            // ============================
            // 3) Énergie normale → off
            // ============================
            lowEnergyBG.SetActive(false);
            bgCg.alpha = 0f;
        }

        // ============================
        // 4) Déclencher alerte Hippo à 0%
        // ============================
        if (currentEnergy <= thresholdZero && !hippoAlertSent)
        {
            hippoAlertSent = true;

            if (wsAlerts)
                wsAlerts.SendManualAlert("hippo");

            // On signale à l'extérieur : "Alerte Hippo enclenchée"
            HippoAlertTriggered?.Invoke();
        }

        // Réarmement si l'énergie remonte > 0%
        if (currentEnergy > thresholdZero + 0.01f)
        {
            hippoAlertSent = false;
        }


    }
}
