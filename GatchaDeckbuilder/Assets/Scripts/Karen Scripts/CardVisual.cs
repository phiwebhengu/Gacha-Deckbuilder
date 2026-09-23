using UnityEngine;
using UnityEngine.UI;

public class CardVisual : MonoBehaviour
{
    [Header("Visual References")]
    public Image cardImage;
    //[SerializeField] private Image tierImage;

    [Header("Rarity Tier Colors")]
    [SerializeField] private Color commonColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [SerializeField] private Color rareColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0f, 1f);

    public void Setup(ActionCardData data, Sprite sprite)
    {
        if (cardImage != null && sprite != null) cardImage.sprite = sprite;
        ApplyRarityColor(data.Tier); // Passes a string from CSV
    }

    public void Setup(SupportCardData data, Sprite sprite)
    {
        if (cardImage != null && sprite != null) cardImage.sprite = sprite;
        ApplyRarityColor(data.Tier); // Passes a string from CSV
    }

    public void PlayReveal(Rarity tier)
    {
        // Instant apply, no animation
        ApplyRarityColor(tier); // Passes the Rarity enum
    }

    public void PlayReveal(string tierString)
    {
        if (System.Enum.TryParse(tierString, true, out Rarity tier))
            PlayReveal(tier);
        else
            PlayReveal(Rarity.Common);
    }

    // Overload 1: Accepts string (from CSV data)
    private void ApplyRarityColor(string tierString)
    {
        if (System.Enum.TryParse(tierString, true, out Rarity tier))
        {
            ApplyRarityColor(tier);
        }
        else
        {
            ApplyRarityColor(Rarity.Common);
        }
    }

    // Overload 2: Accepts Rarity enum (from Gacha pull result)
    private void ApplyRarityColor(Rarity tier)
    {
        /*if (tierImage == null) return;

        tierImage.color = tier switch
        {
            Rarity.Legendary => legendaryColor,
            Rarity.Rare => rareColor,
            _ => commonColor
        };*/
    }
}