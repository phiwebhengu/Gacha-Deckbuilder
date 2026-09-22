using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

public enum CardCategory
{
    Attack,
    Defense,
    Support
}

public class BalatroCardController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Card Category (Set by HandManager or GachaUI)")]
    [SerializeField] private CardCategory category = CardCategory.Attack;
    public CardCategory Category => category;

    // Allow external scripts to set the category after instantiation
    public void SetCategory(CardCategory newCategory) => category = newCategory;

    [Header("Hover Visual Settings")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.15f, 1.15f, 1f);
    [SerializeField] private float hoverLiftAmount = 30f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Image cardFrameOrOutline;

    [Header("Selection Visual Settings")]
    [SerializeField] private float selectedLiftAmount = 60f;
    [SerializeField] private Vector3 selectedScale = new Vector3(1.15f, 1.15f, 1f);

    [Header("Balatro Tilt Settings")]
    [SerializeField] private float maxTiltAngle = 12f;

    [Header("Animation Tuning")]
    [SerializeField] private float lerpSpeed = 12f;

    [Header("Scale Settings")]
    [SerializeField] private Vector3 restingScale = new Vector3(0.7f, 0.7f, 1f);
    public Vector3 RestingScale => restingScale;

    [Header("Combat Highlight Colors")]
    [SerializeField] private Color attackHighlightColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color defenseHighlightColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private Color supportHighlightColor = new Color(0.3f, 0.9f, 0.4f, 1f);

    private bool isHovered = false;
    public bool IsSelected { get; private set; } = false;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 targetLocalPosition;
    private Quaternion targetLocalRotation;
    private Vector3 targetScale = Vector3.one;

    private Color originalColor = Color.white;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    public int CardId { get; private set; }

    public void SetCardId(int id)
    {
        CardId = id;
    }
    public bool IsForever { get; private set; }

    public void SetCardMetadata(bool isForever)
    {
        IsForever = isForever;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (cardFrameOrOutline == null)
            cardFrameOrOutline = GetComponent<Image>();

        if (cardFrameOrOutline != null)
            originalColor = cardFrameOrOutline.color;
    }

    public void SaveBaseTransform()
    {
        baseLocalPosition = rectTransform.localPosition;
        baseLocalRotation = rectTransform.localRotation;
        UpdateTargetVisuals();
    }

    private void Update()
    {
        if (isHovered && !IsSelected)
            CalculateCursorTilt();

        rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetLocalPosition, Time.deltaTime * lerpSpeed);
        rectTransform.localRotation = Quaternion.Slerp(rectTransform.localRotation, targetLocalRotation, Time.deltaTime * lerpSpeed);
        rectTransform.localScale = Vector3.Lerp(rectTransform.localScale, targetScale, Time.deltaTime * lerpSpeed);
    }

    private void CalculateCursorTilt()
    {
        Vector2 mousePos = Vector2.zero;
        if (Mouse.current != null) mousePos = Mouse.current.position.ReadValue();
        else if (Pointer.current != null) mousePos = Pointer.current.position.ReadValue();
        else return;

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
        if (!IsSelected) transform.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        UpdateTargetVisuals();
    }

    public void OnPointerClick(PointerEventData eventData) => ToggleSelection();
    public void OnCardClicked() => ToggleSelection();

    public void ToggleSelection()
    {
        IsSelected = !IsSelected;
        UpdateTargetVisuals();

        var endTurnMgr = FindFirstObjectByType<EndTurnManager>();
        endTurnMgr?.UpdateEndTurnButtonVisibility();
    }

    private void UpdateTargetVisuals()
    {
        if (IsSelected)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * selectedLiftAmount);
            targetLocalRotation = baseLocalRotation;
            targetScale = selectedScale;
            if (cardFrameOrOutline != null) cardFrameOrOutline.color = highlightColor;
        }
        else if (isHovered)
        {
            targetLocalPosition = baseLocalPosition + (transform.up * hoverLiftAmount);
            targetScale = hoverScale;
            if (cardFrameOrOutline != null) cardFrameOrOutline.color = highlightColor;
        }
        else
        {
            targetLocalPosition = baseLocalPosition;
            targetLocalRotation = baseLocalRotation;
            targetScale = restingScale;
            if (cardFrameOrOutline != null) cardFrameOrOutline.color = originalColor;
        }
    }

    public void HighlightCardForCategory(CardCategory targetCategory, float scaleMultiplier = 1.25f)
    {
        if (category != targetCategory) return;

        targetScale = restingScale * scaleMultiplier;
        Color targetHighlight = category switch
        {
            CardCategory.Attack => attackHighlightColor,
            CardCategory.Defense => defenseHighlightColor,
            CardCategory.Support => supportHighlightColor,
            _ => highlightColor
        };

        if (cardFrameOrOutline != null)
            cardFrameOrOutline.color = targetHighlight;
    }

    public void ResetCombatHighlight()
    {
        targetScale = restingScale;
        if (cardFrameOrOutline != null)
            cardFrameOrOutline.color = originalColor;
    }
}