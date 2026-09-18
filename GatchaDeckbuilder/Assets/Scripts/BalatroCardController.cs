using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

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

        ResetFlashOverlays();
        // REMOVE THIS LINE: InitializeCardCategoryAndRarity();
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

    public void InitializeCardCategoryAndRarity(PityManager pityManager = null, bool isAI = false)
    {
        if (category == CardCategory.Attack)
        {
            // 1. Check pity status on the specific PityManager passed in
            bool forceLegendary = (pityManager != null && pityManager.ShouldForceLegendary());

            if (forceLegendary)
            {
                rarity = CardRarity.Legendary;
                cardValue = Random.Range(9, 11);
            }
            else
            {
                float roll = Random.Range(0f, 100f);

                if (roll < 60f)
                {
                    rarity = CardRarity.Common;
                    cardValue = Random.Range(1, 6);
                }
                else if (roll < 90f)
                {
                    rarity = CardRarity.Rare;
                    cardValue = Random.Range(6, 9);
                }
                else
                {
                    rarity = CardRarity.Legendary;
                    cardValue = Random.Range(9, 11);
                }
            }

            // 2. Register with owner's PityManager
            if (pityManager != null)
            {
                pityManager.RegisterPull(rarity);
            }
        }

        ApplyRarityColor();
        UpdateValueTextUI();
    }

    public void PrepareForUnrevealedSpawn()
    {
        isRevealing = true;
        if (valueText != null) valueText.gameObject.SetActive(false);
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

        // --- INSTANT REVEAL, SCREEN SHAKE & RARITY FLASH SYNCHRONIZED WITH OVERSHOOT IMPACT ---
        if (valueText != null) valueText.gameObject.SetActive(true);
        if (tierImage != null) tierImage.gameObject.SetActive(true);

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
            case CardRarity.Common: targetOverlay = commonFlashOverlay; break;
            case CardRarity.Rare: targetOverlay = rareFlashOverlay; break;
            case CardRarity.Legendary: targetOverlay = legendaryFlashOverlay; break;
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

        // Flash In
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, maxFlashAlpha, elapsed / halfDuration);
            SetOverlayAlpha(overlay, alpha);
            yield return null;
        }

        // Flash Out
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
            case CardRarity.Rare: intensity = rareShakeIntensity; break;
            case CardRarity.Legendary: intensity = legendaryShakeIntensity; break;
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
            case CardRarity.Common: tierImage.color = commonColor; break;
            case CardRarity.Rare: tierImage.color = rareColor; break;
            case CardRarity.Legendary: tierImage.color = legendaryColor; break;
        }
    }

    private void UpdateValueTextUI()
    {
        if (valueText != null)
        {
            valueText.text = cardValue.ToString();
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
}