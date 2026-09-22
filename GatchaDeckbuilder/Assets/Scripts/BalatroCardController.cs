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

public class BalatroCardController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Card Category & Rarity")]
    [SerializeField] private CardCategory category = CardCategory.Attack;
    [SerializeField] private Rarity rarity = Rarity.Common;

    [Header("Card Value Settings")]
    [Tooltip("The numerical value of this card (Attack/Defense only; 0 for Support).")]
    [SerializeField] private int cardValue;

    private string supportName = "";
    private string supportEffect = "";
    private bool isForeverEffect = false;

    public string SupportName => supportName;
    public string SupportEffect => supportEffect;
    public bool IsForever => isForeverEffect;

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

    [Header("Card Hidden / Obscure Settings")]
    [Tooltip("UI Image placed over the card face to obscure stats/details until revealed.")]
    [SerializeField] private GameObject cardObscureOverlay;

    [Header("Rarity Flash Overlays")]
    [SerializeField] private Image commonFlashOverlay;
    [SerializeField] private Image rareFlashOverlay;
    [SerializeField] private Image legendaryFlashOverlay;
    [Range(0f, 1f)]
    [SerializeField] private float maxFlashAlpha = 0.6f;
    [SerializeField] private float flashDuration = 0.2f;

    [Header("Rarity Tier Colors")]
    [SerializeField] private Color commonColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [SerializeField] private Color rareColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0f, 1f);

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
    [SerializeField] private Color attackHighlightColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color defenseHighlightColor = new Color(0.3f, 0.6f, 1f, 1f);
    [SerializeField] private Color supportHighlightColor = new Color(0.3f, 0.9f, 0.4f, 1f);

    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource secondaryAudioSource;
    [SerializeField] private AudioClip hoverSFX;
    [SerializeField] private AudioClip selectSFX;
    [SerializeField] private AudioClip flashSFX;
    [SerializeField] private AudioClip legendaryFlashSecondarySFX;
    [SerializeField] private AudioClip commonShakeSFX;
    [SerializeField] private AudioClip rareShakeSFX;
    [SerializeField] private AudioClip legendaryShakeSFX;

    [Header("Rarity Flash Pitch Settings")]
    [SerializeField] private float commonFlashPitch = 0.9f;
    [SerializeField] private float rareFlashPitch = 1.1f;
    [SerializeField] private float legendaryFlashPitch = 1.3f;

    [Header("Audio Pitch Juiciness")]
    [SerializeField] private bool randomizePitch = true;
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    private bool isHovered = false;
    private bool isSelected = false;
    private bool isRevealing = false;
    private bool isObscured = false;

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
    public bool IsObscured => isObscured;
    public Vector3 RestingScale => restingScale;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        if (secondaryAudioSource == null)
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            if (sources.Length > 1)
            {
                secondaryAudioSource = sources[1];
            }
            else
            {
                secondaryAudioSource = gameObject.AddComponent<AudioSource>();
                secondaryAudioSource.playOnAwake = false;
            }
        }

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

    /// <summary>
    /// Enables or disables the obscure image overlay to hide stats/details.
    /// </summary>
    public void SetCardObscured(bool obscure)
    {
        isObscured = obscure;

        if (cardObscureOverlay != null)
        {
            cardObscureOverlay.SetActive(obscure);
        }

        if (obscure)
        {
            if (valueText != null) valueText.gameObject.SetActive(false);
            if (categoryText != null) categoryText.gameObject.SetActive(false);
            if (nameText != null) nameText.gameObject.SetActive(false);
            if (descriptionText != null) descriptionText.gameObject.SetActive(false);
            if (tierImage != null) tierImage.gameObject.SetActive(false);
        }
        else
        {
            RevealCardVisuals();
        }
    }

    /// <summary>
    /// Disables obscuring overlay and restores category/value UI elements.
    /// </summary>
    public void RevealCard()
    {
        SetCardObscured(false);
    }

    public void RevealCardVisuals()
    {
        if (cardObscureOverlay != null)
        {
            cardObscureOverlay.SetActive(false);
        }

        isObscured = false;

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

    private void PlaySound(AudioClip clip, float overridePitch = -1f, AudioSource targetSource = null)
    {
        AudioSource src = targetSource != null ? targetSource : audioSource;
        if (clip == null || src == null) return;

        if (overridePitch > 0f)
        {
            src.pitch = overridePitch;
        }
        else if (randomizePitch)
        {
            src.pitch = Random.Range(minPitch, maxPitch);
        }
        else
        {
            src.pitch = 1f;
        }

        src.PlayOneShot(clip);
    }

    public void ApplyPulledCardData(string cardName, CardCategory cardCategory, Rarity cardRarity, int value, string effectText = "", bool isForever = false)
    {
        category = cardCategory;
        rarity = cardRarity;
        cardValue = value;

        supportName = cardName;
        supportEffect = effectText;
        isForeverEffect = isForever;

        ApplyRarityColor();
        UpdateValueTextUI();
        UpdateCategoryTextUI();
        UpdateSupportTextUI();
    }

    public void PrepareForUnrevealedSpawn()
    {
        isRevealing = true;
        if (cardObscureOverlay != null) cardObscureOverlay.SetActive(true);
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

        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            transform.localScale = Vector3.Lerp(closeUpScale, shrinkScale, t);
            yield return null;
        }

        RevealCardVisuals();

        TriggerRarityScreenShake();
        TriggerRarityFlash();

        elapsed = 0f;
        while (elapsed < overshootDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / overshootDuration;
            transform.localScale = Vector3.Lerp(shrinkScale, overshootScale, t);
            yield return null;
        }

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
        float flashPitch = commonFlashPitch;

        switch (rarity)
        {
            case Rarity.Common:
                targetOverlay = commonFlashOverlay;
                flashPitch = commonFlashPitch;
                break;
            case Rarity.Rare:
                targetOverlay = rareFlashOverlay;
                flashPitch = rareFlashPitch;
                break;
            case Rarity.Legendary:
                targetOverlay = legendaryFlashOverlay;
                flashPitch = legendaryFlashPitch;
                break;
        }

        if (targetOverlay != null)
        {
            PlaySound(flashSFX, flashPitch);

            if (rarity == Rarity.Legendary && legendaryFlashSecondarySFX != null)
            {
                PlaySound(legendaryFlashSecondarySFX, 1.0f, secondaryAudioSource);
            }

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
        AudioClip shakeClip = commonShakeSFX;

        switch (rarity)
        {
            case Rarity.Common:
                intensity = commonShakeIntensity;
                shakeClip = commonShakeSFX;
                break;
            case Rarity.Rare:
                intensity = rareShakeIntensity;
                shakeClip = rareShakeSFX;
                break;
            case Rarity.Legendary:
                intensity = legendaryShakeIntensity;
                shakeClip = legendaryShakeSFX;
                break;
        }

        PlaySound(shakeClip);
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
        if (category == CardCategory.Support)
        {
            if (nameText != null)
            {
                nameText.gameObject.SetActive(true);
                nameText.text = supportName;
            }

            if (descriptionText != null)
            {
                descriptionText.gameObject.SetActive(true);
                descriptionText.text = supportEffect;
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

        if (!isHovered)
        {
            PlaySound(hoverSFX);
        }

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

        PlaySound(selectSFX);

        UpdateTargetVisuals();

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