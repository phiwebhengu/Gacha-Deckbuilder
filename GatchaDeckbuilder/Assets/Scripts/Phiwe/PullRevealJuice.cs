using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PullRevealJuice : MonoBehaviour
{
    [SerializeField] private Image glowOverlay;

    public void PlayReveal(Rarity tier)
    {
        StartCoroutine(Routine_Reveal(tier));
    }

    private IEnumerator Routine_Reveal(Rarity tier)
    {
        (float popScale, float duration, Color glowColor) = tier switch
        {
            Rarity.Legendary => (1.5f, 0.35f, new Color(1f, 0.85f, 0.2f, 0.9f)),
            Rarity.Rare      => (1.25f, 0.22f, new Color(0.7f, 0.4f, 1f, 0.6f)),
            _                => (1.1f, 0.15f, new Color(1f, 1f, 1f, 0.3f)),
        };

        Vector3 startScale = transform.localScale;
        Vector3 poppedScale = startScale * popScale;

        if (glowOverlay != null)
        {
            glowOverlay.color = glowColor;
            glowOverlay.gameObject.SetActive(true);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            transform.localScale = Vector3.Lerp(poppedScale, startScale, t);
            if (glowOverlay != null)
            {
                Color c = glowOverlay.color;
                c.a = Mathf.Lerp(glowColor.a, 0f, t);
                glowOverlay.color = c;
            }
            yield return null;
        }

        transform.localScale = startScale;
        if (glowOverlay != null) glowOverlay.gameObject.SetActive(false);
    }
}