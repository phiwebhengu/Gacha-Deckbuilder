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

    [Header("Combat Highlight Colors")]
    [SerializeField] private Color attackHighlightColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color defenseHighlightColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private Color supportHighlightColor = new Color(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);

    public bool IsSelected { get; private set; } = false;
    private bool isInteractable = true;
    private RectTransform rectTransform;
    private EndTurnManager endTurnManager;

    public int CardId { get; private set; }
    public bool IsForever { get; private set; }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (cardButton == null) cardButton = GetComponent<Button>();

        // Fallback if not set via PlayerHandManager
        if (endTurnManager == null)
        {
            endTurnManager = FindFirstObjectByType<EndTurnManager>();
        }
    }

    public void SetEndTurnManager(EndTurnManager manager)
    {
        endTurnManager = manager;
    }

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
        Debug.Log($"[BalatroCardController] IsSelected is now {IsSelected} on card '{gameObject.name}'");

        if (rectTransform != null)
        {
            Vector2 currentPos = rectTransform.anchoredPosition;
            // Toggle Y position by 50f
            float targetY = IsSelected ? currentPos.y + 50f : currentPos.y - 50f;
            rectTransform.anchoredPosition = new Vector2(currentPos.x, targetY);
        }

        if (endTurnManager != null)
        {
            endTurnManager.UpdateEndTurnButtonVisibility();
        }
        else
        {
            Debug.LogWarning($"[BalatroCardController] endTurnManager is null on card '{gameObject.name}'! Button visibility cannot be updated.");
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

        // Note: You may want to apply this color to an Image component here, e.g.:
        // if (cardImage != null) cardImage.color = targetHighlight;
    }
}