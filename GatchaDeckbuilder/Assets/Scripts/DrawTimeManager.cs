using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class DrawTimerManager : NetworkBehaviour
{
    [Header("Game Mode Settings")]
    [SerializeField] private bool isAIMode = true;

    [Header("Round Display UI")]
    [SerializeField] private TextMeshProUGUI roundDisplayText;
    [SerializeField] private CanvasGroup roundDisplayCanvasGroup;
    [SerializeField] private float roundTextFadeDuration = 0.8f;
    [SerializeField] private float roundTextHoldDuration = 0.6f;

    [Header("Timer Settings")]
    [SerializeField] private float drawWindowDuration = 10f;

    [Header("Countdown Juice")]
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private float wordDisplayDuration = 0.6f;
    [SerializeField] private float popStartScaleMultiplier = 1.8f;
    [SerializeField] private float shrinkSpeed = 10f;

    [Header("Countdown Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip countdownBeepClip;
    [SerializeField] private AudioClip timerTickClip;
    [SerializeField] private float normalPitch = 1.0f;
    [SerializeField] private float drawPitch = 1.4f;
    [SerializeField] private float urgentTickPitch = 1.2f;

    [Header("Timer Audio Juice")]
    [SerializeField] private float urgentThreshold = 5f;
    [SerializeField] private float normalTickInterval = 1.0f;
    [SerializeField] private float urgentTickInterval = 0.5f;

    [Header("Timer UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject preGamePanel;

    [Header("System References")]
    [SerializeField] private GachaManager gachaManager;

    // Network-aware authoritative round state
    public NetworkVariable<int> NetworkCurrentRound = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool isTimerRunning = false;
    private float timeRemaining;
    private int lastTickIndex = -1;

    public bool IsDrawPhaseActive => isTimerRunning;
    public int CurrentRound => IsSpawned ? NetworkCurrentRound.Value : currentRoundFallback;
    private int currentRoundFallback = 1;

    public event Action<bool> OnDrawPhaseChanged;

    private bool hasInitializedOfflineRound = false;

    private void Awake()
    {
        Application.runInBackground = true;
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (timerText != null) timerText.gameObject.SetActive(false);
        UpdateTimerUI(1f, drawWindowDuration);
    }

    public override void OnNetworkSpawn()
    {
        NetworkCurrentRound.OnValueChanged += OnRoundValueChanged;
        if (IsServer && NetworkCurrentRound.Value == 0)
        {
            NetworkCurrentRound.Value = 1;
        }
    }

    public override void OnNetworkDespawn()
    {
        NetworkCurrentRound.OnValueChanged -= OnRoundValueChanged;
    }

    private void OnRoundValueChanged(int oldRound, int newRound)
    {
        StartLocalRoundSequence(newRound);
    }

    private void Start()
    {
        if (isAIMode && !hasInitializedOfflineRound)
        {
            hasInitializedOfflineRound = true;
            Debug.Log("[DrawTimer] AI Mode active: Auto-starting Round 1.");
            RequestStartRound(1);
        }
    }

    public void RequestStartRound(int roundNumber)
    {
        if (isAIMode)
        {
            currentRoundFallback = roundNumber;
            StartLocalRoundSequence(roundNumber);
            return;
        }

        if (!IsSpawned) return;

        if (IsServer)
        {
            NetworkCurrentRound.Value = roundNumber;
        }
    }

    private void StartLocalRoundSequence(int roundNumber)
    {
        currentRoundFallback = roundNumber;
        isTimerRunning = false;
        if (preGamePanel != null) preGamePanel.SetActive(true);
        StartCoroutine(Routine_StartRoundSequence(roundNumber));
    }

    private IEnumerator Routine_StartRoundSequence(int roundNumber)
    {
        if (roundDisplayText != null && roundDisplayCanvasGroup != null)
        {
            roundDisplayText.text = $"Round {roundNumber}";
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
            countdownText.gameObject.SetActive(false);
        }

        StartDrawPhase();
    }

    private IEnumerator Routine_AnimateWord(string word, float pitch)
    {
        countdownText.text = word;
        RectTransform textRect = countdownText.rectTransform;
        textRect.localScale = Vector3.one * popStartScaleMultiplier;

        PlaySound(countdownBeepClip, pitch);

        float elapsedTime = 0f;
        while (elapsedTime < wordDisplayDuration)
        {
            elapsedTime += Time.deltaTime;
            float blend = 1f - Mathf.Exp(-shrinkSpeed * Time.deltaTime);
            textRect.localScale = Vector3.Lerp(textRect.localScale, Vector3.one, blend);
            yield return null;
        }
        textRect.localScale = Vector3.one;
    }

    private void PlaySound(AudioClip clip, float pitch) => SFXPool.Play(clip, pitch);

    private void StartDrawPhase()
    {
        isTimerRunning = true;
        lastTickIndex = -1;
        timeRemaining = drawWindowDuration;

        if (timerText != null) timerText.gameObject.SetActive(true);
        OnDrawPhaseChanged?.Invoke(true);

        PlayerHandManager localHandManager = FindFirstObjectByType<PlayerHandManager>();
        if (localHandManager != null)
        {
            localHandManager.SetAllCardsInteractable(true);
        }
    }

    private void Update()
    {
        if (!isTimerRunning) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndDrawPhase();
            return;
        }

        float elapsed = drawWindowDuration - timeRemaining;
        ProcessTimerAudioTicks(elapsed, timeRemaining);

        float fillRatio = Mathf.Clamp01(timeRemaining / drawWindowDuration);
        UpdateTimerUI(fillRatio, timeRemaining);
    }

    private void ProcessTimerAudioTicks(float elapsedSeconds, float remaining)
    {
        bool isUrgent = remaining <= urgentThreshold;
        float interval = isUrgent ? urgentTickInterval : normalTickInterval;
        float currentPitch = isUrgent ? urgentTickPitch : normalPitch;

        int currentTickIndex = Mathf.FloorToInt(elapsedSeconds / interval);

        if (currentTickIndex > lastTickIndex)
        {
            lastTickIndex = currentTickIndex;
            AudioClip clipToPlay = timerTickClip != null ? timerTickClip : countdownBeepClip;
            PlaySound(clipToPlay, currentPitch);
        }
    }

    public void NotifyCardsDrawn() { }

    private void EndDrawPhase()
    {
        isTimerRunning = false;
        OnDrawPhaseChanged?.Invoke(false);

        if (preGamePanel != null) preGamePanel.SetActive(false);
        if (timerText != null) timerText.gameObject.SetActive(false);

        UpdateTimerUI(0f, 0f);
    }

    private void UpdateTimerUI(float fillRatio, float remaining)
    {
        if (timerText != null)
        {
            timerText.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();
        }
    }
}