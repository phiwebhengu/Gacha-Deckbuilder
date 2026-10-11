using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class JuiceFXManager : MonoBehaviour
{
    public static JuiceFXManager Instance { get; private set; }

    [Header("✨ JUICE: UI Background Shake Settings ✨")]
    [Tooltip("Assign your UI Background Image or Panel RectTransform here.")]
    [SerializeField] private RectTransform backgroundTransform;

    [Header("Common Shake")]
    [SerializeField] private float commonShakeDuration = 0.15f;
    [SerializeField] private float commonShakeMagnitude = 12f;
    [SerializeField] private float commonShakeRotation = 1.5f; // Max rotation angle in degrees

    [Header("Rare Shake")]
    [SerializeField] private float rareShakeDuration = 0.25f;
    [SerializeField] private float rareShakeMagnitude = 25f;
    [SerializeField] private float rareShakeRotation = 3.5f;

    [Header("Legendary Shake")]
    [SerializeField] private float legendaryShakeDuration = 0.45f;
    [SerializeField] private float legendaryShakeMagnitude = 50f;
    [SerializeField] private float legendaryShakeRotation = 7.0f;

    [Header("✨ JUICE: Audio Settings ✨")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Shared sound effect for Common & Rare draws.")]
    [SerializeField] private AudioClip commonRareImpactSFX;
    [Tooltip("Unique impact sound effect for Legendary draws.")]
    [SerializeField] private AudioClip legendaryImpactSFX;
    [Tooltip("Bonus sting/fanfare sound played alongside Legendary reveals.")]
    [SerializeField] private AudioClip legendaryFoundStingSFX;

    [SerializeField] private float commonPitch = 1.0f;
    [SerializeField] private float rarePitch = 1.25f;

    [Header("✨ JUICE: Rarity Flash Overlay Visuals ✨")]
    [SerializeField] private Image commonFlashImage;
    [SerializeField] private Image rareFlashImage;
    [SerializeField] private Image legendaryFlashImage;

    [Tooltip("Peak opacity when a rarity flash triggers (0 to 1).")]
    [Range(0f, 1f)]
    [SerializeField] private float maxFlashOpacity = 0.75f;
    [SerializeField] private float flashFadeDuration = 0.35f;

    private Vector2 originalAnchoredPos;
    private Quaternion originalRotation;
    private Coroutine activeShakeCoroutine;
    private Coroutine activeFlashCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (backgroundTransform != null)
        {
            originalAnchoredPos = backgroundTransform.anchoredPosition;
            originalRotation = backgroundTransform.localRotation;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }

        ResetFlashImages();
    }

    private void ResetFlashImages()
    {
        if (commonFlashImage != null) SetImageOpacity(commonFlashImage, 0f);
        if (rareFlashImage != null) SetImageOpacity(rareFlashImage, 0f);
        if (legendaryFlashImage != null) SetImageOpacity(legendaryFlashImage, 0f);
    }

    public void TriggerRarityJuice(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common:
                TriggerJuiceEffects(commonShakeDuration, commonShakeMagnitude, commonShakeRotation, commonRareImpactSFX, commonPitch, commonFlashImage);
                break;

            case Rarity.Rare:
                TriggerJuiceEffects(rareShakeDuration, rareShakeMagnitude, rareShakeRotation, commonRareImpactSFX, rarePitch, rareFlashImage);
                break;

            case Rarity.Legendary:
                TriggerJuiceEffects(legendaryShakeDuration, legendaryShakeMagnitude, legendaryShakeRotation, legendaryImpactSFX, 1.0f, legendaryFlashImage);
                if (legendaryFoundStingSFX != null && sfxSource != null)
                {
                    sfxSource.PlayOneShot(legendaryFoundStingSFX);
                }
                break;

            default:
                TriggerJuiceEffects(commonShakeDuration, commonShakeMagnitude, commonShakeRotation, commonRareImpactSFX, commonPitch, commonFlashImage);
                break;
        }
    }

    private void TriggerJuiceEffects(float duration, float magnitude, float rotationMagnitude, AudioClip clip, float pitch, Image flashImage)
    {
        if (backgroundTransform != null)
        {
            if (activeShakeCoroutine != null) StopCoroutine(activeShakeCoroutine);
            activeShakeCoroutine = StartCoroutine(Routine_UIShake(duration, magnitude, rotationMagnitude));
        }

        if (sfxSource != null && clip != null)
        {
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip);
        }

        if (flashImage != null)
        {
            if (activeFlashCoroutine != null) StopCoroutine(activeFlashCoroutine);
            activeFlashCoroutine = StartCoroutine(Routine_FlashUI(flashImage));
        }
    }

    private IEnumerator Routine_UIShake(float duration, float positionMagnitude, float rotationMagnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Calculate a decay factor so the shake smoothly dies down near the end
            float damping = 1f - (elapsed / duration);

            // Position offset in UI space
            float offsetX = Random.Range(-1f, 1f) * positionMagnitude * damping;
            float offsetY = Random.Range(-1f, 1f) * positionMagnitude * damping;
            backgroundTransform.anchoredPosition = originalAnchoredPos + new Vector2(offsetX, offsetY);

            // Z-axis rotation offset
            float offsetAngle = Random.Range(-1f, 1f) * rotationMagnitude * damping;
            backgroundTransform.localRotation = originalRotation * Quaternion.Euler(0f, 0f, offsetAngle);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Snap cleanly back to rest transform
        backgroundTransform.anchoredPosition = originalAnchoredPos;
        backgroundTransform.localRotation = originalRotation;
        activeShakeCoroutine = null;
    }

    private IEnumerator Routine_FlashUI(Image flashImage)
    {
        ResetFlashImages();

        float elapsed = 0f;
        SetImageOpacity(flashImage, maxFlashOpacity);

        while (elapsed < flashFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float currentOpacity = Mathf.Lerp(maxFlashOpacity, 0f, elapsed / flashFadeDuration);
            SetImageOpacity(flashImage, currentOpacity);
            yield return null;
        }

        SetImageOpacity(flashImage, 0f);
        activeFlashCoroutine = null;
    }

    private void SetImageOpacity(Image img, float alpha)
    {
        if (img == null) return;
        Color col = img.color;
        col.a = alpha;
        img.color = col;
    }
}