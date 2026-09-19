using System.Collections;
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
    [SerializeField] private CardRarity rarity = CardRarity.Common;

    [Header("Card Value Settings")]
    [SerializeField] private int cardValue;

    [Header("UI References")]
    [SerializeField] private Image tierImage;
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private TextMeshProUGUI categoryText;

    [Header("Rarity Tier Colors")]
    [SerializeField] private Color commonColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [SerializeField] private Color rareColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0f, 1f);

    [Header("Hover Visual Settings")]
    [SerializeField] private Vector3 hoverScale = new Vector3(1.15f, 1.15f, 1f);
    [SerializeField] private float hoverLiftAmount = 30f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Image cardFrameOrOutline;

    [Header("Combat Highlight Colors")]
    [SerializeField] private Color combatHighlightColor = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color combatDimColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    [Header("Selection Visual Settings")]
    [SerializeField] private float selectedLiftAmount = 60f;
    [SerializeField] private Vector3 selectedScale = new Vector3(1.15f, 1.15f, 1f);

    [Header("Balatro Tilt Settings")]
    [SerializeField] private float maxTiltAngle = 12f;

    [Header("Animation Tuning")]
    [SerializeField] private float lerpSpeed = 12f;

    [Header("Scale Settings")]
    [SerializeField] private Vector3 restingScale = new Vector3(0.7f, 0.7f, 1f);

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
    private CanvasGroup canvasGroup;

    public CardCategory Category => category;
    public CardRarity Rarity => rarity;
    public int CardValue => cardValue;
    public bool IsSelected => isSelected;
    public Vector3 RestingScale => restingScale;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (cardFrameOrOutline == null)
        {
            cardFrameOrOutline = GetComponent<Image>();
        }

        if (cardFrameOrOutline != null)
        {
            originalColor = cardFrameOrOutline.color;
        }
    }

    public void ApplyPulledCardData(string cardName, string role, string tier, int value)
    {
        category = role switch
        {
            "Attack" => CardCategory.Attack,
            "Defense" => CardCategory.Defense,
            _ => CardCategory.Support
        };

        rarity = tier switch
        {
            "Legendary" => CardRarity.Legendary,
            "Rare" => CardRarity.Rare,
            _ => CardRarity.Common
        };

        cardValue = value;

        ApplyRarityColor();
        UpdateValueTextUI();
        UpdateCategoryTextUI();
    }

    public void InitializeCardCategoryAndRarity()
    {
        float roll = Random.Range(0f, 100f);
        if (roll < 50f)
        {
            rarity = CardRarity.Common;
            cardValue = Random.Range(1, 6);
        }
        else if (roll < 80f)
        {
            rarity = CardRarity.Rare;
            cardValue = Random.Range(6, 9);
        }
        else
        {
            rarity = CardRarity.Legendary;
            cardValue = Random.Range(9, 11);
        }

        ApplyRarityColor();
        UpdateValueTextUI();
        UpdateCategoryTextUI();
    }

    public void ApplyRarityColor()
    {
        if (tierImage == null) return;

        tierImage.color = rarity switch
        {
            CardRarity.Rare => rareColor,
            CardRarity.Legendary => legendaryColor,
            _ => commonColor
        };
    }

    public void UpdateValueTextUI()
    {
        if (valueText != null) valueText.text = cardValue.ToString();
    }

    public void UpdateCategoryTextUI()
    {
        if (categoryText != null) categoryText.text = category.ToString();
    }

    /// <summary>
    /// Highlights cards matching the active evaluation category and dims non-matching cards during resolution.
    /// </summary>
    public void HighlightCardForCategory(CardCategory activeCategory)
    {
        if (category == activeCategory)
        {
            if (cardFrameOrOutline != null) cardFrameOrOutline.color = combatHighlightColor;
            targetScale = selectedScale;
        }
        else
        {
            if (cardFrameOrOutline != null) cardFrameOrOutline.color = combatDimColor;
            targetScale = restingScale;
        }
    }

    /// <summary>
    /// Restores card visual state after combat category evaluation.
    /// </summary>
    public void ResetCombatHighlight()
    {
        UpdateTargetVisuals();
    }

    public void PrepareForUnrevealedSpawn()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        transform.localScale = Vector3.zero;
    }

    public IEnumerator Routine_AnimateCenterReveal(Vector3 centerTargetPos, float moveDuration, float shrinkDuration, float overshootDuration, float returnDuration)
    {
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        Vector3 startPos = rectTransform.position;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            rectTransform.position = Vector3.Lerp(startPos, centerTargetPos, t);
            rectTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 1.4f, t);
            yield return null;
        }

        rectTransform.position = centerTargetPos;

        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.4f, Vector3.one * 1.1f, t);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < overshootDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / overshootDuration;
            rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.1f, Vector3.one * 1.5f, t);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.5f, Vector3.one, t);
            yield return null;
        }

        rectTransform.localScale = Vector3.one;
        SaveBaseTransform();
    }

    public void SaveBaseTransform()
    {
        baseLocalPosition = rectTransform.localPosition;
        baseLocalRotation = rectTransform.localRotation;
        UpdateTargetVisuals();
    }

    private void Update()
    {
        if (isHovered && !isSelected)
        {
            CalculateCursorTilt();
        }

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
        if (!isSelected) transform.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        UpdateTargetVisuals();
    }

    public void OnPointerClick(PointerEventData eventData) => ToggleSelection();

    public void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateTargetVisuals();
        if (isSelected) transform.SetAsLastSibling();

        EndTurnManager endTurnMgr = FindFirstObjectByType<EndTurnManager>();
        if (endTurnMgr != null)
        {
            endTurnMgr.UpdateEndTurnButtonVisibility();
        }
    }

    private void UpdateTargetVisuals()
    {
        if (isSelected)
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
}