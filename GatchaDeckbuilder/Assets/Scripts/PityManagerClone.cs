using UnityEngine;
using UnityEngine.UI;

public class PityManager : MonoBehaviour
{
    [Header("Pity Settings")]
    [Tooltip("Maximum non-legendary pulls before forcing a Legendary card.")]
    [SerializeField] private int maxPity = 7;

    private int currentPityCount = 0;

    [Header("UI Visual Settings (Optional for AI)")]
    [Tooltip("Assign UI Image squares in order. Leave empty for AI.")]
    [SerializeField] private Image[] pitySquareImages;

    [SerializeField] private Color baseColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
    [SerializeField] private Color highlightedColor = new Color(1f, 0.8f, 0f, 1f);

    [SerializeField] private Vector3 baseSquareScale = Vector3.one;
    [SerializeField] private Vector3 highlightedSquareScale = new Vector3(1.25f, 1.25f, 1f);

    public int CurrentPityCount => currentPityCount;
    public int MaxPity => maxPity;

    private void Start()
    {
        UpdatePityUI();
    }

    public bool ShouldForceLegendary()
    {
        return currentPityCount >= maxPity;
    }

    public void RegisterPull(CardRarity rarity)
    {
        if (rarity == CardRarity.Legendary)
        {
            currentPityCount = 0;
        }
        else
        {
            currentPityCount++;
            if (currentPityCount > maxPity)
            {
                currentPityCount = maxPity;
            }
        }

        UpdatePityUI();
    }

    private void UpdatePityUI()
    {
        if (pitySquareImages == null || pitySquareImages.Length == 0) return;

        for (int i = 0; i < pitySquareImages.Length; i++)
        {
            if (pitySquareImages[i] == null) continue;

            if (i < currentPityCount)
            {
                pitySquareImages[i].color = highlightedColor;
                pitySquareImages[i].transform.localScale = highlightedSquareScale;
            }
            else
            {
                pitySquareImages[i].color = baseColor;
                pitySquareImages[i].transform.localScale = baseSquareScale;
            }
        }
    }
}