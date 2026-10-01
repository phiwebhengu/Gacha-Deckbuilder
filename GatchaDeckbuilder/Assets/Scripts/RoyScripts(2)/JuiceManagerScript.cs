using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class JuiceFXManager : MonoBehaviour
{
    public static JuiceFXManager Instance { get; private set; }

    [Header("✨ JUICE: Camera Shake Settings ✨")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float commonShakeDuration = 0.15f;
    [SerializeField] private float commonShakeMagnitude = 10f;
    [SerializeField] private float rareShakeDuration = 0.25f;
    [SerializeField] private float rareShakeMagnitude = 20f;
    [SerializeField] private float legendaryShakeDuration = 0.45f;
    [SerializeField] private float legendaryShakeMagnitude = 40f;

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

    private Vector3 originalCamPos;
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

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform != null)
        {
            originalCamPos = cameraTransform.localPosition;
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

    // ✨ CHANGED: Accept 'Rarity' instead of 'CardTier' ✨
    public void TriggerRarityJuice(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common:
                TriggerJuiceEffects(commonShakeDuration, commonShakeMagnitude, commonRareImpactSFX, commonPitch, commonFlashImage);
                break;

            case Rarity.Rare:
                TriggerJuiceEffects(rareShakeDuration, rareShakeMagnitude, commonRareImpactSFX, rarePitch, rareFlashImage);
                break;

            case Rarity.Legendary:
                TriggerJuiceEffects(legendaryShakeDuration, legendaryShakeMagnitude, legendaryImpactSFX, 1.0f, legendaryFlashImage);
                if (legendaryFoundStingSFX != null && sfxSource != null)
                {
                    sfxSource.PlayOneShot(legendaryFoundStingSFX);
                }
                break;

            default:
                TriggerJuiceEffects(commonShakeDuration, commonShakeMagnitude, commonRareImpactSFX, commonPitch, commonFlashImage);
                break;
        }
    }

    private void TriggerJuiceEffects(float shakeDuration, float shakeMagnitude, AudioClip clip, float pitch, Image flashImage)
    {
        if (cameraTransform != null)
        {
            if (activeShakeCoroutine != null) StopCoroutine(activeShakeCoroutine);
            activeShakeCoroutine = StartCoroutine(Routine_ScreenShake(shakeDuration, shakeMagnitude));
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

    private IEnumerator Routine_ScreenShake(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;

            cameraTransform.localPosition = originalCamPos + new Vector3(x, y, 0f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        cameraTransform.localPosition = originalCamPos;
    }

    private IEnumerator Routine_FlashUI(Image flashImage)
    {
        ResetFlashImages();

        float elapsed = 0f;
        SetImageOpacity(flashImage, maxFlashOpacity);

        while (elapsed < flashFadeDuration)
        {
            elapsed += Time.deltaTime;
            float currentOpacity = Mathf.Lerp(maxFlashOpacity, 0f, elapsed / flashFadeDuration);
            SetImageOpacity(flashImage, currentOpacity);
            yield return null;
        }

        SetImageOpacity(flashImage, 0f);
    }

    private void SetImageOpacity(Image img, float alpha)
    {
        if (img == null) return;
        Color col = img.color;
        col.a = alpha;
        img.color = col;
    }
}