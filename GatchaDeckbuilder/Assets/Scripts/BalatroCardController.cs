using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro; // Added for TextMeshPro references

public enum CardCategory
{
    Attack,
    Defense
}

public enum CardRarity
{
    Common,
    Rare,
    Legendary
}

public class BalatroCardController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Card Category & Rarity")]
    [SerializeField] private CardCategory category = CardCategory.Attack;
    [SerializeField] private CardRarity rarity;

    [Header("Card Value Settings")]
    [Tooltip("The calculated numerical value of this card based on its rarity.")]
    [SerializeField] private int cardValue;

    [Header("UI References")]
    [Tooltip("The UI Image on your button that reflects the tier's color.")]
    [SerializeField] private Image tierImage;

    [Tooltip("TextMeshPro component displaying the card's numerical value.")]
    [SerializeField] private TextMeshProUGUI valueText;

    [Header("Rarity Tier Colors")]
    [SerializeField] private Color commonColor = new Color(0.6f, 0.6f, 0.6f, 1f);     // Gray/Silver
    [SerializeField] private Color rareColor = new Color(0.2f, 0.6f, 1f, 1f);       // Blue
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0f, 1f);     // Gold

    [Header("Hover Visual Settings")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.15f, 1.15f, 1f);
    [SerializeField] private float hoverLiftAmount = 30f; // Pixels lifted while hovering
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Image cardFrameOrOutline;

    [Header("Selection Visual Settings")]
    [Tooltip("Height above the resting hand position when locked in as selected")]
    [SerializeField] private float selectedLiftAmount = 60f;
    [SerializeField] private Vector3 selectedScale = new Vector3(1.15f, 1.15f, 1f);

    [Header("Balatro Tilt Settings")]
    [SerializeField] private float maxTiltAngle = 12f;

    [Header("Animation Tuning")]
    [SerializeField] private float lerpSpeed = 12f;

    [Header("Scale Settings")]
    [Tooltip("Base scale of the card while resting in hand (unselected)")]
    [SerializeField] private Vector3 restingScale = new Vector3(0.7f, 0.7f, 1f);

    // Internal State Tracking
    private bool isHovered = false;
    private bool isSelected = false;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 targetLocalPosition;
    private Quaternion targetLocalRotation;
    private Vector3 targetScale = Vector3.one;

    private Color originalColor = Color.white;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    public CardCategory Category => category;
    public CardRarity Rarity => rarity;
    public int CardValue => cardValue;
    public bool IsSelected => isSelected;
    public Vector3 RestingScale => restingScale;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (cardFrameOrOutline == null)
        {
            cardFrameOrOutline = GetComponent<Image>();
        }

        if (cardFrameOrOutline != null)
        {
            originalColor = cardFrameOrOutline.color;
        }

        // Initialize category, rarity, and numerical value upon spawn
        InitializeCardCategoryAndRarity();
    }

    /// <summary>
    /// Assigns card category, calculates probability-based rarity, and rolls numerical value:
    /// Common (50%): 1 to 5 | Rare (30%): 6 to 8 | Legendary (20%): 9 to 10
    /// </summary>
    public void InitializeCardCategoryAndRarity()
    {
        if (category == CardCategory.Attack)
        {
            float roll = Random.Range(0f, 100f);

            if (roll < 50f)
            {
                rarity = CardRarity.Common;
                cardValue = Random.Range(1, 6); // 1 to 5 inclusive
            }
            else if (roll < 80f) // 50% to 80% range (30% total)
            {
                rarity = CardRarity.Rare;
                cardValue = Random.Range(6, 9); // 6 to 8 inclusive
            }
            else // 80% to 100% range (20% total)
            {
                rarity = CardRarity.Legendary;
                cardValue = Random.Range(9, 11); // 9 to 10 inclusive
            }
        }

        ApplyRarityColor();
        UpdateValueTextUI();
    }

    private void ApplyRarityColor()
    {
        if (tierImage == null) return;

        switch (rarity)
        {
            case CardRarity.Common:
                tierImage.color = commonColor;
                break;
            case CardRarity.Rare:
                tierImage.color = rareColor;
                break;
            case CardRarity.Legendary:
                tierImage.color = legendaryColor;
                break;
        }
    }

    private void UpdateValueTextUI()
    {
        if (valueText != null)
        {
            valueText.text = cardValue.ToString();
        }
    }

    // Call this whenever the Hand Manager updates the fan layout positions
    public void SaveBaseTransform()
    {
        baseLocalPosition = rectTransform.localPosition;
        baseLocalRotation = rectTransform.localRotation;

        UpdateTargetVisuals();
    }

    private void Update()
    {
        // Dynamic Balatro tilt only occurs when hovering and NOT locked in selection
        if (isHovered && !isSelected)
        {
            CalculateCursorTilt();
        }

        // Interpolate position, rotation, and scale smoothly
        rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetLocalPosition, Time.deltaTime * lerpSpeed);
        rectTransform.localRotation = Quaternion.Slerp(rectTransform.localRotation, targetLocalRotation, Time.deltaTime * lerpSpeed);
        rectTransform.localScale = Vector3.Lerp(rectTransform.localScale, targetScale, Time.deltaTime * lerpSpeed);
    }

    private void CalculateCursorTilt()
    {
        Vector2 mousePos = Vector2.zero;

        if (Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }
        else if (Pointer.current != null)
        {
            mousePos = Pointer.current.position.ReadValue();
        }
        else
        {
            return;
        }

        Camera uiCamera = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? parentCanvas.worldCamera : null;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, mousePos, uiCamera, out Vector2 localMousePos))
        {
            float normalizedX = Mathf.Clamp(localMousePos.x / rectTransform.rect.width, -0.5f, 0.5f);
            float normalizedY = Mathf.Clamp(localMousePos.y / rectTransform.rect.height, -0.5f, 0.5f);

            float tiltX = -normalizedY * maxTiltAngle;
            float tiltY = normalizedX * maxTiltAngle;

            targetLocalRotation = baseLocalRotation * Quaternion.Euler(tiltX, tiltY, 0f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        UpdateTargetVisuals();

        if (!isSelected)
        {
            transform.SetAsLastSibling();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        UpdateTargetVisuals();
    }

    // Direct interface click handler
    public void OnPointerClick(PointerEventData eventData)
    {
        ToggleSelection();
    }

    // Exposed parameterless method for standard UI Button OnClick() events
    public void OnCardClicked()
    {
        ToggleSelection();
    }

    public void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateTargetVisuals();

        if (isSelected)
        {
            transform.SetAsLastSibling();
            Debug.Log($"[Card System] Selected: {gameObject.name} ({rarity} {category} - Value: {cardValue})");
        }
        else
        {
            Debug.Log($"[Card System] Deselected: {gameObject.name}");
        }

        // Notify End Turn system to update button visibility
        EndTurnManager endTurnMgr = FindObjectOfType<EndTurnManager>();
        if (endTurnMgr != null)
        {
            endTurnMgr.UpdateEndTurnButtonVisibility();
        }
    }

    // Central state machine that determines target transforms based on (isSelected, isHovered)
    private void UpdateTargetVisuals()
    {
        if (isSelected)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * selectedLiftAmount);
            targetLocalRotation = baseLocalRotation;
            targetScale = selectedScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = highlightColor;
            }
        }
        else if (isHovered)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * hoverLiftAmount);
            targetScale = hoverScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = highlightColor;
            }
        }
        else
        {
            // Resets back to resting scale instead of Vector3.one
            targetLocalPosition = baseLocalPosition;
            targetLocalRotation = baseLocalRotation;
            targetScale = restingScale;

            if (cardFrameOrOutline != null)
            {
                cardFrameOrOutline.color = originalColor;
            }
        }
    }
}