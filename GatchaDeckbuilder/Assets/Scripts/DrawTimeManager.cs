using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class DrawTimerManager : MonoBehaviour
{
    [Header("Match & Round Settings")]
    [SerializeField] private int maxRounds = 4;

    [Header("Round Display UI References")]
    [SerializeField] private TextMeshProUGUI roundDisplayText;
    [SerializeField] private CanvasGroup roundDisplayCanvasGroup;
    [SerializeField] private float roundTextFadeDuration = 0.8f;
    [SerializeField] private float roundTextHoldDuration = 0.6f;

    [Header("Timer Settings")]
    [SerializeField] private float drawWindowDuration = 5f;
    [SerializeField] private float pulseSpeed = 8f;
    [SerializeField] private float pulseScaleAmount = 0.15f;

    [Header("Countdown Juice Settings")]
    [Tooltip("Text component used to display Ready... Set... Draw!")]
    [SerializeField] private TextMeshProUGUI countdownText;

    [Tooltip("Time in seconds each countdown word stays on screen")]
    [SerializeField] private float wordDisplayDuration = 0.6f;

    [Tooltip("Starting scale multiplier when a word appears before shrinking back to 1.0")]
    [SerializeField] private float popStartScaleMultiplier = 1.8f;

    [Tooltip("Speed at which the word shrinks down to normal scale")]
    [SerializeField] private float shrinkSpeed = 10f;

    [Header("Button Polish Settings")]
    [SerializeField] private float buttonHoverScaleMultiplier = 1.1f;
    [SerializeField] private Color buttonHoverColor = new Color(1f, 0.9f, 0.5f, 1f);

    [Header("Damage Overlay Settings")]
    [Tooltip("UI Image that flashes red or overlay visual when player takes incremental HP damage")]
    [SerializeField] private Image damageOverlayImage;
    [SerializeField] private float damageFlashDuration = 0.15f;
    [Range(0f, 1f)]
    [SerializeField] private float maxDamageOverlayAlpha = 0.75f;
    [Tooltip("Base color to apply when flashing (e.g., pure red for blood flash)")]
    [SerializeField] private Color damageFlashColor = Color.red;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip startButtonHoverSFX;
    [SerializeField] private AudioClip startButtonClickSFX;
    [SerializeField] private AudioClip roundStartSFX;
    [SerializeField] private AudioClip timerTickSFX;

    [Header("Countdown Audio Settings")]
    [SerializeField] private AudioClip countdownWordSFX;
    [Tooltip("Pitch multiplier applied to the SFX when 'DRAW!' is displayed")]
    [SerializeField] private float drawWordPitchMultiplier = 1.35f;

    [Header("UI References")]
    [SerializeField] private Button startDrawButton;
    [SerializeField] private Image radialTimerImage;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private RectTransform timerContainer;
    [Tooltip("UI Image placed over the player's hand to block clicks during the draw phase")]
    [SerializeField] private Image handBlockerImage;

    [Header("System References")]
    [SerializeField] private TokenSelectionManager tokenManager;
    [SerializeField] private List<DeckButton> availableDecks = new List<DeckButton>();

    [Header("AI Rival Integration")]
    [SerializeField] private AIRivalController aiRival;

    private int currentRound = 1;
    private float currentTimer;
    private bool isTimerRunning = false;
    private bool playerHasDrawn = false;
    private Vector3 originalTimerScale = Vector3.one;

    private Vector3 originalButtonScale = Vector3.one;
    private Color originalButtonColor = Color.white;
    private Image buttonImage;

    private int lastLoggedSecond = -1;
    private Coroutine activeDamageFlashCoroutine;
    private CanvasGroup damageOverlayCanvasGroup;

    public bool IsDrawPhaseActive => isTimerRunning;
    public int CurrentRound => currentRound;
    public int MaxRounds => maxRounds;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (timerContainer != null)
        {
            originalTimerScale = timerContainer.localScale;
            timerContainer.gameObject.SetActive(false);
        }

        if (startDrawButton != null)
        {
            originalButtonScale = startDrawButton.transform.localScale;
            buttonImage = startDrawButton.GetComponent<Image>();
            if (buttonImage != null) originalButtonColor = buttonImage.color;

            startDrawButton.onClick.AddListener(OnStartButtonClicked);
            AddHoverAndExitListenersToButton(startDrawButton);
        }

        // Consolidated Damage Overlay Setup
        if (damageOverlayImage != null)
        {
            damageOverlayImage.raycastTarget = false;
            damageOverlayImage.gameObject.SetActive(true);
            damageOverlayImage.transform.SetAsLastSibling(); // Bring to front of UI canvas

            damageOverlayCanvasGroup = damageOverlayImage.GetComponent<CanvasGroup>();

            // Apply solid color (RGB) without altering alpha directly
            Color baseColor = damageFlashColor;
            baseColor.a = 1f;
            damageOverlayImage.color = baseColor;

            // Start fully hidden
            ApplyOverlayAlpha(0f);
        }

        if (tokenManager == null)
        {
            tokenManager = FindObjectOfType<TokenSelectionManager>();
        }

        if (handBlockerImage != null)
        {
            handBlockerImage.gameObject.SetActive(false);
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(false);
        }

        if (aiRival != null)
        {
            aiRival.StopAIDrawPhase();
        }

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (roundDisplayCanvasGroup != null)
        {
            roundDisplayCanvasGroup.alpha = 0f;
        }

        UpdateTimerUI(1f, drawWindowDuration);
    }

    private void AddHoverAndExitListenersToButton(Button btn)
    {
        EventTrigger trigger = btn.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btn.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener((data) =>
        {
            PlaySFX(startButtonHoverSFX);
            btn.transform.localScale = originalButtonScale * buttonHoverScaleMultiplier;
            if (buttonImage != null) buttonImage.color = buttonHoverColor;
        });
        trigger.triggers.Add(enterEntry);

        EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener((data) =>
        {
            btn.transform.localScale = originalButtonScale;
            if (buttonImage != null) buttonImage.color = originalButtonColor;
        });
        trigger.triggers.Add(exitEntry);
    }

    private void OnStartButtonClicked()
    {
        PlaySFX(startButtonClickSFX);

        if (startDrawButton != null)
        {
            startDrawButton.transform.localScale = originalButtonScale;
            if (buttonImage != null) buttonImage.color = originalButtonColor;
            startDrawButton.gameObject.SetActive(false);
        }

        StartCoroutine(Routine_StartRoundSequence());
    }

    /// <summary>
    /// Instantly spikes overlay opacity to peak visual levels and handles high-frequency damage calls smoothly.
    /// </summary>
    public void FlashDamageOverlay()
    {
        if (damageOverlayImage == null)
        {
            Debug.LogWarning("[DrawTimerManager] FlashDamageOverlay called, but damageOverlayImage reference is missing!");
            return;
        }

        ApplyOverlayAlpha(maxDamageOverlayAlpha);

        if (activeDamageFlashCoroutine != null)
        {
            StopCoroutine(activeDamageFlashCoroutine);
        }

        activeDamageFlashCoroutine = StartCoroutine(Routine_FadeOutOverlay());
    }

    private IEnumerator Routine_FadeOutOverlay()
    {
        float elapsed = 0f;
        float startingAlpha = GetCurrentOverlayAlpha();

        while (elapsed < damageFlashDuration)
        {
            elapsed += Time.deltaTime;
            float currentAlpha = Mathf.Lerp(startingAlpha, 0f, elapsed / damageFlashDuration);
            ApplyOverlayAlpha(currentAlpha);
            yield return null;
        }

        ApplyOverlayAlpha(0f);
        activeDamageFlashCoroutine = null;
    }

    private float GetCurrentOverlayAlpha()
    {
        if (damageOverlayCanvasGroup != null)
        {
            return damageOverlayCanvasGroup.alpha;
        }
        else if (damageOverlayImage != null)
        {
            return damageOverlayImage.color.a;
        }
        return 0f;
    }

    private void ApplyOverlayAlpha(float alpha)
    {
        if (damageOverlayCanvasGroup != null)
        {
            damageOverlayCanvasGroup.alpha = alpha;
        }
        else if (damageOverlayImage != null)
        {
            Color c = damageOverlayImage.color;
            c.a = alpha;
            damageOverlayImage.color = c;
        }
    }

    public void TriggerNextRound()
    {
        currentRound++;
        StartCoroutine(Routine_StartRoundSequence());
    }

    private IEnumerator Routine_StartRoundSequence()
    {
        if (roundDisplayText != null && roundDisplayCanvasGroup != null)
        {
            roundDisplayText.text = $"ROUND {currentRound}";
            roundDisplayCanvasGroup.alpha = 1f;

            PlaySFX(roundStartSFX);

            yield return new WaitForSeconds(roundTextHoldDuration);

            float elapsed = 0f;
            while (elapsed < roundTextFadeDuration)
            {
                elapsed += Time.deltaTime;
                roundDisplayCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / roundTextFadeDuration);
                yield return null;
            }

            roundDisplayCanvasGroup.alpha = 0f;
        }

        yield return StartCoroutine(Routine_ExecuteCountdown());
    }

    private IEnumerator Routine_ExecuteCountdown()
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);

            yield return StartCoroutine(Routine_AnimateWord("READY", 1.0f));
            yield return StartCoroutine(Routine_AnimateWord("SET", 1.0f));
            yield return StartCoroutine(Routine_AnimateWord("DRAW!", drawWordPitchMultiplier));

            countdownText.gameObject.SetActive(false);
        }

        StartDrawPhase();
    }

    private IEnumerator Routine_AnimateWord(string word, float pitchMultiplier = 1.0f)
    {
        countdownText.text = word;
        RectTransform textRect = countdownText.rectTransform;

        Vector3 oversizedScale = Vector3.one * popStartScaleMultiplier;
        Vector3 targetScale = Vector3.one;

        textRect.localScale = oversizedScale;

        PlaySFXWithPitch(countdownWordSFX, pitchMultiplier);

        float elapsedTime = 0f;

        while (elapsedTime < wordDisplayDuration)
        {
            elapsedTime += Time.deltaTime;
            textRect.localScale = Vector3.Lerp(textRect.localScale, targetScale, Time.deltaTime * shrinkSpeed);
            yield return null;
        }

        textRect.localScale = targetScale;
    }

    public void StartDrawPhase()
    {
        playerHasDrawn = false;
        currentTimer = drawWindowDuration;
        isTimerRunning = true;
        lastLoggedSecond = Mathf.CeilToInt(currentTimer);

        if (handBlockerImage != null)
        {
            handBlockerImage.gameObject.SetActive(true);
        }

        if (timerContainer != null)
        {
            timerContainer.gameObject.SetActive(true);
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(true);
            // Note: Remove ResetTokensForNewRound() here if tokens are intended 
            // to persist continuously across rounds without refilling.
        }

        if (aiRival != null)
        {
            aiRival.StartAIDrawPhase();
        }

        StartCoroutine(Routine_RunTimer());
    }

    private IEnumerator Routine_RunTimer()
    {
        while (currentTimer > 0f)
        {
            currentTimer -= Time.deltaTime;
            float fillRatio = Mathf.Clamp01(currentTimer / drawWindowDuration);

            int currentSecond = Mathf.CeilToInt(Mathf.Max(0f, currentTimer));
            if (currentSecond != lastLoggedSecond && currentSecond >= 0)
            {
                lastLoggedSecond = currentSecond;
                PlaySFX(timerTickSFX);
            }

            UpdateTimerUI(fillRatio, currentTimer);

            if (timerContainer != null)
            {
                float pulse = 1f + (Mathf.Sin(Time.time * pulseSpeed) * pulseScaleAmount * (1f - fillRatio));
                timerContainer.localScale = originalTimerScale * pulse;
            }

            yield return null;
        }

        EndDrawPhase();
    }

    public void NotifyCardsDrawn()
    {
        if (isTimerRunning)
        {
            playerHasDrawn = true;
            Debug.Log("[Draw Timer] Player drew cards! Selection window remains open until time runs out.");
        }
    }

    private void EndDrawPhase()
    {
        isTimerRunning = false;

        if (handBlockerImage != null)
        {
            handBlockerImage.gameObject.SetActive(false);
        }

        if (timerContainer != null)
        {
            timerContainer.localScale = originalTimerScale;
            timerContainer.gameObject.SetActive(false);
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(false);
        }

        if (aiRival != null)
        {
            aiRival.StopAIDrawPhase();
        }

        if (!playerHasDrawn)
        {
            Debug.Log("[Draw Timer] Time expired with zero selections! Executing random auto-draw penalty.");
            StartCoroutine(Routine_ExecuteAutoDrawPenalty());
        }

        UpdateTimerUI(0f, 0f);
    }

    private IEnumerator Routine_ExecuteAutoDrawPenalty()
    {
        if (tokenManager == null || availableDecks.Count == 0) yield break;

        int randomIndex = Random.Range(0, availableDecks.Count);
        DeckButton chosenDeck = availableDecks[randomIndex];

        tokenManager.gameObject.SetActive(true);
        tokenManager.ForceAutoDrawSingleToken(chosenDeck);

        yield return new WaitForSeconds(0.5f);

        tokenManager.gameObject.SetActive(false);
    }

    private void UpdateTimerUI(float fillRatio, float timeRemaining)
    {
        if (radialTimerImage != null)
        {
            radialTimerImage.fillAmount = fillRatio;
        }

        if (timerText != null)
        {
            timerText.text = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining)).ToString();
        }
    }

    private void PlaySFX(AudioClip clip)
    {
        PlaySFXWithPitch(clip, 1.0f);
    }

    private void PlaySFXWithPitch(AudioClip clip, float pitch)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip);
    }
}