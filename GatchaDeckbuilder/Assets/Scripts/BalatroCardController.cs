using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum CardCategory { Attack, Defense, Support }

public class BalatroCardController : MonoBehaviour, IPointerClickHandler
{
    [Header("Card Category")]
    [SerializeField] private CardCategory category = CardCategory.Attack;
    public CardCategory Category => category;
    public void SetCategory(CardCategory newCategory) => category = newCategory;

    [Header("References")]
    [SerializeField] private Button cardButton;
    [SerializeField] private Image cardImage;

    [Header("Combat Highlight Colors")]
    [SerializeField] private Color attackHighlightColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color defenseHighlightColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private Color supportHighlightColor = new Color(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color defaultColor = Color.white;

    public bool IsSelected { get; private set; } = false;
    public bool IsInteractable => isInteractable; // Added for debugging

    private bool isInteractable = true;
    private RectTransform rectTransform;
    private EndTurnManager endTurnManager;

    public int CardId { get; private set; }
    public bool IsForever { get; private set; }

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
    public void SetCardId(int id) => CardId = id;
    public void SetCardMetadata(bool isForever) => IsForever = isForever;

    public void SetInteractable(bool state)
    {
        isInteractable = state;
        if (cardButton != null) cardButton.interactable = state;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isInteractable)
        {
            Debug.Log($"[BalatroCardController] Click ignored on '{gameObject.name}' because isInteractable is false.");
            return;
        }
        ToggleSelection();
    }

    public void ToggleSelection()
    {
        if (!isInteractable) return;

        IsSelected = !IsSelected;
        Debug.Log($"[BalatroCardController] Toggled IsSelected to {IsSelected} on '{gameObject.name}'");

        UpdateSelectionVisuals();

        if (endTurnManager != null)
        {
            endTurnManager.UpdateEndTurnButtonVisibility();
        }
    }

    // ✅ NEW: Bypasses the isInteractable check for programmatic cleanup
    public void ForceDeselect()
    {
        if (!IsSelected) return;

        IsSelected = false;
        UpdateSelectionVisuals();

        if (endTurnManager != null)
        {
            endTurnManager.UpdateEndTurnButtonVisibility();
        }
    }

    // ✅ NEW: Helper to keep the visual logic DRY (Don't Repeat Yourself)
    private void UpdateSelectionVisuals()
    {
        if (rectTransform != null)
        {
            Vector2 currentPos = rectTransform.anchoredPosition;
            float targetY = IsSelected ? currentPos.y + 50f : currentPos.y - 50f;
            rectTransform.anchoredPosition = new Vector2(currentPos.x, targetY);
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