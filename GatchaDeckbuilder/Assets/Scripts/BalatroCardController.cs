using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum CardCategory { Attack, Defense, Support }

public class BalatroCardController : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Card Category")]
    [SerializeField] private CardCategory category = CardCategory.Attack;
    public CardCategory Category => category;
    public void SetCategory(CardCategory newCategory) => category = newCategory;

    [Header("References")]
    [SerializeField] private Button cardButton;
    [SerializeField] private Image cardImage;

    [Header("Hover Settings")]
    [SerializeField] private float hoverScale = 1.15f;
    [SerializeField] private Color hoverColor = new Color(1f, 0.95f, 0.8f, 1f);

    [Header("Combat Highlight Colors")]
    [SerializeField] private Color attackHighlightColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color defenseHighlightColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private Color supportHighlightColor = new Color(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Selection Offset")]
    [SerializeField] private float selectYOffset = 40f;
    public float SelectYOffset => selectYOffset;

    public bool IsSelected { get; private set; } = false;
    public bool IsInteractable => isInteractable;

    private bool isInteractable = true;
    private bool isHovered = false;
    private RectTransform rectTransform;
    private EndTurnManager endTurnManager;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (cardButton == null) cardButton = GetComponent<Button>();
        if (cardImage == null) cardImage = GetComponent<Image>();

        if (endTurnManager == null)
        {
            endTurnManager = FindFirstObjectByType<EndTurnManager>();
        }
    }

    public void SetEndTurnManager(EndTurnManager manager) => endTurnManager = manager;
    public int CardId { get; private set; }
    public void SetCardId(int id) => CardId = id;
    public bool IsForever { get; private set; }
    public void SetCardMetadata(bool isForever) => IsForever = isForever;

    public void SetInteractable(bool state)
    {
        isInteractable = state;
        if (cardButton != null) cardButton.interactable = state;

        // Ensure raycast target is active so EventSystem registers hover
        if (cardImage != null) cardImage.raycastTarget = state;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isHovered = true;

        // Highlight & scale feedback on hover
        if (!IsSelected)
        {
            if (cardImage != null) cardImage.color = hoverColor;
            if (rectTransform != null) rectTransform.localScale = Vector3.one * hoverScale;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isHovered = false;

        // Revert to normal state when mouse leaves
        if (!IsSelected)
        {
            ResetHighlight();
            if (rectTransform != null) rectTransform.localScale = Vector3.one;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isInteractable) return;
        ToggleSelection();
    }

    public void ToggleSelection()
    {
        if (!isInteractable) return;

        IsSelected = !IsSelected;
        UpdateSelectionVisuals(IsSelected);

        if (endTurnManager != null)
        {
            endTurnManager.UpdateEndTurnButtonVisibility();
        }
    }

    public void ForceDeselect()
    {
        if (!IsSelected) return;

        IsSelected = false;
        UpdateSelectionVisuals(false);

        if (endTurnManager != null)
        {
            endTurnManager.UpdateEndTurnButtonVisibility();
        }
    }

    /// <summary>
    /// Resets all visual states (scale, hover state, selection state, and colors) back to hand defaults.
    /// </summary>
    public void ResetCardState()
    {
        IsSelected = false;
        isHovered = false;
        ResetHighlight();

        if (rectTransform != null)
        {
            rectTransform.localScale = Vector3.one;
        }

        if (cardImage != null)
        {
            cardImage.raycastTarget = true;
        }

        if (endTurnManager != null)
        {
            endTurnManager.UpdateEndTurnButtonVisibility();
        }
    }

    private void UpdateSelectionVisuals(bool selected)
    {
        if (rectTransform != null)
        {
            Vector2 currentPos = rectTransform.anchoredPosition;
            float shift = selected ? selectYOffset : -selectYOffset;
            rectTransform.anchoredPosition = new Vector2(currentPos.x, currentPos.y + shift);
        }
    }

    public void HighlightCardForCategory(CardCategory targetCategory)
    {
        if (category != targetCategory) return;

        Color targetHighlight = category switch
        {
            CardCategory.Attack => attackHighlightColor,
            CardCategory.Defense => defenseHighlightColor,
            CardCategory.Support => supportHighlightColor,
            _ => highlightColor
        };

        if (cardImage != null) cardImage.color = targetHighlight;
    }

    public void ResetHighlight()
    {
        if (cardImage != null) cardImage.color = defaultColor;
    }
}