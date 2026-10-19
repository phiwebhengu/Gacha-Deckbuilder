using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(Image))]
public class CardJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale Parameters")]
    [SerializeField] private Vector3 normalScale = Vector3.one;
    [SerializeField] private Vector3 hoveredScale = new Vector3(1.08f, 1.08f, 1f);
    [Tooltip("Size of a SELECTED card (the 'pop'). 1.35 = 35% bigger than normal.")]
    [SerializeField] private Vector3 selectedPopScale = new Vector3(1.35f, 1.35f, 1f);
    [SerializeField] private float transitionSpeed = 15f;

    [Header("Draw Order")]
    [Tooltip("Hovered and selected cards are drawn in front of the rest of the hand.")]
    [SerializeField] private bool bringToFront = true;

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

    // While true, combat highlight colors own the card's color and hover/select tints are ignored.
    private bool hasColorOverride = false;

    private Coroutine activeScaleCoroutine;
    private Coroutine activeColorCoroutine;

    // Frame-rate independent smoothing factor. (Time.deltaTime * speed overshoots / snaps on lag spikes.)
    private float Blend => 1f - Mathf.Exp(-transitionSpeed * Time.unscaledDeltaTime);

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
        if (cardImage != null) cardImage.color = normalColor;
    }

    // --- EVENT SYSTEM INTERFACES ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (cardButton != null && !cardButton.interactable) return;

        isHovered = true;

        if (!isSelected)
        {
            ApplyScale(hoveredScale);
            ApplyColor(highlightColor);
            PlaySFX(hoverSFX);
        }
        UpdateDrawOrder();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (cardButton != null && !cardButton.interactable) return;

        isHovered = false;

        if (!isSelected)
        {
            ApplyScale(normalScale);
            ApplyColor(normalColor);
        }
        UpdateDrawOrder();
    }

    // --- SELECTION CONTROL FROM CONTROLLER ---

    public void SetSelectedState(bool selected, bool hovered)
    {
        isHovered = hovered;
        isSelected = selected;

        if (isSelected)
        {
            ApplyScale(selectedPopScale);
            ApplyColor(selectedColor);
            PlaySFX(selectSFX);
        }
        else
        {
            ApplyScale(isHovered ? hoveredScale : normalScale);
            ApplyColor(StateColor());
            PlaySFX(deselectSFX);
        }
        UpdateDrawOrder();
    }

    // --- DRAW ORDER (UI draws later siblings on top) ---

    private Transform raisedParent;
    private int originalSiblingIndex;
    private bool isRaised;

    /// <summary>Selected or hovered cards go in front of the hand; everyone else returns to their slot.</summary>
    private void UpdateDrawOrder()
    {
        if (!bringToFront) return;

        bool shouldBeOnTop = isSelected || isHovered;

        if (shouldBeOnTop && !isRaised)
        {
            raisedParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();
            transform.SetAsLastSibling();
            isRaised = true;
        }
        else if (shouldBeOnTop && isRaised)
        {
            // Already raised, but another card may have been raised after us: stay on top.
            transform.SetAsLastSibling();
        }
        else if (!shouldBeOnTop && isRaised)
        {
            RestoreDrawOrder();
        }
    }

    private void RestoreDrawOrder()
    {
        if (isRaised && raisedParent != null && transform.parent == raisedParent)
        {
            int idx = Mathf.Clamp(originalSiblingIndex, 0, raisedParent.childCount - 1);
            transform.SetSiblingIndex(idx);
        }
        isRaised = false;
        raisedParent = null;
    }

    // --- COMBAT HIGHLIGHT OVERRIDE (used by BalatroCardController) ---

    /// <summary>Takes over the card color (e.g. attack/defense highlight) without fighting the hover/select lerps.</summary>
    public void SetColorOverride(Color color)
    {
        hasColorOverride = true;
        if (activeColorCoroutine != null)
        {
            StopCoroutine(activeColorCoroutine);
            activeColorCoroutine = null;
        }
        if (cardImage != null) cardImage.color = color;
    }

    /// <summary>Gives the color back to the normal hover/selected logic.</summary>
    public void ClearColorOverride()
    {
        if (!hasColorOverride) return;
        hasColorOverride = false;
        ApplyColor(StateColor());
    }

    private Color StateColor()
    {
        if (isSelected) return selectedColor;
        return isHovered ? highlightColor : normalColor;
    }

    // --- SMOOTH INTERPOLATION HELPERS ---

    private void ApplyScale(Vector3 targetScale)
    {
        if (activeScaleCoroutine != null) StopCoroutine(activeScaleCoroutine);

        if (!isActiveAndEnabled)
        {
            transform.localScale = targetScale;
            return;
        }
        activeScaleCoroutine = StartCoroutine(Routine_LerpScale(targetScale));
    }

    private void ApplyColor(Color targetColor)
    {
        if (hasColorOverride) return; // combat highlight owns the color right now

        if (activeColorCoroutine != null) StopCoroutine(activeColorCoroutine);

        if (!isActiveAndEnabled)
        {
            if (cardImage != null) cardImage.color = targetColor;
            return;
        }
        activeColorCoroutine = StartCoroutine(Routine_LerpColor(targetColor));
    }

    private IEnumerator Routine_LerpScale(Vector3 targetScale)
    {
        while (Vector3.Distance(transform.localScale, targetScale) > 0.001f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Blend);
            yield return null;
        }
        transform.localScale = targetScale;
        activeScaleCoroutine = null;
    }

    private IEnumerator Routine_LerpColor(Color targetColor)
    {
        while (cardImage != null && !hasColorOverride && Vector4.Distance(cardImage.color, targetColor) > 0.001f)
        {
            cardImage.color = Color.Lerp(cardImage.color, targetColor, Blend);
            yield return null;
        }
        if (cardImage != null && !hasColorOverride) cardImage.color = targetColor;
        activeColorCoroutine = null;
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    // --- EXTERNAL STATE API ---

    public void ForceResetCardState()
    {
        isSelected = false;
        isHovered = false;
        hasColorOverride = false;
        RestoreDrawOrder();

        if (activeScaleCoroutine != null) StopCoroutine(activeScaleCoroutine);
        if (activeColorCoroutine != null) StopCoroutine(activeColorCoroutine);
        activeScaleCoroutine = null;
        activeColorCoroutine = null;

        transform.localScale = normalScale;
        if (cardImage != null) cardImage.color = normalColor;
    }

    public bool IsSelected => isSelected;
}