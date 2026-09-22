using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum DeckType
{
    Action,
    Support
}

public class DeckButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Deck Config")]
    [SerializeField] private DeckType deckType = DeckType.Action;
    [SerializeField] private RectTransform deckTransform;

    [Header("Deck Category Settings")]
    [SerializeField] private bool isSupportDeck = false;

    [Header("Target Managers")]
    [SerializeField] private TokenSelectionManager tokenManager;
    [SerializeField] private DrawTimerManager timerManager;

    [Header("Visual Tuning")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.08f, 1.08f, 1f);
    [SerializeField] private float scaleSpeed = 12f;

    [Header("Invalid Click Flash Settings")]
    [SerializeField] private Color invalidClickColor = Color.red;
    [SerializeField] private float flashDuration = 0.25f;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip cardSwipeSFX;
    [SerializeField] private AudioClip errorSFX;

    [Header("Swipe Pitch Dynamic Range")]
    [SerializeField] private bool randomizeSwipePitch = true;
    [SerializeField] private float minPitch = 0.9f;
    [SerializeField] private float maxPitch = 1.1f;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private Image buttonImage;
    private Color originalImageColor = Color.white;
    private Coroutine activeFlashCoroutine;

    public DeckType Type => deckType;
    public bool IsSupportDeck => isSupportDeck || deckType == DeckType.Support;
    public RectTransform DeckTransform => deckTransform != null ? deckTransform : GetComponent<RectTransform>();

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;

        buttonImage = GetComponent<Image>();
        if (buttonImage != null)
        {
            originalImageColor = buttonImage.color;
        }

        if (deckType == DeckType.Support)
        {
            isSupportDeck = true;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        if (tokenManager == null)
        {
            tokenManager = GetComponentInParent<TokenSelectionManager>();
            if (tokenManager == null) tokenManager = FindFirstObjectByType<TokenSelectionManager>();
        }

        if (timerManager == null)
        {
            timerManager = FindFirstObjectByType<DrawTimerManager>();
        }
    }

    private void OnValidate()
    {
        if (deckType == DeckType.Support)
        {
            isSupportDeck = true;
        }
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (timerManager != null && timerManager.IsDrawPhaseActive)
        {
            targetScale = hoverScale;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 1. Outside active draw window check
        if (timerManager != null && !timerManager.IsDrawPhaseActive)
        {
            Debug.LogWarning("[DeckButton] Draw attempted outside active draw window!");
            PlaySound(errorSFX, 1.0f);
            FlashInvalidClick();
            return;
        }

        // 2. Trigger deck selection and play swipe sound
        if (tokenManager != null)
        {
            tokenManager.OnDeckSelected(this);

            float pitch = randomizeSwipePitch ? Random.Range(minPitch, maxPitch) : 1.0f;
            PlaySound(cardSwipeSFX, pitch);
        }
    }

    public void FlashInvalidClick()
    {
        if (buttonImage == null) return;

        if (activeFlashCoroutine != null)
        {
            StopCoroutine(activeFlashCoroutine);
        }

        activeFlashCoroutine = StartCoroutine(Routine_FlashRed());
    }

    private IEnumerator Routine_FlashRed()
    {
        if (buttonImage == null) yield break;

        buttonImage.color = invalidClickColor;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            buttonImage.color = Color.Lerp(invalidClickColor, originalImageColor, elapsed / flashDuration);
            yield return null;
        }

        buttonImage.color = originalImageColor;
        activeFlashCoroutine = null;
    }

    private void PlaySound(AudioClip clip, float pitch = 1.0f)
    {
        if (clip == null || audioSource == null) return;

        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip);
    }
}