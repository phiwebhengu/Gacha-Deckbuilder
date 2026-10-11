using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class DrawTimerManager : NetworkBehaviour
{
    [Header("Round Display UI")]
    [SerializeField] private TextMeshProUGUI roundDisplayText;
    [SerializeField] private CanvasGroup roundDisplayCanvasGroup;
    [SerializeField] private float roundTextFadeDuration = 0.8f;
    [SerializeField] private float roundTextHoldDuration = 0.6f;

    [Header("Timer Settings")]
    [SerializeField] private float drawWindowDuration = 10f; // Updated to 10s window

    [Header("Countdown Juice")]
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private float wordDisplayDuration = 0.6f;
    [SerializeField] private float popStartScaleMultiplier = 1.8f;
    [SerializeField] private float shrinkSpeed = 10f;

    [Header("Countdown Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip countdownBeepClip;
    [SerializeField] private AudioClip timerTickClip; // Optional distinct tick SFX (falls back to countdownBeepClip if null)
    [SerializeField] private float normalPitch = 1.0f;
    [SerializeField] private float drawPitch = 1.4f;
    [SerializeField] private float urgentTickPitch = 1.2f;

    [Header("Timer Audio Juice")]
    [SerializeField] private float urgentThreshold = 5f; // Seconds remaining to double tick rate
    [SerializeField] private float normalTickInterval = 1.0f; // 1 tick per second
    [SerializeField] private float urgentTickInterval = 0.5f; // 2 ticks per second

    [Header("Timer UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject preGamePanel;

    [Header("System References")]
    [SerializeField] private GachaManager gachaManager;

    private int currentRound = 1;
    private bool isTimerRunning = false;
    private bool playerHasDrawn = false;
    private double roundStartTime;
    private int lastTickIndex = -1;

    public bool IsDrawPhaseActive => isTimerRunning;
    public int CurrentRound => currentRound;
    public event Action<bool> OnDrawPhaseChanged;

    private void Awake()
    {
        // Auto-get AudioSource if not manually assigned
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        // Hide all UI elements initially
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (timerText != null) timerText.gameObject.SetActive(false); // Hide timer until draw phase

        UpdateTimerUI(1f, drawWindowDuration);
    }

    public void RequestStartRound(int roundNumber = 1)
    {
        if (!IsSpawned)
        {
            Debug.LogError("[DrawTimer] RequestStartRound called, but this NetworkBehaviour is not spawned!");
            return;
        }

        if (IsServer)
        {
            Debug.Log($"[DrawTimer] Host/Server requesting start for Round {roundNumber}");
            RequestStartRoundServerRpc(roundNumber);
        }
        else
        {
            Debug.LogWarning("[DrawTimer] Client tried to start round. Only server can initiate.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartRoundServerRpc(int roundNumber)
    {
        Debug.Log("[DrawTimer] ServerRPC received. Starting round sequence.");
        StartRoundClientRpc(roundNumber, NetworkManager.Singleton.LocalTime.Time);
    }

    [ClientRpc]
    private void StartRoundClientRpc(int roundNumber, double startTime)
    {
        Debug.Log($"[DrawTimer] ClientRPC received. Starting sequence for Round {roundNumber}");
        currentRound = roundNumber;
        playerHasDrawn = false;
        isTimerRunning = false; // Keep false until the countdown actually finishes
        if (preGamePanel != null)
        {
            preGamePanel.SetActive(true);
        }
        StartCoroutine(Routine_StartRoundSequence());
    }

    private IEnumerator Routine_StartRoundSequence()
    {
        if (roundDisplayText != null && roundDisplayCanvasGroup != null)
        {
            roundDisplayText.text = $"Round {currentRound}";
            roundDisplayCanvasGroup.alpha = 1f;
            yield return new WaitForSeconds(roundTextHoldDuration);

            float elapsed = 0f;
            while (elapsed < roundTextFadeDuration)
            {
                elapsed += Time.deltaTime;
                roundDisplayCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / roundTextFadeDuration);
                yield return null;
            }
            roundDisplayCanvasGroup.alpha = 1f;
        }

        yield return StartCoroutine(Routine_ExecuteCountdown());
    }

    private IEnumerator Routine_ExecuteCountdown()
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            yield return StartCoroutine(Routine_AnimateWord("READY", normalPitch));
            yield return StartCoroutine(Routine_AnimateWord("SET", normalPitch));
            yield return StartCoroutine(Routine_AnimateWord("DRAW!", drawPitch));
            countdownText.gameObject.SetActive(false); // Countdown disappears
        }

        // NOW start the actual timer
        StartDrawPhase();
    }

    private IEnumerator Routine_AnimateWord(string word, float pitch)
    {
        countdownText.text = word;
        RectTransform textRect = countdownText.rectTransform;
        textRect.localScale = Vector3.one * popStartScaleMultiplier;

        // Play the intro countdown sound
        PlaySound(countdownBeepClip, pitch);

        float elapsedTime = 0f;
        while (elapsedTime < wordDisplayDuration)
        {
            elapsedTime += Time.deltaTime;
            textRect.localScale = Vector3.Lerp(textRect.localScale, Vector3.one, Time.deltaTime * shrinkSpeed);
            yield return null;
        }
        textRect.localScale = Vector3.one;
    }

    private void PlaySound(AudioClip clip, float pitch)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip);
        }
    }

    private void StartDrawPhase()
    {
        playerHasDrawn = false;
        isTimerRunning = true;
        lastTickIndex = -1;

        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            roundStartTime = NetworkManager.Singleton.LocalTime.Time;
        }
        else
        {
            roundStartTime = Time.time;
        }

        OnDrawPhaseChanged?.Invoke(true);
        Debug.Log("[DrawTimer] Draw phase officially ACTIVE. Timer started.");
    }

    private void Update()
    {
        if (!isTimerRunning) return;

        if (NetworkManager.Singleton == null) return;

        double currentTime = NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.LocalTime.Time
            : Time.time;

        double elapsed = currentTime - roundStartTime;
        float timeRemaining = (float)(drawWindowDuration - elapsed);

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndDrawPhase();
            return;
        }

        // Handle Audio Ticks based on remaining time
        ProcessTimerAudioTicks((float)elapsed, timeRemaining);

        float fillRatio = Mathf.Clamp01(timeRemaining / drawWindowDuration);
        UpdateTimerUI(fillRatio, timeRemaining);
    }

    private void ProcessTimerAudioTicks(float elapsedSeconds, float timeRemaining)
    {
        // Calculate effective tick rate based on remaining time threshold
        bool isUrgent = timeRemaining <= urgentThreshold;
        float interval = isUrgent ? urgentTickInterval : normalTickInterval;
        float currentPitch = isUrgent ? urgentTickPitch : normalPitch;

        // Determine current discrete tick index
        int currentTickIndex = Mathf.FloorToInt(elapsedSeconds / interval);

        if (currentTickIndex > lastTickIndex)
        {
            lastTickIndex = currentTickIndex;
            AudioClip clipToPlay = timerTickClip != null ? timerTickClip : countdownBeepClip;
            PlaySound(clipToPlay, currentPitch);
        }
    }

    public void NotifyCardsDrawn()
    {
        if (isTimerRunning)
        {
            playerHasDrawn = true;
            Debug.Log("[DrawTimer] Local instance registered: Player drew cards!");

            if (!IsServer && IsSpawned)
            {
                NotifyCardsDrawnServerRpc();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyCardsDrawnServerRpc()
    {
        playerHasDrawn = true;
        Debug.Log("[DrawTimer] Server received draw notification from client!");
    }

    private void EndDrawPhase()
    {
        isTimerRunning = false;
        OnDrawPhaseChanged?.Invoke(false);

        if (preGamePanel != null) preGamePanel.SetActive(false);

        if (timerText != null) timerText.gameObject.SetActive(false);

        if (!playerHasDrawn)
        {
            Debug.Log("[DrawTimer] Time expired! Executing random auto-draw penalty.");
            ExecuteAutoDrawPenalty();
        }

        UpdateTimerUI(0f, 0f);
    }

    private void ExecuteAutoDrawPenalty()
    {
        bool isAction = UnityEngine.Random.value > 0.5f;
        DeckType deckToPull = isAction ? DeckType.Action : DeckType.Support;

        PlayerHandManager handManager = FindFirstObjectByType<PlayerHandManager>();

        if (handManager != null)
        {
            Debug.Log($"[DrawTimer] Time expired! Executing random auto-draw penalty: 1 {deckToPull} card.");
            handManager.DealCardsFromTokens(1, deckToPull);
        }
        else
        {
            Debug.LogError("[DrawTimer] PlayerHandManager not found! Cannot execute auto-draw penalty.");
            NotifyCardsDrawn();
        }
    }

    private void UpdateTimerUI(float fillRatio, float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining)).ToString();
        }
    }
}