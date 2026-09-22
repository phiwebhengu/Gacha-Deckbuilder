using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CardVisual : MonoBehaviour
{
    [Header("Visual References")]
    [Tooltip("The main Image component displaying the card art")]
    public Image cardImage;

    [Tooltip("An overlay image used for the glowing pop effect")]
    [SerializeField] private Image glowOverlay;

    [Tooltip("Optional: An image to color-code based on rarity (e.g., a border or background element)")]
    [SerializeField] private Image tierImage;

    [Header("Rarity Tier Colors")]
    [SerializeField] private Color commonColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [SerializeField] private Color rareColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0f, 1f);

    /// <summary>
    /// Sets up the visual representation of an Action Card (Art + Rarity Color only).
    /// </summary>
    public void Setup(ActionCardData data, Sprite sprite)
    {
        if (cardImage != null && sprite != null)
        {
            cardImage.sprite = sprite;
        }
        ApplyRarityColor(data.Tier);
    }

    /// <summary>
    /// Sets up the visual representation of a Support Card (Art + Rarity Color only).
    /// </summary>
    public void Setup(SupportCardData data, Sprite sprite)
    {
        if (cardImage != null && sprite != null)
        {
            cardImage.sprite = sprite;
        }
        ApplyRarityColor(data.Tier);
    }

    /// <summary>
    /// Triggers the reveal animation using the Rarity Enum.
    /// </summary>
    public void PlayReveal(Rarity tier)
    {
        StartCoroutine(Routine_Reveal(tier));
    }

    /// <summary>
    /// OVERLOAD: Triggers the reveal animation using a string (e.g., data.Tier).
    /// Automatically parses "Legendary", "Rare", or "Common" into the Enum.
    /// </summary>
    public void PlayReveal(string tierString)
    {
        if (System.Enum.TryParse(tierString, true, out Rarity tier))
        {
            PlayReveal(tier);
        }
        else
        {
            // Fallback to Common if the string doesn't match the enum
            PlayReveal(Rarity.Common);
        }
    }

    private void ApplyRarityColor(string tierString)
    {
        if (tierImage == null) return;

        if (System.Enum.TryParse(tierString, true, out Rarity tier))
        {
            tierImage.color = tier switch
            {
                Rarity.Legendary => legendaryColor,
                Rarity.Rare => rareColor,
                _ => commonColor
            };
        }
        else
        {
            tierImage.color = commonColor;
        }
    }

    private IEnumerator Routine_Reveal(Rarity tier)
    {
        // Determine animation parameters based on rarity
        (float popScale, float duration, Color glowColor) = tier switch
        {
            Rarity.Legendary => (1.5f, 0.35f, legendaryColor * 1.5f), // Boosted for glow
            Rarity.Rare => (1.25f, 0.22f, rareColor * 1.5f),
            _ => (1.1f, 0.15f, commonColor * 1.5f),
        };

        Vector3 startScale = transform.localScale;
        Vector3 poppedScale = startScale * popScale;

        // Activate and set glow overlay
        if (glowOverlay != null)
        {
            glowOverlay.color = glowColor;
            glowOverlay.gameObject.SetActive(true);
        }

        // Animate the pop and fade
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Scale down from popped size back to normal
            transform.localScale = Vector3.Lerp(poppedScale, startScale, t);

            // Fade out the glow overlay
            if (glowOverlay != null)
            {
                Color c = glowOverlay.color;
                c.a = Mathf.Lerp(glowColor.a, 0f, t);
                glowOverlay.color = c;
            }

            yield return null;
        }

        // Ensure exact final state
        transform.localScale = startScale;
        if (glowOverlay != null)
        {
            glowOverlay.gameObject.SetActive(false);
        }
    }
}