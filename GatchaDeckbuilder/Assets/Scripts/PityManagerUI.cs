using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PityPanelUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GachaManager gachaManager;
    [Tooltip("Set to true if this panel displays Action deck pity, false for Support")]
    [SerializeField] private bool isActionDeckPanel = true;

    [Header("Text Display")]
    [SerializeField] private TextMeshProUGUI pityText;
    [SerializeField] private string textFormat = "Action Pity: {0}/{1}";
    [SerializeField] private bool showMaxThreshold = true;

    [Header("Color Coding")]
    [SerializeField] private bool useColorCoding = true;
    [SerializeField] private Color nearPityColor = Color.yellow;
    [SerializeField] private Color normalColor = Color.white;

    private void OnEnable()
    {
        if (gachaManager == null) gachaManager = FindFirstObjectByType<GachaManager>();
        if (gachaManager != null) gachaManager.OnPityUpdated += RefreshPityUI;
    }

    private void OnDisable()
    {
        if (gachaManager != null) gachaManager.OnPityUpdated -= RefreshPityUI;
    }

    private void RefreshPityUI(int currentPity, int maxThreshold, bool isAction)
    {
        if (isAction != isActionDeckPanel || pityText == null) return;

        string displayText = showMaxThreshold ? string.Format(textFormat, currentPity, maxThreshold) : string.Format(textFormat, currentPity);
        pityText.text = displayText;

        if (useColorCoding)
        {
            int pullsLeft = maxThreshold - currentPity;
            pityText.color = (pullsLeft <= 2) ? nearPityColor : normalColor; // Turns yellow at 2 or fewer pulls left
        }
    }
}