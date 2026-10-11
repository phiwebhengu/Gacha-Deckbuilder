using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Image))]
public class JuicyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("✨ Scale Settings ✨")]
    [SerializeField] private float hoverScale = 1.08f;
    [SerializeField] private float pressShrinkScale = 0.92f;
    [SerializeField] private float releaseBounceScale = 1.15f;
    [SerializeField] private float scaleLerpSpeed = 15f;

    [Header("✨ Color Settings ✨")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = new Color(1f, 0.92f, 0.5f, 1f); // Warm highlighted tint
    [SerializeField] private Color pressColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private float colorLerpSpeed = 15f;

    [Header("✨ Audio Clips ✨")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hoverSFX;
    [SerializeField] private AudioClip clickDownSFX;
    [SerializeField] private AudioClip clickReleaseSFX;
    [SerializeField] private float hoverPitch = 1.1f;
    [SerializeField] private float clickPitch = 1.0f;

    private Image buttonImage;
    private Vector3 baseScale;
    private Vector3 targetScale;
    private Color targetColor;
    private Coroutine bounceCoroutine;

    private void Awake()
    {
        buttonImage = GetComponent<Image>();
        baseScale = transform.localScale;
        targetScale = baseScale;
        targetColor = normalColor;

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
    }

    private void OnEnable()
    {
        // Reset state whenever button is re-enabled
        transform.localScale = baseScale;
        if (buttonImage != null) buttonImage.color = normalColor;
        targetScale = baseScale;
        targetColor = normalColor;
    }

    private void Update()
    {
        // Smoothly interpolate scale and color towards targets
        if (bounceCoroutine == null)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleLerpSpeed);
        }

        if (buttonImage != null)
        {
            buttonImage.color = Color.Lerp(buttonImage.color, targetColor, Time.deltaTime * colorLerpSpeed);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = baseScale * hoverScale;
        targetColor = hoverColor;
        PlaySFX(hoverSFX, hoverPitch);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = baseScale;
        targetColor = normalColor;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (bounceCoroutine != null) StopCoroutine(bounceCoroutine);

        targetScale = baseScale * pressShrinkScale;
        targetColor = pressColor;
        PlaySFX(clickDownSFX, clickPitch);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        PlaySFX(clickReleaseSFX, clickPitch * 1.05f);

        // Quick punchy shrink -> pop -> return sequence
        if (bounceCoroutine != null) StopCoroutine(bounceCoroutine);
        bounceCoroutine = StartCoroutine(Routine_JuicyBounce());
    }

    private IEnumerator Routine_JuicyBounce()
    {
        float t = 0f;
        Vector3 startScale = transform.localScale;
        Vector3 peakScale = baseScale * releaseBounceScale;

        // Phase 1: Pop out to overshoot scale
        while (t < 0.08f)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(startScale, peakScale, t / 0.08f);
            yield return null;
        }

        // Phase 2: Settle back down to hover or normal scale
        t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(peakScale, targetScale, t / 0.12f);
            yield return null;
        }

        bounceCoroutine = null;
    }

    private void PlaySFX(AudioClip clip, float pitch)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip);
    }
}