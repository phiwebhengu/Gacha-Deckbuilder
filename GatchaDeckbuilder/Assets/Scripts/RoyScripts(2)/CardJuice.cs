using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(Image))]
public class CardJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Scale Parameters")]
    [SerializeField] private Vector3 normalScale = Vector3.one;
    [SerializeField] private Vector3 hoveredScale = new Vector3(1.08f, 1.08f, 1f);
    [SerializeField] private Vector3 selectedScale = new Vector3(1.12f, 1.12f, 1f);
    [SerializeField] private float transitionSpeed = 15f;

    [Header("Color Parameters")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color highlightColor = new Color(1f, 0.92f, 0.65f, 1f); // Warm highlight glow
    [SerializeField] private Color selectedColor = new Color(0.6f, 1f, 0.6f, 1f);   // Distinct selection color

    [Header("Audio Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hoverSFX;
    [SerializeField] private AudioClip selectSFX;
    [SerializeField] private AudioClip deselectSFX;

    // State Variables
    private Image cardImage;
    private Button cardButton;
    private bool isSelected = false;
    private bool isHovered = false;

    private Coroutine activeScaleCoroutine;
    private Coroutine activeColorCoroutine;

    private void Awake()
    {
        cardImage = GetComponent<Image>();
        cardButton = GetComponent<Button>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        // Initialize state
        transform.localScale = normalScale;
        cardImage.color = normalColor;
    }

    // --- EVENT SYSTEM INTERFACES ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!cardButton.interactable) return;

        isHovered = true;

        if (!isSelected)
        {
            ApplyScale(hoveredScale);
            ApplyColor(highlightColor);
            PlaySFX(hoverSFX);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!cardButton.interactable) return;

        isHovered = false;

        if (!isSelected)
        {
            ApplyScale(normalScale);
            ApplyColor(normalColor);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!cardButton.interactable) return;

        // Toggle selection state
        isSelected = !isSelected;

        if (isSelected)
        {
            // Selected state takes priority
            ApplyScale(selectedScale);
            ApplyColor(selectedColor);
            PlaySFX(selectSFX);
        }
        else
        {
            // Deselected: return to hovered state if mouse is still on card, otherwise return to normal
            Vector3 targetScale = isHovered ? hoveredScale : normalScale;
            Color targetColor = isHovered ? highlightColor : normalColor;

            ApplyScale(targetScale);
            ApplyColor(targetColor);
            PlaySFX(deselectSFX);
        }
    }

    // --- SMOOTH INTERPOLATION HELPERS ---

    private void ApplyScale(Vector3 targetScale)
    {
        if (activeScaleCoroutine != null) StopCoroutine(activeScaleCoroutine);
        activeScaleCoroutine = StartCoroutine(Routine_LerpScale(targetScale));
    }

    private void ApplyColor(Color targetColor)
    {
        if (activeColorCoroutine != null) StopCoroutine(activeColorCoroutine);
        activeColorCoroutine = StartCoroutine(Routine_LerpColor(targetColor));
    }

    private IEnumerator Routine_LerpScale(Vector3 targetScale)
    {
        while (Vector3.Distance(transform.localScale, targetScale) > 0.001f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * transitionSpeed);
            yield return null;
        }
        transform.localScale = targetScale;
    }

    private IEnumerator Routine_LerpColor(Color targetColor)
    {
        while (Vector4.Distance(cardImage.color, targetColor) > 0.001f)
        {
            cardImage.color = Color.Lerp(cardImage.color, targetColor, Time.deltaTime * transitionSpeed);
            yield return null;
        }
        cardImage.color = targetColor;
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    // --- EXTERNAL STATE API (FOR NETWORK SYNC & ROUND RESETS) ---

    public void ForceResetCardState()
    {
        isSelected = false;
        isHovered = false;
        ApplyScale(normalScale);
        ApplyColor(normalColor);
    }

    public bool IsSelected => isSelected;
}