using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    [SerializeField] private CardJuice cardJuice;

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

    public event Action<BalatroCardController> SelectionChanged;

    public RectTransform Rect
    {
        get
        {
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            return rectTransform;
        }
    }

    // One lookup shared by every card instead of a FindFirstObjectByType in every card's Awake.
    private static EndTurnManager sharedEndTurnManager;

    private EndTurnManager EndTurn
    {
        get
        {
            if (endTurnManager == null)
            {
                if (sharedEndTurnManager == null) sharedEndTurnManager = FindFirstObjectByType<EndTurnManager>();
                endTurnManager = sharedEndTurnManager;
            }
            return endTurnManager;
        }
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (cardButton == null) cardButton = GetComponent<Button>();
        if (cardImage == null) cardImage = GetComponent<Image>();
        if (cardJuice == null) cardJuice = GetComponent<CardJuice>();
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
        if (cardImage != null) cardImage.raycastTarget = state;
    }

    /// <summary>Stops the card from receiving hover/click events while it is flying across the screen.</summary>
    public void LockForFlight()
    {
        if (cardImage != null) cardImage.raycastTarget = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isHovered = true;

        if (!IsSelected)
        {
            if (cardImage != null && cardJuice == null) cardImage.color = hoverColor;
            if (rectTransform != null && cardJuice == null) rectTransform.localScale = Vector3.one * hoverScale;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isHovered = false;

        if (!IsSelected)
        {
            ResetHighlight();
            if (rectTransform != null && cardJuice == null) rectTransform.localScale = Vector3.one;
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
        SetSelected(!IsSelected);
    }

    public void ForceDeselect()
    {
        if (!IsSelected) return;
        SetSelected(false);
    }

    private void SetSelected(bool selected)
    {
        IsSelected = selected;

        if (cardJuice != null)
        {
            cardJuice.SetSelectedState(IsSelected, isHovered);
        }

        // Hand manager re-fans the hand (including this card's lift).
        SelectionChanged?.Invoke(this);

        if (EndTurn != null)
        {
            EndTurn.UpdateEndTurnButtonVisibility();
        }
    }

    /// <summary>
    /// Resets all visual states (scale, hover state, selection state, and colors) back to hand defaults.
    /// </summary>
    public void ResetCardState()
    {
        IsSelected = false;
        isHovered = false;

        if (cardJuice != null)
        {
            cardJuice.ForceResetCardState();
        }
        else
        {
            ResetHighlight();
            if (rectTransform != null) rectTransform.localScale = Vector3.one;
        }

        if (cardImage != null)
        {
            cardImage.raycastTarget = true;
        }

        if (EndTurn != null)
        {
            EndTurn.UpdateEndTurnButtonVisibility();
        }
    }

    /// <summary>
    /// Neutral look (not selected / not hovered / no highlight) WITHOUT touching interactability.
    /// Used when a card is locked in for the turn so it looks the same as the opponent's revealed cards.
    /// </summary>
    public void ResetVisualsOnly()
    {
        IsSelected = false;
        isHovered = false;

        if (cardJuice != null)
        {
            cardJuice.ForceResetCardState();
        }
        else
        {
            ResetHighlight();
            if (rectTransform != null) rectTransform.localScale = Vector3.one;
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

        if (cardJuice != null) cardJuice.SetColorOverride(targetHighlight);
        else if (cardImage != null) cardImage.color = targetHighlight;
    }

    public void ResetHighlight()
    {
        if (cardJuice != null) cardJuice.ClearColorOverride();
        else if (cardImage != null) cardImage.color = defaultColor;
    }
}