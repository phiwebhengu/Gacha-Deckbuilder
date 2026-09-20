using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using GachaSystem;

public enum CardCategory
{
    Attack,
    Defense,
    Support
}

public class BalatroCardController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Gacha & Support Config")]
    [Tooltip("ScriptableObject determining rarity odds and rolling logic.")]
    [SerializeField] private PullConfig pullConfig;

    [Tooltip("Reference to the database containing support card data parsed from CSV.")]
    [SerializeField] private SupportDeckDatabase supportDatabase;

    [Header("Card Category & Rarity")]
    [SerializeField] private CardCategory category = CardCategory.Attack;
    [SerializeField] private Rarity rarity = Rarity.Common;

    [Header("Card Value & Support Data Settings")]
    [Tooltip("The calculated numerical value of this card based on its rarity.")]
    [SerializeField] private int cardValue;

    // Fully qualified namespace reference prevents ambiguous class collisions
    private GachaSystem.SupportCardData currentSupportData;
    public GachaSystem.SupportCardData CurrentSupportData => currentSupportData;

    [Header("UI References")]
    [Tooltip("The UI Image on your button that reflects the tier's color.")]
    [SerializeField] private Image tierImage;

    [Tooltip("TextMeshPro component displaying the card's numerical value (for Attack/Defense).")]
    [SerializeField] private TextMeshProUGUI valueText;

    [Tooltip("TextMeshPro component displaying card category (ATTACK, DEFENSE, or SUPPORT).")]
    [SerializeField] private TextMeshProUGUI categoryText;

    [Tooltip("TextMeshPro component displaying card title/name (especially useful for Support cards).")]
    [SerializeField] private TextMeshProUGUI nameText;

    [Tooltip("TextMeshPro component displaying the support effect text description.")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Rarity Flash Overlays")]
    [Tooltip("Overlay UI Image for Common flash (No Raycast Target)")]
    [SerializeField] private Image commonFlashOverlay;

    [Tooltip("Overlay UI Image for Rare flash (No Raycast Target)")]
    [SerializeField] private Image rareFlashOverlay;

    [Tooltip("Overlay UI Image for Legendary flash (No Raycast Target)")]
    [SerializeField] private Image legendaryFlashOverlay;

    [Tooltip("Peak opacity for the flash (0.0 to 1.0)")]
    [Range(0f, 1f)]
    [SerializeField] private float maxFlashAlpha = 0.6f;

    [Tooltip("Duration in seconds of the flash fade in and fade out")]
    [SerializeField] private float flashDuration = 0.2f;

    [Header("Rarity Tier Colors")]
    [SerializeField] private Color commonColor = new Color(0.6f, 0.6f, 0.6f, 1f);     // Gray/Silver
    [SerializeField] private Color rareColor = new Color(0.2f, 0.6f, 1f, 1f);        // Blue
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0f, 1f);     // Gold

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
    [Tooltip("Base scale of the card while resting in hand (unselected)")]
    [SerializeField] private Vector3 restingScale = new Vector3(0.7f, 0.7f, 1f);

    [Header("Reveal Juiciness Settings")]
    [SerializeField] private Vector3 closeUpScale = new Vector3(2.0f, 2.0f, 1f);
    [SerializeField] private Vector3 shrinkScale = new Vector3(1.6f, 1.6f, 1f);
    [SerializeField] private Vector3 overshootScale = new Vector3(2.3f, 2.3f, 1f);

    [Header("Screen Shake Settings")]
    [SerializeField] private float commonShakeIntensity = 3f;
    [SerializeField] private float rareShakeIntensity = 8f;
    [SerializeField] private float legendaryShakeIntensity = 18f;
    [SerializeField] private float shakeDuration = 0.25f;

    [Header("Combat Highlight Colors")]
    [SerializeField] private Color attackHighlightColor = new Color(1f, 0.3f, 0.3f, 1f);  // Red Glow
    [SerializeField] private Color defenseHighlightColor = new Color(0.3f, 0.6f, 1f, 1f); // Blue Glow
    [SerializeField] private Color supportHighlightColor = new Color(0.3f, 0.9f, 0.4f, 1f); // Green Glow

    public GachaSystem.SupportCardData SupportData => currentSupportData;

    // Internal State Tracking
    private bool isHovered = false;
    private bool isSelected = false;
    private bool isRevealing = false;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 targetLocalPosition;
    private Quaternion targetLocalRotation;
    private Vector3 targetScale = Vector3.one;

    private Color originalColor = Color.white;
    private RectTransform rectTransform;
    private Canvas parentCanvas;

    public CardCategory Category => category;
    public Rarity Rarity => rarity;
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

        ResetFlashOverlays();
    }

    private void ResetFlashOverlays()
    {
        SetOverlayAlpha(commonFlashOverlay, 0f);
        SetOverlayAlpha(rareFlashOverlay, 0f);
        SetOverlayAlpha(legendaryFlashOverlay, 0f);
    }

    private void SetOverlayAlpha(Image overlay, float alpha)
    {
        if (overlay != null)
        {
            Color color = overlay.color;
            color.a = alpha;
            overlay.color = color;
        }
    }

    /// <summary>
    /// Initializes standard Attack or Defense card rolling logic.
    /// </summary>
    public void InitializeCardCategoryAndRarity(PityManager pityManager = null, System.Random networkRng = null, PullConfig configOverride = null)
    {
        PullConfig activeConfig = configOverride != null ? configOverride : pullConfig;
        System.Random rng = networkRng ?? new System.Random();

        // Force category to roll ONLY between Attack and Defense (50/50)
        category = (rng.NextDouble() < 0.5) ? CardCategory.Attack : CardCategory.Defense;

        // Roll Rarity
        bool forceLegendary = (pityManager != null && pityManager.ShouldForceLegendary());
        if (forceLegendary)
        {
            rarity = Rarity.Legendary;
        }
        else if (activeConfig != null)
        {
            rarity = activeConfig.RollRarity(rng);
        }
        else
        {
            rarity = Rarity.Common;
        }

        if (pityManager != null) pityManager.RegisterPull(rarity);

        // Roll Value
        switch (rarity)
        {
            case Rarity.Common: cardValue = rng.Next(1, 6); break;
            case Rarity.Rare: cardValue = rng.Next(6, 9); break;
            case Rarity.Legendary: cardValue = rng.Next(9, 11); break;
        }

        currentSupportData = null;
        ApplyRarityColor();
        UpdateValueTextUI();
        UpdateCategoryTextUI();
        UpdateSupportTextUI();
    }

    /// <summary>
    /// Specifically initializes card as a Support card, selecting a random entry from SupportDeckDatabase matching the rolled rarity.
    /// </summary>
    public void InitializeAsSupportCard(SupportDeckDatabase databaseOverride = null, PityManager pityManager = null, System.Random networkRng = null, PullConfig configOverride = null)
    {
        category = CardCategory.Support;

        SupportDeckDatabase db = databaseOverride != null ? databaseOverride : supportDatabase;
        PullConfig activeConfig = configOverride != null ? configOverride : pullConfig;
        System.Random rng = networkRng ?? new System.Random();

        // Roll Rarity
        bool forceLegendary = (pityManager != null && pityManager.ShouldForceLegendary());
        if (forceLegendary)
        {
            rarity = Rarity.Legendary;
        }
        else if (activeConfig != null)
        {
            rarity = activeConfig.RollRarity(rng);
        }
        else
        {
            rarity = Rarity.Common;
        }

        if (pityManager != null)
        {
            pityManager.RegisterPull(rarity);
        }

        // Pull random support card from CSV entries matching rolled rarity
        if (db != null)
        {
            currentSupportData = db.GetRandomCardByRarity(rarity, rng);

            // Fallback if rarity tier had no matches
            if (currentSupportData == null && db.SupportCards.Count > 0)
            {
                currentSupportData = db.SupportCards[rng.Next(0, db.SupportCards.Count)];
            }
        }
        else
        {
            Debug.LogError($"[BalatroCardController] SupportDeckDatabase reference is missing on {gameObject.name}!");
        }

        cardValue = 0; // Support cards derive utility from effects rather than base numerical value

        ApplyRarityColor();
        UpdateValueTextUI();
        UpdateCategoryTextUI();
        UpdateSupportTextUI();
    }

    public void PrepareForUnrevealedSpawn()
    {
        isRevealing = true;
        if (valueText != null) valueText.gameObject.SetActive(false);
        if (categoryText != null) categoryText.gameObject.SetActive(false);
        if (nameText != null) nameText.gameObject.SetActive(false);
        if (descriptionText != null) descriptionText.gameObject.SetActive(false);
        if (tierImage != null) tierImage.gameObject.SetActive(false);
    }

    public IEnumerator Routine_AnimateCenterReveal(
        Vector3 centerWorldPos,
        float moveDuration,
        float shrinkDuration,
        float overshootDuration,
        float returnDuration)
    {
        isRevealing = true;
        transform.rotation = Quaternion.identity;

        // 1. Move to screen center while scaling up
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            transform.position = Vector3.Lerp(startPos, centerWorldPos, t);
            transform.localScale = Vector3.Lerp(startScale, closeUpScale, t);
            yield return null;
        }
        transform.position = centerWorldPos;

        // 2. Quick Reduction in scale
        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            transform.localScale = Vector3.Lerp(closeUpScale, shrinkScale, t);
            yield return null;
        }

        // Reveal UI elements
        if (categoryText != null) categoryText.gameObject.SetActive(true);
        if (tierImage != null) tierImage.gameObject.SetActive(true);

        if (category == CardCategory.Support)
        {
            if (nameText != null) nameText.gameObject.SetActive(true);
            if (descriptionText != null) descriptionText.gameObject.SetActive(true);
            if (valueText != null) valueText.gameObject.SetActive(false);
        }
        else
        {
            if (valueText != null) valueText.gameObject.SetActive(true);
            if (nameText != null) nameText.gameObject.SetActive(false);
            if (descriptionText != null) descriptionText.gameObject.SetActive(false);
        }

        TriggerRarityScreenShake();
        TriggerRarityFlash();

        // 3. Quick Expansion Overshoot
        elapsed = 0f;
        while (elapsed < overshootDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / overshootDuration;
            transform.localScale = Vector3.Lerp(shrinkScale, overshootScale, t);
            yield return null;
        }

        // 4. Return to normal scale
        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            transform.localScale = Vector3.Lerp(overshootScale, Vector3.one, t);
            yield return null;
        }
        transform.localScale = Vector3.one;

        isRevealing = false;
    }

    private void TriggerRarityFlash()
    {
        Image targetOverlay = null;

        switch (rarity)
        {
            case Rarity.Common: targetOverlay = commonFlashOverlay; break;
            case Rarity.Rare: targetOverlay = rareFlashOverlay; break;
            case Rarity.Legendary: targetOverlay = legendaryFlashOverlay; break;
        }

        if (targetOverlay != null)
        {
            StartCoroutine(Routine_FlashOverlay(targetOverlay));
        }
    }

    private IEnumerator Routine_FlashOverlay(Image overlay)
    {
        float halfDuration = flashDuration * 0.5f;
        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, maxFlashAlpha, elapsed / halfDuration);
            SetOverlayAlpha(overlay, alpha);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(maxFlashAlpha, 0f, elapsed / halfDuration);
            SetOverlayAlpha(overlay, alpha);
            yield return null;
        }

        SetOverlayAlpha(overlay, 0f);
    }

    private void TriggerRarityScreenShake()
    {
        float intensity = commonShakeIntensity;
        switch (rarity)
        {
            case Rarity.Rare: intensity = rareShakeIntensity; break;
            case Rarity.Legendary: intensity = legendaryShakeIntensity; break;
        }

        StartCoroutine(Routine_ScreenShake(intensity, shakeDuration));
    }

    private IEnumerator Routine_ScreenShake(float intensity, float duration)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) yield break;

        Vector3 originalCamPos = mainCam.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector2 randomOffset = Random.insideUnitCircle * intensity * 0.01f;
            mainCam.transform.localPosition = originalCamPos + new Vector3(randomOffset.x, randomOffset.y, 0f);
            yield return null;
        }

        mainCam.transform.localPosition = originalCamPos;
    }

    private void ApplyRarityColor()
    {
        if (tierImage == null) return;

        switch (rarity)
        {
            case Rarity.Common: tierImage.color = commonColor; break;
            case Rarity.Rare: tierImage.color = rareColor; break;
            case Rarity.Legendary: tierImage.color = legendaryColor; break;
        }
    }

    private void UpdateValueTextUI()
    {
        if (valueText == null) return;

        if (category == CardCategory.Support)
        {
            valueText.gameObject.SetActive(false);
        }
        else
        {
            valueText.gameObject.SetActive(true);
            valueText.text = cardValue.ToString();
        }
    }

    private void UpdateCategoryTextUI()
    {
        if (categoryText != null)
        {
            switch (category)
            {
                case CardCategory.Attack:
                    categoryText.text = "ATTACK";
                    break;
                case CardCategory.Defense:
                    categoryText.text = "DEFENSE";
                    break;
                case CardCategory.Support:
                    categoryText.text = "SUPPORT";
                    break;
            }
        }
    }

    private void UpdateSupportTextUI()
    {
        if (category == CardCategory.Support && currentSupportData != null)
        {
            if (nameText != null)
            {
                nameText.gameObject.SetActive(true);
                nameText.text = currentSupportData.cardName;
            }

            if (descriptionText != null)
            {
                descriptionText.gameObject.SetActive(true);
                descriptionText.text = currentSupportData.effectDescription;
            }
        }
        else
        {
            if (nameText != null) nameText.gameObject.SetActive(false);
            if (descriptionText != null) descriptionText.gameObject.SetActive(false);
        }
    }

    public void SaveBaseTransform()
    {
        baseLocalPosition = rectTransform.localPosition;
        baseLocalRotation = rectTransform.localRotation;
        UpdateTargetVisuals();
    }

    private void Update()
    {
        if (isRevealing) return;

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
        if (isRevealing) return;
        isHovered = true;
        UpdateTargetVisuals();

        if (!isSelected) transform.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isRevealing) return;
        isHovered = false;
        UpdateTargetVisuals();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isRevealing) return;
        ToggleSelection();
    }

    public void OnCardClicked()
    {
        if (isRevealing) return;
        ToggleSelection();
    }

    public void ToggleSelection()
    {
        if (isRevealing) return;
        isSelected = !isSelected;
        UpdateTargetVisuals();

        EndTurnManager endTurnMgr = FindObjectOfType<EndTurnManager>();
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

    public void HighlightCardForCategory(CardCategory targetCategory, float scaleMultiplier = 1.25f)
    {
        if (category != targetCategory) return;

        targetScale = restingScale * scaleMultiplier;

        Color targetHighlight = attackHighlightColor;
        switch (category)
        {
            case CardCategory.Attack:
                targetHighlight = attackHighlightColor;
                break;
            case CardCategory.Defense:
                targetHighlight = defenseHighlightColor;
                break;
            case CardCategory.Support:
                targetHighlight = supportHighlightColor;
                break;
        }

        if (cardFrameOrOutline != null)
        {
            cardFrameOrOutline.color = targetHighlight;
        }
    }

    public void ResetCombatHighlight()
    {
        targetScale = restingScale;
        if (cardFrameOrOutline != null)
        {
            cardFrameOrOutline.color = originalColor;
        }
    }
}