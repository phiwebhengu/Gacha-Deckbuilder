using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CardVisual : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The main Image component displaying the card art")]
    public Image cardImage;

    [Header("VFX / Juice")]
    [Tooltip("An overlay image used for the glowing pop effect")]
    [SerializeField] private Image glowOverlay;

    /// <summary>
    /// Sets up the visual representation of an Action Card.
    /// </summary>
    public void Setup(ActionCardData data, Sprite sprite)
    {
        if (cardImage != null && sprite != null)
        {
            cardImage.sprite = sprite;
        }
    }

    /// <summary>
    /// Sets up the visual representation of a Support Card.
    /// </summary>
    public void Setup(SupportCardData data, Sprite sprite)
    {
        if (cardImage != null && sprite != null)
        {
            cardImage.sprite = sprite;
        }
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
            Debug.LogWarning($"Could not parse tier '{tierString}'. Defaulting to Common.");
            PlayReveal(Rarity.Common);
        }
    }

    private IEnumerator Routine_Reveal(Rarity tier)
    {
        // Determine animation parameters based on rarity
        (float popScale, float duration, Color glowColor) = tier switch
        {
            Rarity.Legendary => (1.5f, 0.35f, new Color(1f, 0.85f, 0.2f, 0.9f)), // Gold glow
            Rarity.Rare => (1.25f, 0.22f, new Color(0.7f, 0.4f, 1f, 0.6f)), // Purple glow
            _ => (1.1f, 0.15f, new Color(1f, 1f, 1f, 0.3f)),      // White glow (Common)
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