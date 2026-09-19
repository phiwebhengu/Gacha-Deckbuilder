using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PityPanelUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private PityManager pityManager;

    [Header("Pip Indicator System")]
    [Tooltip("List of UI Image elements representing pity pips/gems in sequence.")]
    [SerializeField] private List<Image> pityPips = new List<Image>();

    [Header("Pip Visual States")]
    [SerializeField] private Color activeColor = Color.yellow;
    [SerializeField] private Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    [SerializeField] private Vector3 activeScale = new Vector3(1.2f, 1.2f, 1f);
    [SerializeField] private Vector3 inactiveScale = Vector3.one;

    [Header("Optional UI Components")]
    [SerializeField] private TextMeshProUGUI pityCounterText;

    private void OnEnable()
    {
        if (pityManager != null)
        {
            pityManager.OnPityUpdated += RefreshPityUI;
        }
    }

    private void OnDisable()
    {
        if (pityManager != null)
        {
            pityManager.OnPityUpdated -= RefreshPityUI;
        }
    }

    private void RefreshPityUI(int current, int maxThreshold)
    {
        // Update text counter if assigned
        if (pityCounterText != null)
        {
            pityCounterText.text = $"Pity: {current} / {maxThreshold}";
        }

        // Update pip indicators
        for (int i = 0; i < pityPips.Count; i++)
        {
            if (pityPips[i] == null) continue;

            bool isActive = i < current;

            pityPips[i].color = isActive ? activeColor : inactiveColor;
            pityPips[i].transform.localScale = isActive ? activeScale : inactiveScale;
        }
    }
}