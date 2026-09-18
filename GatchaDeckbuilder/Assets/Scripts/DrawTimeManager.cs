using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

    [Header("UI References")]
    [SerializeField] private Button startDrawButton;
    [SerializeField] private Image radialTimerImage;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private RectTransform timerContainer;

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

    public bool IsDrawPhaseActive => isTimerRunning; // <-- Add this public property

    public int CurrentRound => currentRound;
    public int MaxRounds => maxRounds;

    private void Awake()
    {
        if (timerContainer != null)
        {
            originalTimerScale = timerContainer.localScale;
            timerContainer.gameObject.SetActive(false);
        }

        if (startDrawButton != null)
        {
            startDrawButton.onClick.AddListener(OnStartButtonClicked);
        }

        if (tokenManager == null)
        {
            tokenManager = FindObjectOfType<TokenSelectionManager>();
        }

        // Hide Player's Tokens on Start
        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(false);
        }

        // Hide AI's Tokens on Start
        if (aiRival != null)
        {
            aiRival.StopAIDrawPhase();
        }

        // Hide countdown text on start
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

    private void OnStartButtonClicked()
    {
        if (startDrawButton != null)
        {
            startDrawButton.gameObject.SetActive(false);
        }

        StartCoroutine(Routine_StartRoundSequence());
    }

    public void TriggerNextRound()
    {
        currentRound++;
        StartCoroutine(Routine_StartRoundSequence());
    }

    private IEnumerator Routine_StartRoundSequence()
    {
        // 1. Display and Fade-Out Round Banner Text
        if (roundDisplayText != null && roundDisplayCanvasGroup != null)
        {
            roundDisplayText.text = $"ROUND {currentRound}";
            roundDisplayCanvasGroup.alpha = 1f;

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

        // 2. Execute Ready... Set... Draw!
        yield return StartCoroutine(Routine_ExecuteCountdown());
    }

    private IEnumerator Routine_ExecuteCountdown()
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);

            yield return StartCoroutine(Routine_AnimateWord("READY"));
            yield return StartCoroutine(Routine_AnimateWord("SET"));
            yield return StartCoroutine(Routine_AnimateWord("DRAW!"));

            countdownText.gameObject.SetActive(false);
        }

        StartDrawPhase();
    }

    private IEnumerator Routine_AnimateWord(string word)
    {
        countdownText.text = word;
        RectTransform textRect = countdownText.rectTransform;

        Vector3 oversizedScale = Vector3.one * popStartScaleMultiplier;
        Vector3 targetScale = Vector3.one;

        textRect.localScale = oversizedScale;

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
        isTimerRunning = true; // <-- Controls draw state

        if (timerContainer != null)
        {
            timerContainer.gameObject.SetActive(true);
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(true);
            tokenManager.ResetTokensForNewRound();
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
}