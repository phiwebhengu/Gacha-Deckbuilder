using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public struct TurnResultData : INetworkSerializable
{
    public int[] playerCardIds;
    public int[] opponentCardIds;
    public int playerAttack;
    public int playerDefense;
    public int opponentAttack;
    public int opponentDefense;
    public int netDamageToPlayer;
    public int netDamageToOpponent;
    public int finalPlayerHP;
    public int finalOpponentHP;

    public int[] playerDiscardedCardIds;
    public int[] opponentDiscardedCardIds;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        int playerIdsLength = playerCardIds != null ? playerCardIds.Length : 0;
        serializer.SerializeValue(ref playerIdsLength);
        if (serializer.IsReader && playerIdsLength > 0) playerCardIds = new int[playerIdsLength];
        for (int i = 0; i < playerIdsLength; i++) serializer.SerializeValue(ref playerCardIds[i]);

        int oppIdsLength = opponentCardIds != null ? opponentCardIds.Length : 0;
        serializer.SerializeValue(ref oppIdsLength);
        if (serializer.IsReader && oppIdsLength > 0) opponentCardIds = new int[oppIdsLength];
        for (int i = 0; i < oppIdsLength; i++) serializer.SerializeValue(ref opponentCardIds[i]);

        serializer.SerializeValue(ref playerAttack);
        serializer.SerializeValue(ref playerDefense);
        serializer.SerializeValue(ref opponentAttack);
        serializer.SerializeValue(ref opponentDefense);
        serializer.SerializeValue(ref netDamageToPlayer);
        serializer.SerializeValue(ref netDamageToOpponent);
        serializer.SerializeValue(ref finalPlayerHP);
        serializer.SerializeValue(ref finalOpponentHP);

        int playerDiscardLength = playerDiscardedCardIds != null ? playerDiscardedCardIds.Length : 0;
        serializer.SerializeValue(ref playerDiscardLength);
        if (serializer.IsReader && playerDiscardLength > 0) playerDiscardedCardIds = new int[playerDiscardLength];
        for (int i = 0; i < playerDiscardLength; i++) serializer.SerializeValue(ref playerDiscardedCardIds[i]);

        int oppDiscardLength = opponentDiscardedCardIds != null ? opponentDiscardedCardIds.Length : 0;
        serializer.SerializeValue(ref oppDiscardLength);
        if (serializer.IsReader && oppDiscardLength > 0) opponentDiscardedCardIds = new int[oppDiscardLength];
        for (int i = 0; i < oppDiscardLength; i++) serializer.SerializeValue(ref opponentDiscardedCardIds[i]);
    }
}

public static class CardNetId
{
    public const int SupportOffset = 10000;
    public static int Encode(int cardId, bool isSupport) => isSupport ? cardId + SupportOffset : cardId;
    public static bool IsSupport(int netId) => netId >= SupportOffset;
    public static int Decode(int netId) => netId >= SupportOffset ? netId - SupportOffset : netId;
}

public class EndTurnManager : NetworkBehaviour
{
    [Header("Game Mode Settings")]
    [SerializeField] public bool isOfflineAIRival = false;

    [Header("Health & Points Settings")]
    [SerializeField] private int startingHP = 20;

    [Header("Network State")]
    public NetworkVariable<int> Player1_HP = new NetworkVariable<int>(20, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Player2_HP = new NetworkVariable<int>(20, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsGamePaused = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> PauseRequesterClientId = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> GameEnded = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("HP UI References")]
    [SerializeField] private TextMeshProUGUI playerHPText;
    [SerializeField] private TextMeshProUGUI opponentHPText;

    [Header("Combat Count UI References")]
    [SerializeField] private TextMeshProUGUI playerAttackText;
    [SerializeField] private TextMeshProUGUI playerDefenseText;
    [SerializeField] private TextMeshProUGUI opponentAttackText;
    [SerializeField] private TextMeshProUGUI opponentDefenseText;
    [SerializeField] private TextMeshProUGUI opponentText;

    [Header("Center Difference Display")]
    [SerializeField] private TextMeshProUGUI centerDifferenceText;
    [SerializeField] private float countStepInterval = 0.08f;
    [SerializeField] private float popScaleMultiplier = 1.35f;
    [SerializeField] private float popDuration = 0.06f;
    [SerializeField] private float cardMovementWaitDelay = 0.5f;
    [SerializeField] private float phaseTransitionPause = 0.4f;

    [Header("✨ AUDIO SETTINGS ✨")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pointCountSFX;
    [SerializeField] private float minPitch = 0.9f;
    [SerializeField] private float maxPitch = 1.8f;
    [SerializeField] private AudioClip damageSFX;
    [SerializeField] private AudioClip attackHighlightSFX;
    [SerializeField] private AudioClip defenseHighlightSFX;
    [SerializeField] private AudioClip supportHighlightSFX;

    [Header("✨ JUICE: Damage Flash UI ✨")]
    [SerializeField] private Image damageFlashImage;
    [SerializeField] private float flashInDuration = 0.05f;
    [SerializeField] private float flashOutDuration = 0.25f;
    [SerializeField][Range(0f, 1f)] private float maxFlashAlpha = 0.6f;

    [Header("✨ JUICE: Damage UI Shake Settings ✨")]
    [SerializeField] private RectTransform playerHPRectTransformOverride;
    [SerializeField] private RectTransform opponentHPRectTransformOverride;
    [SerializeField] private float damageShakeDuration = 0.15f;
    [SerializeField] private float damageShakeMagnitude = 12f;
    [SerializeField] private float damageShakeRotation = 1.5f;

    [Header("Camera Shake (Legacy / Background)")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float shakeIntensity = 0.25f;
    [SerializeField] private float shakeDuration = 0.08f;

    [Header("UI References")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private RectTransform selectedHandTransform;
    [SerializeField] private RectTransform opponentSelectedHandTransform;

    [Header("Opponent Card Display")]
    [SerializeField] private GameObject opponentCardPrefab;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private GachaUI gachaUI;

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private Button returnToLobbyButton;

    [Header("Pause & Forfeit UI")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button forfeitButton;
    [SerializeField] private Button pauseButton;

    [Header("Leave To Lobby")]
    [SerializeField] private GameObject leaveConfirmPanel;

    [Header("Scene Settings")]
    [SerializeField] private string gameType = "Game";
    [SerializeField] private string lobbySceneName = "Lobby";
    [Tooltip("Where Home / Return goes in an offline match (AI or Tutorial). There is no lobby offline.")]
    [SerializeField] private string offlineExitSceneName = "Menu";

    [Header("Match Settings")]
    [SerializeField] private int maxRounds = 10;

    [Header("Pop-up Font (Futura)")]
    [SerializeField] private TMP_FontAsset popupFont;
    [SerializeField] private float reactionPopupFontSize = 40f;
    [SerializeField] private TextMeshProUGUI reactionPopupTemplate;

    [Header("Skip Turn")]
    [SerializeField] private Button skipTurnButton;
    private bool drawPhaseSeen = false;
    private float nextSkipCheck = 0f;

    private int currentPlayerHP;
    private int currentOpponentHP;
    private Dictionary<ulong, int[]> pendingTurns = new Dictionary<ulong, int[]>();
    private bool isPlayer1 = true;

    private bool isMatchOver = false;
    private bool isResolvingCombat = false;
    private bool leavingToLobby = false;

    private bool localTurnSubmitted = false;
    private int opponentCardsReturningToHand = 0;
    private int[] pendingOwnDiscardNetIds = null;

    private readonly Dictionary<ulong, SupportMatchState> supportStates = new Dictionary<ulong, SupportMatchState>();
    private GachaManager cachedGachaManager;
    private readonly List<GachaManager.RoundEndInfo> pendingRoundEnd = new List<GachaManager.RoundEndInfo>();
    private readonly HashSet<ulong> clientsReadyForNextRound = new HashSet<ulong>();

    private readonly Dictionary<Transform, Coroutine> activePops = new Dictionary<Transform, Coroutine>();
    private readonly Dictionary<RectTransform, Vector2> restPositions = new Dictionary<RectTransform, Vector2>();
    private Coroutine activeCameraShake;
    private int offlineCurrentRound = 1;
    private bool timerRunning = true;
    private float lastPauseRequestTime = 0f;
    private const float PAUSE_COOLDOWN = 5f;

    private Vector3 originalCenterScale = Vector3.one;
    private Vector3 originalPlayerHPScale = Vector3.one;
    private Vector3 originalOpponentHPScale = Vector3.one;
    private Vector3 originalPlayerAttackScale = Vector3.one;
    private Vector3 originalPlayerDefenseScale = Vector3.one;
    private Vector3 originalOpponentAttackScale = Vector3.one;
    private Vector3 originalOpponentDefenseScale = Vector3.one;
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;

    private Coroutine activePlayerShakeCoroutine;
    private Coroutine activeOpponentShakeCoroutine;
    private Coroutine activeDamageFlashCoroutine;

    private PlayerHandManager _cachedLocalHandManager;
    private bool _hasCachedHandManager = false;

    // AI Persistent Hand & Token State
    private readonly List<int> aiPersistentHandNetIds = new List<int>();
    private int aiTokens = 10;

    // ----- Tutorial hooks (off by default) -----

    /// <summary>A play was refused (CardRules or ExtraPlayRule). The string is the message shown to the player.</summary>
    public event Action<string> OnInvalidPlay;

    /// <summary>The local player locked in these cards (network ids).</summary>
    public event Action<int[]> OnTurnSubmitted;

    /// <summary>A round's reveal finished; HP is already updated. Fires before the played cards are cleared.</summary>
    public event Action<TurnResultData> OnRoundResolved;

    /// <summary>While true, the played cards stay on the table after a round instead of clearing for the next one.</summary>
    public bool HoldAfterRound { get; set; }

    /// <summary>An extra rule checked when End Turn is pressed: return a message to refuse the play, or null to allow it.</summary>
    public Func<int[], string> ExtraPlayRule;

    /// <summary>Offline only. Given the round number, the cards the AI draws that round (network ids), or null to draw normally.</summary>
    public Func<int, int[]> ScriptedAIDraw;

    /// <summary>Offline only. Given the round number, the cards the AI plays that round (network ids), or null to choose normally.</summary>
    public Func<int, int[]> ScriptedAITurn;

    public Button EndTurnButton => endTurnButton;
    public RectTransform PlayerPlayedArea => selectedHandTransform;
    public RectTransform OpponentPlayedArea => opponentSelectedHandTransform;
    public TextMeshProUGUI PlayerHPText => playerHPText;
    public TextMeshProUGUI OpponentHPText => opponentHPText;
    public int PlayerHP => currentPlayerHP;
    public int OpponentHP => currentOpponentHP;
    private int CurrentRoundNumber => timerManager != null ? timerManager.CurrentRound : offlineCurrentRound;

    public override void OnNetworkSpawn()
    {
        isPlayer1 = IsServer;

        // BULLETPROOF: Clear any stale ready states from previous matches or role swaps
        clientsReadyForNextRound.Clear();
        pendingTurns.Clear();

        if (isOfflineAIRival)
        {
            currentPlayerHP = startingHP;
            currentOpponentHP = startingHP;
            aiTokens = 10;
            aiPersistentHandNetIds.Clear();
        }
        else
        {
            currentPlayerHP = isPlayer1 ? Player1_HP.Value : Player2_HP.Value;
            currentOpponentHP = isPlayer1 ? Player2_HP.Value : Player1_HP.Value;
        }

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }
        else
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleDisconnectedFromHost;
        }

        GameEnded.OnValueChanged += OnGameEndedChanged;
        UpdateHPUI();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
        else if (!IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleDisconnectedFromHost;
        }
        GameEnded.OnValueChanged -= OnGameEndedChanged;

        if (!IsServer) ShowMatchEndedByDisconnect("DISCONNECTED (Host left the match)");
    }

    private void OnEnable()
    {
        if (timerManager != null)
        {
            timerManager.OnDrawPhaseChanged += HandleDrawPhaseChanged;
        }
    }

    private void OnDisable()
    {
        if (timerManager != null)
        {
            timerManager.OnDrawPhaseChanged -= HandleDrawPhaseChanged;
        }
    }

    private void HandleDrawPhaseChanged(bool isDrawPhase)
    {
        if (!isDrawPhase) UpdateEndTurnButtonVisibility();

        if (isDrawPhase && !isMatchOver)
        {
            ResetForNewRound();

            if (isOfflineAIRival)
            {
                StartCoroutine(Routine_AIDrawCardsPhase());
            }
        }
    }

    private void ResetForNewRound()
    {
        localTurnSubmitted = false;
        drawPhaseSeen = false;
        ResetTurnUI();

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager != null)
        {
            localHandManager.SetAllCardsInteractable(true);
        }

        UpdateEndTurnButtonVisibility();
    }

    private IEnumerator Routine_AIDrawCardsPhase()
    {
        yield return new WaitForSeconds(0.5f);

        var deckMgr = FindFirstObjectByType<DeckManager>();
        if (deckMgr == null || deckMgr.loadedActionCards == null || deckMgr.loadedActionCards.Count == 0) yield break;

        if (gachaUI == null) gachaUI = FindFirstObjectByType<GachaUI>();

        int[] scriptedDraw = ScriptedAIDraw != null ? ScriptedAIDraw(CurrentRoundNumber) : null;
        if (scriptedDraw != null)
        {
            foreach (int netId in scriptedDraw)
            {
                aiPersistentHandNetIds.Add(netId);
                if (gachaUI != null) gachaUI.AddOpponentCardBacks(1);
                yield return new WaitForSeconds(0.4f);
            }
            yield break;
        }

        int maxPossiblePulls = Mathf.Min(3, aiTokens);
        if (maxPossiblePulls <= 0) yield break;

        int pullCount = UnityEngine.Random.Range(1, maxPossiblePulls + 1);

        for (int i = 0; i < pullCount; i++)
        {
            if (aiTokens <= 0) break;

            bool preferSupport = UnityEngine.Random.value > 0.6f && deckMgr.loadedSupportCards != null && deckMgr.loadedSupportCards.Count > 0;

            int cardId = preferSupport
                ? deckMgr.loadedSupportCards[UnityEngine.Random.Range(0, deckMgr.loadedSupportCards.Count)].Id
                : deckMgr.loadedActionCards[UnityEngine.Random.Range(0, deckMgr.loadedActionCards.Count)].Id;

            int netId = CardNetId.Encode(cardId, preferSupport);
            aiPersistentHandNetIds.Add(netId);
            aiTokens--;

            if (gachaUI != null)
            {
                gachaUI.AddOpponentCardBacks(1);
            }

            yield return new WaitForSeconds(0.4f);
        }
    }

    private void OnGameEndedChanged(bool oldVal, bool newVal)
    {
        if (newVal && !isMatchOver)
        {
            if (isResolvingCombat) return;

            isMatchOver = true;
            StopAllCoroutines();
            ResetPauseState();

            if (gameOverPanel != null) gameOverPanel.SetActive(true);
            if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(true);

            if (resultText != null && string.IsNullOrEmpty(resultText.text))
            {
                resultText.text = "GAME OVER";
            }
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (pendingTurns.ContainsKey(clientId))
        {
            pendingTurns.Remove(clientId);
        }
        clientsReadyForNextRound.Remove(clientId);

        if (NetworkManager.Singleton != null && clientId != NetworkManager.Singleton.LocalClientId)
        {
            ShowMatchEndedByDisconnect("VICTORY! (Opponent disconnected)");
        }
    }

    private void HandleDisconnectedFromHost(ulong clientId)
    {
        ShowMatchEndedByDisconnect("DISCONNECTED (Host left the match)");
    }

    private void ShowMatchEndedByDisconnect(string message)
    {
        if (isMatchOver || leavingToLobby) return;

        isMatchOver = true;
        isResolvingCombat = false;
        StopAllCoroutines();
        ResetPauseState();

        if (IsServer && IsSpawned) GameEnded.Value = true;

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = message;
        }

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
        if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(true);
    }

    private void Start()
    {
        if (isOfflineAIRival || !IsSpawned)
        {
            currentPlayerHP = startingHP;
            currentOpponentHP = startingHP;
            aiTokens = 10;
            aiPersistentHandNetIds.Clear();
        }

        if (isOfflineAIRival)
        {
            if (cachedGachaManager == null) cachedGachaManager = FindFirstObjectByType<GachaManager>();
            if (cachedGachaManager != null) cachedGachaManager.SetOfflineOpponentHand(aiPersistentHandNetIds);
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null)
        {
            originalCamPos = mainCamera.transform.localPosition;
            originalCamRot = mainCamera.transform.localRotation;
        }

        if (damageFlashImage != null)
        {
            Color c = damageFlashImage.color;
            c.a = 0f;
            damageFlashImage.color = c;
            damageFlashImage.gameObject.SetActive(true);
        }

        CacheAndResetUI();
        UpdateEndTurnButtonVisibility();
        ClearOpponentPanel();

        offlineCurrentRound = 1;
        SetupSkipButton();
    }

    private void CacheAndResetUI()
    {
        if (centerDifferenceText != null)
        {
            originalCenterScale = centerDifferenceText.transform.localScale;
            centerDifferenceText.text = "";
        }
        if (playerHPText != null) originalPlayerHPScale = playerHPText.transform.localScale;
        if (opponentHPText != null) originalOpponentHPScale = opponentHPText.transform.localScale;
        if (playerAttackText != null) { originalPlayerAttackScale = playerAttackText.transform.localScale; playerAttackText.text = "0"; }
        if (playerDefenseText != null) { originalPlayerDefenseScale = playerDefenseText.transform.localScale; playerDefenseText.text = "0"; }
        if (opponentAttackText != null) { originalOpponentAttackScale = opponentAttackText.transform.localScale; opponentAttackText.text = "0"; }
        if (opponentDefenseText != null) { originalOpponentDefenseScale = opponentDefenseText.transform.localScale; opponentDefenseText.text = "0"; }

        UpdateHPUI();
    }

    private void ClearOpponentPanel()
    {
        if (opponentSelectedHandTransform != null)
        {
            BalatroCardController[] oppCards = opponentSelectedHandTransform.GetComponentsInChildren<BalatroCardController>();
            foreach (var card in oppCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
        }
    }

    private PlayerHandManager GetLocalHandManager()
    {
        if (_hasCachedHandManager && _cachedLocalHandManager != null)
            return _cachedLocalHandManager;

        // Use NetworkManager's actual client ID index instead of just IsServer check
        bool localIsPlayer1 = true;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            var allClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
            allClients.Sort();
            localIsPlayer1 = allClients.Count > 0 && NetworkManager.Singleton.LocalClientId == allClients[0];
        }
        else
        {
            localIsPlayer1 = IsServer;
        }

        int myPlayerSlot = localIsPlayer1 ? 1 : 2;
        PlayerSlot[] allSlots = FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None);

        foreach (PlayerSlot slotObj in allSlots)
        {
            if (slotObj.slot == myPlayerSlot)
            {
                _cachedLocalHandManager = slotObj.GetComponentInChildren<PlayerHandManager>();
                if (_cachedLocalHandManager != null)
                {
                    _hasCachedHandManager = true;
                    return _cachedLocalHandManager;
                }
            }
        }
        _cachedLocalHandManager = FindFirstObjectByType<PlayerHandManager>();
        _hasCachedHandManager = true;
        return _cachedLocalHandManager;
    }

    public bool IsMatchOver => isMatchOver || (IsSpawned && GameEnded.Value);

    public void UpdateEndTurnButtonVisibility()
    {
        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || endTurnButton == null) return;

        // Cards can be picked while the draw window is open, but the turn is only locked in after it closes.
        bool drawWindowOpen = timerManager != null && timerManager.IsDrawPhaseActive;
        int selectedCount = localHandManager.GetSelectedCards().Count;
        endTurnButton.gameObject.SetActive(selectedCount > 0 && !localTurnSubmitted && !drawWindowOpen);
    }

    private void SetupSkipButton()
    {
        if (skipTurnButton == null && endTurnButton != null)
        {
            skipTurnButton = Instantiate(endTurnButton, endTurnButton.transform.parent);
            skipTurnButton.name = "SkipTurnButton";
            skipTurnButton.onClick = new Button.ButtonClickedEvent();

            TMP_Text label = skipTurnButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "Skip Turn";

            RectTransform rt = skipTurnButton.GetComponent<RectTransform>();
            rt.anchoredPosition += new Vector2(-(rt.sizeDelta.x + 30f), 0f);
        }

        if (skipTurnButton != null)
        {
            skipTurnButton.onClick.RemoveListener(OnSkipTurnClicked);
            skipTurnButton.onClick.AddListener(OnSkipTurnClicked);
            skipTurnButton.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (skipTurnButton == null) return;

        if (timerManager != null && timerManager.IsDrawPhaseActive) drawPhaseSeen = true;

        if (Time.unscaledTime < nextSkipCheck) return;
        nextSkipCheck = Time.unscaledTime + 0.25f;

        bool show = (IsSpawned || isOfflineAIRival) && !isMatchOver && !localTurnSubmitted && !isResolvingCombat
                    && drawPhaseSeen && timerManager != null && !timerManager.IsDrawPhaseActive
                    && LocalPlayerHasNoPlayableCard();

        if (skipTurnButton.gameObject.activeSelf != show) skipTurnButton.gameObject.SetActive(show);
    }

    private bool LocalPlayerHasNoPlayableCard()
    {
        DeckManager deck = FindFirstObjectByType<DeckManager>();
        if (deck == null || deck.loadedActionCards == null) return false;

        foreach (BalatroCardController card in FindObjectsByType<BalatroCardController>(FindObjectsSortMode.None))
        {
            if (card == null || !card.IsInteractable) continue;
            if (selectedHandTransform != null && card.transform.IsChildOf(selectedHandTransform)) continue;
            if (opponentSelectedHandTransform != null && card.transform.IsChildOf(opponentSelectedHandTransform)) continue;

            if (card.Category == CardCategory.Support) return false;

            ActionCardData data = deck.loadedActionCards.Find(c => c.Id == card.CardId);
            if (data == null) return false;

            string cat = (data.Category ?? "").Trim();
            bool needsPartner = cat.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
                             || cat.Equals("Artefact", StringComparison.OrdinalIgnoreCase);
            if (!needsPartner) return false;
        }
        return true;
    }

    public void OnSkipTurnClicked()
    {
        if (localTurnSubmitted || isMatchOver) return;

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || !LocalPlayerHasNoPlayableCard()) return;

        foreach (BalatroCardController c in localHandManager.GetSelectedCards()) c.ForceDeselect();

        localTurnSubmitted = true;
        drawPhaseSeen = false;
        localHandManager.SetAllCardsInteractable(false);

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
        if (skipTurnButton != null) skipTurnButton.gameObject.SetActive(false);

        if (isOfflineAIRival)
        {
            ResolveOfflineAITurn(Array.Empty<int>());
        }
        else
        {
            SubmitTurnServerRpc(Array.Empty<int>());
        }
    }

    public void OnEndTurnClicked()
    {
        if (timerManager != null && timerManager.IsDrawPhaseActive) return;

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || selectedHandTransform == null) return;

        int[] selectedIds = localHandManager.GetSelectedCardIds().ToArray();
        if (selectedIds.Length == 0)
        {
            Debug.LogWarning("[EndTurnManager] No selected card IDs found! Aborting turn.");
            return;
        }

        DeckManager rulesDeck = FindFirstObjectByType<DeckManager>();
        if (rulesDeck != null && !CardRules.IsPlayAllowed(selectedIds, rulesDeck.loadedActionCards, out string invalidReason))
        {
            ShowInvalidPlayMessage(invalidReason);
            return;
        }

        string extraRuleMessage = ExtraPlayRule != null ? ExtraPlayRule(selectedIds) : null;
        if (!string.IsNullOrEmpty(extraRuleMessage))
        {
            ShowInvalidPlayMessage(extraRuleMessage);
            return;
        }

        localTurnSubmitted = true;
        drawPhaseSeen = false;
        if (skipTurnButton != null) skipTurnButton.gameObject.SetActive(false);
        localHandManager.SetAllCardsInteractable(false);
        localHandManager.SubmitSelectedCardsToHand(selectedHandTransform);

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);

        OnTurnSubmitted?.Invoke(selectedIds);

        if (isOfflineAIRival)
        {
            ResolveOfflineAITurn(selectedIds);
        }
        else
        {
            SubmitTurnServerRpc(selectedIds);
        }
    }

    private void ResolveOfflineAITurn(int[] playerSelectedIds)
    {
        var deckMgr = FindFirstObjectByType<DeckManager>();

        if (aiPersistentHandNetIds.Count == 0 && deckMgr != null && deckMgr.loadedActionCards != null && deckMgr.loadedActionCards.Count > 0 && aiTokens > 0)
        {
            int randId = deckMgr.loadedActionCards[UnityEngine.Random.Range(0, deckMgr.loadedActionCards.Count)].Id;
            aiPersistentHandNetIds.Add(CardNetId.Encode(randId, false));
            aiTokens--;
        }

        int[] opponentCards = ChooseAICards(deckMgr);

        if (gachaUI == null) gachaUI = FindFirstObjectByType<GachaUI>();
        if (gachaUI != null)
        {
            gachaUI.RemoveOpponentCardBacks(opponentCards.Length);
        }

        SupportEffectResolver.Resolve(
            playerSelectedIds, opponentCards, deckMgr,
            GetSupportState(GachaManager.OfflinePlayerId), GetSupportState(GachaManager.OfflineOpponentId),
            out SideOutcome outPlayer, out SideOutcome outOpponent);

        // Like the player's, the AI's Action and Forever cards go back to its hand; spent and discarded ones leave.
        foreach (PlayedSupportInfo played in outOpponent.PlayedSupports)
            if (played.Spent) aiPersistentHandNetIds.Remove(played.NetId);
        foreach (int discarded in outOpponent.DiscardedNetIds)
            aiPersistentHandNetIds.Remove(discarded);

        GrantTokens(GachaManager.OfflinePlayerId, outPlayer.TokenReward);
        aiTokens += Mathf.Max(0, outOpponent.TokenReward);

        pendingRoundEnd.Clear();
        pendingRoundEnd.Add(new GachaManager.RoundEndInfo { ClientId = GachaManager.OfflinePlayerId, Outcome = outPlayer });
        pendingRoundEnd.Add(new GachaManager.RoundEndInfo { ClientId = GachaManager.OfflineOpponentId, Outcome = outOpponent });

        int finalPlayerHP = Mathf.Max(0, currentPlayerHP - outOpponent.DamageDealt);
        int finalOpponentHP = Mathf.Max(0, currentOpponentHP - outPlayer.DamageDealt);

        var result = new TurnResultData
        {
            playerCardIds = playerSelectedIds,
            opponentCardIds = opponentCards,
            playerAttack = outPlayer.Attack,
            playerDefense = outPlayer.Defense,
            opponentAttack = outOpponent.Attack,
            opponentDefense = outOpponent.Defense,
            netDamageToPlayer = outOpponent.DamageDealt,
            netDamageToOpponent = outPlayer.DamageDealt,
            finalPlayerHP = finalPlayerHP,
            finalOpponentHP = finalOpponentHP,
            playerDiscardedCardIds = outPlayer.DiscardedNetIds.ToArray(),
            opponentDiscardedCardIds = outOpponent.DiscardedNetIds.ToArray()
        };

        StartCoroutine(Routine_ResolveCombatSequence(result));
    }

    /// <summary>One or two random cards from the AI's hand that the card rules allow together (a Weapon needs a Character...).</summary>
    private int[] ChooseAICards(DeckManager deckMgr)
    {
        int[] scriptedTurn = ScriptedAITurn != null ? ScriptedAITurn(CurrentRoundNumber) : null;
        if (scriptedTurn != null)
        {
            var available = new List<int>(aiPersistentHandNetIds);
            var play = new List<int>();
            foreach (int netId in scriptedTurn)
            {
                if (available.Remove(netId)) play.Add(netId);
            }
            return play.ToArray();
        }

        List<ActionCardData> actionCards = deckMgr != null ? deckMgr.loadedActionCards : null;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            int count = Mathf.Min(UnityEngine.Random.Range(1, 3), aiPersistentHandNetIds.Count);
            var pool = new List<int>(aiPersistentHandNetIds);
            var pick = new List<int>();

            for (int i = 0; i < count; i++)
            {
                int index = UnityEngine.Random.Range(0, pool.Count);
                pick.Add(pool[index]);
                pool.RemoveAt(index);
            }

            if (pick.Count > 0 && CardRules.IsPlayAllowed(pick, actionCards, out _)) return pick.ToArray();
        }

        foreach (int netId in aiPersistentHandNetIds)
        {
            if (CardRules.IsPlayAllowed(new[] { netId }, actionCards, out _)) return new[] { netId };
        }

        return Array.Empty<int>();
    }

    [Header("Invalid Play Message")]
    [SerializeField] private TextMeshProUGUI invalidPlayText;
    [SerializeField] private float invalidPlayMessageSeconds = 3f;
    private Coroutine invalidPlayRoutine;

    private void ShowInvalidPlayMessage(string message)
    {
        OnInvalidPlay?.Invoke(message);

        if (invalidPlayText == null)
        {
            if (endTurnButton == null) { Debug.LogWarning("[EndTurnManager] " + message); return; }

            var go = new GameObject("InvalidPlayText", typeof(RectTransform));
            go.transform.SetParent(endTurnButton.transform.parent, false);
            invalidPlayText = go.AddComponent<TextMeshProUGUI>();
            invalidPlayText.alignment = TextAlignmentOptions.Center;
            invalidPlayText.fontSize = 34f;
            invalidPlayText.fontStyle = FontStyles.Bold;
            invalidPlayText.color = new Color(1f, 0.35f, 0.3f, 1f);
            invalidPlayText.raycastTarget = false;

            var rt = invalidPlayText.rectTransform;
            RectTransform buttonRt = endTurnButton.GetComponent<RectTransform>();
            rt.anchorMin = buttonRt.anchorMin;
            rt.anchorMax = buttonRt.anchorMax;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(900f, 90f);
            rt.anchoredPosition = buttonRt.anchoredPosition + new Vector2(0f, buttonRt.sizeDelta.y * 0.5f + 20f);
        }

        if (popupFont != null) invalidPlayText.font = popupFont;
        invalidPlayText.text = message;
        invalidPlayText.gameObject.SetActive(true);
        invalidPlayText.transform.SetAsLastSibling();

        if (invalidPlayRoutine != null) StopCoroutine(invalidPlayRoutine);
        invalidPlayRoutine = StartCoroutine(Routine_HideInvalidPlayMessage());
    }

    private IEnumerator Routine_HideInvalidPlayMessage()
    {
        yield return new WaitForSecondsRealtime(invalidPlayMessageSeconds);
        if (invalidPlayText != null) invalidPlayText.gameObject.SetActive(false);
        invalidPlayRoutine = null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitTurnServerRpc(int[] selectedCardIds, ServerRpcParams rpcParams = default)
    {
        if (isMatchOver || GameEnded.Value) return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (pendingTurns.ContainsKey(clientId)) return;

        pendingTurns[clientId] = selectedCardIds ?? Array.Empty<int>();

        var others = new List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            if (id != clientId) others.Add(id);

        if (others.Count > 0)
        {
            OpponentPlayedCardsClientRpc(pendingTurns[clientId].Length, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = others.ToArray() }
            });
        }

        if (pendingTurns.Count >= 2)
        {
            ResolveAndBroadcastCombat();
        }
    }

    [ClientRpc]
    private void OpponentPlayedCardsClientRpc(int cardCount, ClientRpcParams rpcParams = default)
    {
        foreach (GachaUI ui in FindObjectsByType<GachaUI>(FindObjectsSortMode.None))
            ui.RemoveOpponentCardBacks(cardCount);
    }

    private void ResolveAndBroadcastCombat()
    {
        ulong[] keys = pendingTurns.Keys.ToArray();
        ulong idA = keys[0];
        ulong idB = keys[1];

        int[] cardsA = pendingTurns[idA];
        int[] cardsB = pendingTurns[idB];

        bool aIsPlayer1 = IsPlayer1Id(idA);
        var deckMgr = FindFirstObjectByType<DeckManager>();

        SupportEffectResolver.Resolve(
            cardsA, cardsB, deckMgr,
            GetSupportState(idA), GetSupportState(idB),
            out SideOutcome outA, out SideOutcome outB);

        int currentHpA = aIsPlayer1 ? Player1_HP.Value : Player2_HP.Value;
        int currentHpB = aIsPlayer1 ? Player2_HP.Value : Player1_HP.Value;

        int finalHpA = Mathf.Max(0, currentHpA - outB.DamageDealt);
        int finalHpB = Mathf.Max(0, currentHpB - outA.DamageDealt);

        if (aIsPlayer1)
        {
            Player1_HP.Value = finalHpA;
            Player2_HP.Value = finalHpB;
        }
        else
        {
            Player2_HP.Value = finalHpA;
            Player1_HP.Value = finalHpB;
        }

        var resultA = new TurnResultData
        {
            playerCardIds = cardsA,
            opponentCardIds = cardsB,
            playerAttack = outA.Attack,
            playerDefense = outA.Defense,
            opponentAttack = outB.Attack,
            opponentDefense = outB.Defense,
            netDamageToPlayer = outB.DamageDealt,
            netDamageToOpponent = outA.DamageDealt,
            finalPlayerHP = finalHpA,
            finalOpponentHP = finalHpB,
            playerDiscardedCardIds = outA.DiscardedNetIds.ToArray(),
            opponentDiscardedCardIds = outB.DiscardedNetIds.ToArray()
        };

        var resultB = new TurnResultData
        {
            playerCardIds = cardsB,
            opponentCardIds = cardsA,
            playerAttack = outB.Attack,
            playerDefense = outB.Defense,
            opponentAttack = outA.Attack,
            opponentDefense = outA.Defense,
            netDamageToPlayer = outA.DamageDealt,
            netDamageToOpponent = outB.DamageDealt,
            finalPlayerHP = finalHpB,
            finalOpponentHP = finalHpA,
            playerDiscardedCardIds = outB.DiscardedNetIds.ToArray(),
            opponentDiscardedCardIds = outA.DiscardedNetIds.ToArray()
        };

        SendResultToClient(idA, resultA);
        SendResultToClient(idB, resultB);

        GrantTokens(idA, outA.TokenReward);
        GrantTokens(idB, outB.TokenReward);

        pendingRoundEnd.Clear();
        pendingRoundEnd.Add(new GachaManager.RoundEndInfo { ClientId = idA, Outcome = outA });
        pendingRoundEnd.Add(new GachaManager.RoundEndInfo { ClientId = idB, Outcome = outB });

        pendingTurns.Clear();
    }

    private SupportMatchState GetSupportState(ulong clientId)
    {
        if (!supportStates.TryGetValue(clientId, out SupportMatchState state))
        {
            state = new SupportMatchState();
            supportStates[clientId] = state;
        }
        return state;
    }

    private void GrantTokens(ulong clientId, int amount)
    {
        if (amount <= 0) return;
        if (cachedGachaManager == null) cachedGachaManager = FindFirstObjectByType<GachaManager>();
        if (cachedGachaManager != null) cachedGachaManager.AddTokens(clientId, amount);
    }

    private void ApplyGachaRoundEnd()
    {
        if (pendingRoundEnd.Count == 0) return;
        if (cachedGachaManager == null) cachedGachaManager = FindFirstObjectByType<GachaManager>();
        if (cachedGachaManager != null) cachedGachaManager.ApplyRoundEnd(pendingRoundEnd);
        pendingRoundEnd.Clear();
    }

    private bool IsPlayer1Id(ulong clientId)
    {
        var allClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        allClients.Sort();
        return clientId == allClients[0];
    }

    private void SendResultToClient(ulong clientId, TurnResultData result)
    {
        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        ResolveCombatClientRpc(result, targetParams);
    }

    [ClientRpc]
    private void ResolveCombatClientRpc(TurnResultData result, ClientRpcParams rpcParams = default)
    {
        StartCoroutine(Routine_ResolveCombatSequence(result));
    }

    private IEnumerator Routine_ResolveCombatSequence(TurnResultData result)
    {
        isResolvingCombat = true;
        pendingOwnDiscardNetIds = result.playerDiscardedCardIds;
        opponentCardsReturningToHand = CountOpponentCardsReturningToHand(result.opponentCardIds, result.opponentDiscardedCardIds);
        yield return new WaitForSeconds(cardMovementWaitDelay);

        List<BalatroCardController> playerCards = GetControllersFromTransform(selectedHandTransform);
        List<BalatroCardController> opponentCards = InstantiateOpponentCards(result.opponentCardIds);

        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Attack);
        PlayHighlightSFX(CardCategory.Attack);
        ShowReactionPopups(result, true);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerAttack, playerAttackText, originalPlayerAttackScale,
            result.opponentAttack, opponentAttackText, originalOpponentAttackScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Defense);
        PlayHighlightSFX(CardCategory.Defense);
        ShowReactionPopups(result, false);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerDefense, playerDefenseText, originalPlayerDefenseScale,
            result.opponentDefense, opponentDefenseText, originalOpponentDefenseScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        int maxNetDamage = Mathf.Max(result.netDamageToOpponent, result.netDamageToPlayer);
        if (centerDifferenceText != null && maxNetDamage > 0)
        {
            centerDifferenceText.text = "0";
            for (int i = 1; i <= maxNetDamage; i++)
            {
                centerDifferenceText.text = i.ToString();
                PopText(centerDifferenceText.transform, originalCenterScale);
                yield return new WaitForSeconds(countStepInterval);
            }
        }
        yield return new WaitForSeconds(phaseTransitionPause);

        if (result.netDamageToPlayer > 0)
        {
            RectTransform targetTargetRect = playerHPRectTransformOverride != null
                ? playerHPRectTransformOverride
                : (playerHPText != null ? playerHPText.rectTransform : null);

            if (activeDamageFlashCoroutine != null) StopCoroutine(activeDamageFlashCoroutine);
            activeDamageFlashCoroutine = StartCoroutine(Routine_DamageFlash());

            for (int i = 1; i <= result.netDamageToPlayer; i++)
            {
                currentPlayerHP = Mathf.Max(0, currentPlayerHP - 1);
                UpdateHPUI();
                PlayFixedDamageSFX();

                if (playerHPText != null) PopText(playerHPText.transform, originalPlayerHPScale);

                if (targetTargetRect != null)
                {
                    if (activePlayerShakeCoroutine != null)
                    {
                        StopCoroutine(activePlayerShakeCoroutine);
                        targetTargetRect.localRotation = Quaternion.identity;
                    }
                    activePlayerShakeCoroutine = StartCoroutine(Routine_UIRectShake(targetTargetRect, damageShakeDuration, damageShakeMagnitude, damageShakeRotation, () => activePlayerShakeCoroutine = null));
                }

                ShakeCamera();
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        if (result.netDamageToOpponent > 0)
        {
            RectTransform targetTargetRect = opponentHPRectTransformOverride != null
                ? opponentHPRectTransformOverride
                : (opponentHPText != null ? opponentHPText.rectTransform : null);

            for (int i = 1; i <= result.netDamageToOpponent; i++)
            {
                currentOpponentHP = Mathf.Max(0, currentOpponentHP - 1);
                UpdateHPUI();
                PlayFixedDamageSFX();

                if (opponentHPText != null) PopText(opponentHPText.transform, originalOpponentHPScale);

                if (targetTargetRect != null)
                {
                    if (activeOpponentShakeCoroutine != null)
                    {
                        StopCoroutine(activeOpponentShakeCoroutine);
                        targetTargetRect.localRotation = Quaternion.identity;
                    }
                    activeOpponentShakeCoroutine = StartCoroutine(Routine_UIRectShake(targetTargetRect, damageShakeDuration, damageShakeMagnitude, damageShakeRotation, () => activeOpponentShakeCoroutine = null));
                }

                ShakeCamera();
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        currentPlayerHP = result.finalPlayerHP;
        currentOpponentHP = result.finalOpponentHP;
        UpdateHPUI();

        MatchStats.RecordRound(result.netDamageToOpponent, result.netDamageToPlayer);

        OnRoundResolved?.Invoke(result);
        while (HoldAfterRound) yield return null;

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(Routine_CheckNextRoundOrEndGame());
        isResolvingCombat = false;
    }

    private void ShowReactionPopups(TurnResultData result, bool attackPhase)
    {
        DeckManager deck = FindFirstObjectByType<DeckManager>();
        if (deck == null) return;

        SpawnReactionPopups(result.playerCardIds, false, attackPhase, playerAttackText, playerDefenseText);
        SpawnReactionPopups(result.opponentCardIds, true, attackPhase, opponentAttackText, opponentDefenseText);
    }

    private float reactionPopupFreeAt = 0f;

    private void SpawnReactionPopups(int[] netIds, bool isOpponent, bool attackPhase, TextMeshProUGUI attackText, TextMeshProUGUI defenseText)
    {
        if (netIds == null) return;

        var elements = new HashSet<string>();
        foreach (int netId in netIds)
        {
            if (CardNetId.IsSupport(netId)) continue;
            string e = CardRules.ElementOf(CardNetId.Decode(netId));
            if (e != null) elements.Add(e);
        }

        int shown = 0;
        foreach (ElementalReactions.Reaction r in ElementalReactions.Find(elements))
        {
            int amount = attackPhase ? r.Attack : r.Defense;
            if (amount <= 0) continue;

            string who = (reactionPopupTemplate != null && isOpponent) ? "Opponent: " : "";
            string label = who + r.Name + " +" + amount + (attackPhase ? " ATK" : " DEF");
            Color color = attackPhase ? new Color(1f, 0.4f, 0.5f, 1f) : new Color(0.4f, 0.65f, 1f, 1f);

            float delay, lifetime;
            if (reactionPopupTemplate != null)
            {
                float start = Mathf.Max(Time.time, reactionPopupFreeAt);
                delay = start - Time.time;
                reactionPopupFreeAt = start + 1.1f;
                lifetime = 1.0f;
            }
            else
            {
                delay = shown * 0.35f;
                lifetime = 1.3f;
            }

            StartCoroutine(ReactionPopup.Routine(attackPhase ? attackText : defenseText, label, color,
                                                 popupFont, delay, reactionPopupFontSize,
                                                 lifetime: lifetime, template: reactionPopupTemplate));
            shown++;
        }
    }

    private void PlayHighlightSFX(CardCategory category)
    {
        AudioClip clipToPlay = category switch
        {
            CardCategory.Attack => attackHighlightSFX,
            CardCategory.Defense => defenseHighlightSFX,
            CardCategory.Support => supportHighlightSFX,
            _ => null
        };
        SFXPool.Play(clipToPlay, 1f);
    }

    private void PlayFixedDamageSFX() => SFXPool.Play(damageSFX, 1f);

    private void HighlightCardsByCategory(List<BalatroCardController> playerList, List<BalatroCardController> opponentList, CardCategory category)
    {
        foreach (var card in playerList) card.HighlightCardForCategory(category);
        foreach (var card in opponentList) card.HighlightCardForCategory(category);
    }

    private void ResetCardHighlights(List<BalatroCardController> playerList, List<BalatroCardController> opponentList)
    {
        foreach (var card in playerList) card.ResetHighlight();
        foreach (var card in opponentList) card.ResetHighlight();
    }

    private int CountOpponentCardsReturningToHand(int[] opponentCardIds, int[] opponentDiscardedIds)
    {
        if (opponentCardIds == null) return 0;

        var deckMgr = FindFirstObjectByType<DeckManager>();
        int returning = 0;

        foreach (int netId in opponentCardIds)
        {
            if (!CardNetId.IsSupport(netId))
            {
                returning++;
                continue;
            }

            int id = CardNetId.Decode(netId);
            var supportCard = deckMgr != null ? deckMgr.loadedSupportCards?.Find(c => c.Id == id) : null;
            if (supportCard != null && supportCard.EffectType == "Forever") returning++;
        }

        if (opponentDiscardedIds != null) returning = Mathf.Max(0, returning - opponentDiscardedIds.Length);
        return returning;
    }

    private List<BalatroCardController> InstantiateOpponentCards(int[] opponentCardIds)
    {
        List<BalatroCardController> opponentCards = new List<BalatroCardController>();
        if (opponentCardIds == null || opponentSelectedHandTransform == null) return opponentCards;

        var deckMgr = FindFirstObjectByType<DeckManager>();
        PlayerHandManager anyHandManager = FindFirstObjectByType<PlayerHandManager>();
        if (deckMgr == null || anyHandManager == null || anyHandManager.cardPrefab == null) return opponentCards;

        foreach (int netCardId in opponentCardIds)
        {
            bool isSupportCard = CardNetId.IsSupport(netCardId);
            int cardId = CardNetId.Decode(netCardId);

            GameObject newCardObj = Instantiate(anyHandManager.cardPrefab, opponentSelectedHandTransform);
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponentInChildren<CardVisual>();

            if (controller != null && visual != null)
            {
                controller.SetCardId(cardId);
                controller.SetInteractable(false);

                var actionCard = isSupportCard ? null : deckMgr.loadedActionCards?.Find(c => c.Id == cardId);
                if (actionCard != null)
                {
                    Sprite sprite = GetCardSprite(cardId, true);
                    visual.Setup(actionCard, sprite);
                    controller.SetCategory(actionCard.Role == "Attack" ? CardCategory.Attack : CardCategory.Defense);
                }
                else
                {
                    var supportCard = deckMgr.loadedSupportCards?.Find(c => c.Id == cardId);
                    if (supportCard != null)
                    {
                        Sprite sprite = GetCardSprite(cardId, false);
                        visual.Setup(supportCard, sprite);
                        controller.SetCategory(CardCategory.Support);
                        controller.SetCardMetadata(supportCard.EffectType == "Forever");
                    }
                }
                opponentCards.Add(controller);
            }
        }
        return opponentCards;
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard) => PlayerHandManager.GetCardSpriteCached(cardId, isActionCard);

    private List<BalatroCardController> GetControllersFromTransform(RectTransform container)
    {
        List<BalatroCardController> list = new List<BalatroCardController>();
        if (container == null) return list;
        foreach (BalatroCardController ctrl in container.GetComponentsInChildren<BalatroCardController>())
            if (ctrl != null) list.Add(ctrl);
        return list;
    }

    private IEnumerator Routine_CountUpPair(int playerTargetVal, TextMeshProUGUI playerText, Vector3 playerScale, int opponentTargetVal, TextMeshProUGUI opponentText, Vector3 opponentScale)
    {
        int maxSteps = Mathf.Max(playerTargetVal, opponentTargetVal);
        if (maxSteps <= 0) yield break;

        for (int step = 1; step <= maxSteps; step++)
        {
            float t = maxSteps > 1 ? (float)(step - 1) / (maxSteps - 1) : 1f;
            float currentPitch = Mathf.Lerp(minPitch, maxPitch, t);

            SFXPool.Play(pointCountSFX, currentPitch);

            if (step <= playerTargetVal && playerText != null)
            {
                playerText.text = step.ToString();
                PopText(playerText.transform, playerScale);
            }
            if (step <= opponentTargetVal && opponentText != null)
            {
                opponentText.text = step.ToString();
                PopText(opponentText.transform, opponentScale);
            }

            yield return new WaitForSeconds(countStepInterval);
        }

        if (audioSource != null) audioSource.pitch = 1.0f;
    }

    private IEnumerator Routine_CheckNextRoundOrEndGame()
    {
        if (currentOpponentHP <= 0 && currentPlayerHP <= 0)
        {
            TriggerLocalGameOver(false, true);
            yield break;
        }
        if (currentOpponentHP <= 0)
        {
            TriggerLocalGameOver(true, false);
            yield break;
        }
        if (currentPlayerHP <= 0)
        {
            TriggerLocalGameOver(false, false);
            yield break;
        }

        int activeRound = timerManager != null ? timerManager.CurrentRound : offlineCurrentRound;
        if (activeRound >= maxRounds)
        {
            EvaluateWinnerByHP();
            yield break;
        }

        yield return StartCoroutine(Routine_ClearAllSubmittedCards());

        if (timerManager != null)
        {
            if (isOfflineAIRival)
            {
                ApplyGachaRoundEnd();
                offlineCurrentRound = activeRound + 1;
                timerManager.RequestStartRound(offlineCurrentRound);
            }
            else
            {
                // Unlocks local state immediately after combat animation finishes
                ResetForNewRound();
                ReadyForNextRoundServerRpc();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReadyForNextRoundServerRpc(ServerRpcParams rpcParams = default)
    {
        if (GameEnded.Value || !IsServer) return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        clientsReadyForNextRound.Add(clientId);

        int connectedClientCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;

        // If for any reason the count exceeds connected clients (e.g., reconnect/role swap), sanitize it
        if (clientsReadyForNextRound.Count > connectedClientCount)
        {
            clientsReadyForNextRound.Clear();
            clientsReadyForNextRound.Add(clientId);
        }

        if (connectedClientCount > 0 && clientsReadyForNextRound.Count >= connectedClientCount)
        {
            clientsReadyForNextRound.Clear();
            ApplyGachaRoundEnd();

            if (timerManager != null)
            {
                int nextRound = timerManager.CurrentRound + 1;
                timerManager.RequestStartRound(nextRound);
            }
        }
    }

    private void EvaluateWinnerByHP()
    {
        if (currentPlayerHP > currentOpponentHP) TriggerLocalGameOver(true, false);
        else if (currentOpponentHP > currentPlayerHP) TriggerLocalGameOver(false, false);
        else TriggerLocalGameOver(false, true);
    }

    private void TriggerLocalGameOver(bool isPlayerWinner, bool isDraw)
    {
        isMatchOver = true;
        isResolvingCombat = false;

        if (!isOfflineAIRival)
        {
            if (IsServer) GameEnded.Value = true;
            else NotifyServerGameOverServerRpc();
        }

        StopAllCoroutines();

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = isDraw ? "DRAW" : (isPlayerWinner ? "VICTORY!" : "DEFEAT");
        }
        if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(true);
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyServerGameOverServerRpc(ServerRpcParams rpcParams = default)
    {
        isMatchOver = true;
        GameEnded.Value = true;
    }

    private void ResetTurnUI()
    {
        if (playerAttackText != null) playerAttackText.text = "0";
        if (playerDefenseText != null) playerDefenseText.text = "0";
        if (opponentAttackText != null) opponentAttackText.text = "0";
        if (opponentDefenseText != null) opponentDefenseText.text = "0";
        if (centerDifferenceText != null) centerDifferenceText.text = "";

        drawPhaseSeen = false;
        if (skipTurnButton != null) skipTurnButton.gameObject.SetActive(false);
    }

    private IEnumerator Routine_ClearAllSubmittedCards()
    {
        if (selectedHandTransform != null)
        {
            PlayerHandManager localHandManager = GetLocalHandManager();
            if (localHandManager != null)
            {
                localHandManager.DiscardSubmittedImmediateSupports();
                localHandManager.DiscardSubmittedCardsByNetId(pendingOwnDiscardNetIds);
                pendingOwnDiscardNetIds = null;
                localHandManager.ReturnSubmittedCardsToHand(selectedHandTransform);
            }
            yield return new WaitForSeconds(0.3f);
        }

        if (opponentSelectedHandTransform != null)
        {
            foreach (var card in opponentSelectedHandTransform.GetComponentsInChildren<BalatroCardController>())
            {
                if (card != null) Destroy(card.gameObject);
            }
        }

        if (opponentCardsReturningToHand > 0)
        {
            foreach (GachaUI ui in FindObjectsByType<GachaUI>(FindObjectsSortMode.None))
                ui.AddOpponentCardBacks(opponentCardsReturningToHand);
            opponentCardsReturningToHand = 0;
        }

        yield return null;
    }

    private void PopText(Transform textTransform, Vector3 baseScale)
    {
        if (textTransform == null) return;
        if (activePops.TryGetValue(textTransform, out Coroutine running) && running != null) StopCoroutine(running);
        textTransform.localScale = baseScale;
        activePops[textTransform] = StartCoroutine(Routine_PopText(textTransform, baseScale));
    }

    private void ShakeCamera()
    {
        if (mainCamera == null) return;
        if (activeCameraShake != null) StopCoroutine(activeCameraShake);
        mainCamera.transform.localPosition = originalCamPos;
        activeCameraShake = StartCoroutine(Routine_CameraShake());
    }

    private IEnumerator Routine_PopText(Transform textTransform, Vector3 baseScale)
    {
        Vector3 targetScale = baseScale * popScaleMultiplier;
        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            textTransform.localScale = Vector3.Lerp(baseScale, targetScale, elapsed / popDuration);
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            textTransform.localScale = Vector3.Lerp(targetScale, baseScale, elapsed / popDuration);
            yield return null;
        }
        textTransform.localScale = baseScale;
    }

    private IEnumerator Routine_DamageFlash()
    {
        if (damageFlashImage == null) yield break;
        Color flashColor = damageFlashImage.color;
        float elapsed = 0f;

        while (elapsed < flashInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            flashColor.a = Mathf.Lerp(0f, maxFlashAlpha, elapsed / flashInDuration);
            damageFlashImage.color = flashColor;
            yield return null;
        }

        flashColor.a = maxFlashAlpha;
        damageFlashImage.color = flashColor;
        elapsed = 0f;

        while (elapsed < flashOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            flashColor.a = Mathf.Lerp(maxFlashAlpha, 0f, elapsed / flashOutDuration);
            damageFlashImage.color = flashColor;
            yield return null;
        }

        flashColor.a = 0f;
        damageFlashImage.color = flashColor;
    }

    private IEnumerator Routine_UIRectShake(RectTransform targetRect, float duration, float positionMagnitude, float rotationMagnitude, Action onComplete = null)
    {
        if (targetRect == null) yield break;

        if (!restPositions.TryGetValue(targetRect, out Vector2 originalAnchoredPos))
        {
            originalAnchoredPos = targetRect.anchoredPosition;
            restPositions[targetRect] = originalAnchoredPos;
        }
        float elapsed = 0f;

        try
        {
            while (elapsed < duration)
            {
                float damping = 1f - (elapsed / duration);
                float offsetX = UnityEngine.Random.Range(-1f, 1f) * positionMagnitude * damping;
                float offsetY = UnityEngine.Random.Range(-1f, 1f) * positionMagnitude * damping;
                targetRect.anchoredPosition = originalAnchoredPos + new Vector2(offsetX, offsetY);

                float offsetAngle = UnityEngine.Random.Range(-1f, 1f) * rotationMagnitude * damping;
                targetRect.localRotation = Quaternion.Euler(0f, 0f, offsetAngle);

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        finally
        {
            targetRect.anchoredPosition = originalAnchoredPos;
            targetRect.localRotation = Quaternion.identity;
            onComplete?.Invoke();
        }
    }

    private IEnumerator Routine_CameraShake()
    {
        if (mainCamera == null) yield break;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * shakeIntensity;
            mainCamera.transform.localPosition = originalCamPos + new Vector3(randomOffset.x, randomOffset.y, 0f);
            yield return null;
        }

        mainCamera.transform.localPosition = originalCamPos;
        mainCamera.transform.localRotation = originalCamRot;
        activeCameraShake = null;
    }

    private void UpdateHPUI()
    {
        if (playerHPText != null) playerHPText.text = $"{currentPlayerHP}";
        if (opponentHPText != null) opponentHPText.text = $"{currentOpponentHP}";
    }

    public void OnForfeit()
    {
        if (isOfflineAIRival)
        {
            if (isMatchOver) return;
            ResetPauseState();
            TriggerLocalGameOver(false, false);
            if (resultText != null) resultText.text = "DEFEAT (You Forfeited)";
            return;
        }

        if (!IsSpawned || GameEnded.Value) return;
        if (IsGamePaused.Value && IsServer)
        {
            IsGamePaused.Value = false;
            PauseRequesterClientId.Value = 0;
            Time.timeScale = 1f;
        }
        SubmitForfeitServerRpc();
    }

    public void PlayerForfeit() => OnForfeit();

    [ServerRpc(RequireOwnership = false)]
    void SubmitForfeitServerRpc(ServerRpcParams rpcParams = default)
    {
        if (GameEnded.Value) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        var allClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        allClients.Sort();

        int forfeitingPlayer = (senderClientId == allClients[0]) ? 1 : 2;
        int winner = (forfeitingPlayer == 1) ? 2 : 1;

        GameEnded.Value = true;
        isMatchOver = true;
        GameOverDueToForfeitClientRpc(winner, senderClientId);
    }

    [ClientRpc]
    void GameOverDueToForfeitClientRpc(int winner, ulong forfeiterId)
    {
        if (leavingToLobby) return;
        isMatchOver = true;
        StopAllCoroutines();
        ResetPauseState();

        bool isLocalPlayerWinner = (NetworkManager.LocalClientId != forfeiterId);

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = isLocalPlayerWinner ? "VICTORY! (Opponent Forfeited)" : "DEFEAT (You Forfeited)";
        }
        if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(true);
    }

    private bool IsMatchRunning => (IsSpawned || isOfflineAIRival) && !isMatchOver && !GameEnded.Value;

    private void ResetPauseState()
    {
        Time.timeScale = 1f;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (leaveConfirmPanel != null) leaveConfirmPanel.SetActive(false);
    }

    public void OnLeaveClicked()
    {
        if (leavingToLobby) return;
        if (IsMatchRunning && leaveConfirmPanel != null)
        {
            leaveConfirmPanel.SetActive(true);
            return;
        }
        OnGoToLobby();
    }

    public void OnCancelLeave()
    {
        if (leaveConfirmPanel != null) leaveConfirmPanel.SetActive(false);
    }

    public void OnGoToLobby()
    {
        if (leavingToLobby) return;
        leavingToLobby = true;

        bool matchWasRunning = IsMatchRunning;
        isMatchOver = true;
        isResolvingCombat = false;
        StopAllCoroutines();
        ResetPauseState();

        bool sendForfeit = matchWasRunning && !isOfflineAIRival;
        if (sendForfeit) SubmitForfeitServerRpc();
        StartCoroutine(Routine_LeaveToLobby(sendForfeit));
    }

    private IEnumerator Routine_LeaveToLobby(bool sentForfeit)
    {
        if (sentForfeit) yield return new WaitForSecondsRealtime(0.35f);

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            nm.Shutdown();
            float waited = 0f;
            while (nm != null && nm.IsListening && waited < 2f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene(isOfflineAIRival ? offlineExitSceneName : lobbySceneName);
    }

    public void RequestPause()
    {
        if (isOfflineAIRival)
        {
            if (!isMatchOver) ApplyPauseState(true);
            return;
        }

        if (!IsSpawned || GameEnded.Value) return;
        if (Time.time - lastPauseRequestTime < PAUSE_COOLDOWN) return;
        lastPauseRequestTime = Time.time;
        RequestPauseServerRpc();
    }

    public void RequestResume()
    {
        if (isOfflineAIRival)
        {
            ApplyPauseState(false);
            return;
        }

        if (!IsSpawned || GameEnded.Value) return;
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        if (IsServer || PauseRequesterClientId.Value == localClientId) RequestResumeServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPauseServerRpc(ServerRpcParams rpcParams = default)
    {
        if (IsGamePaused.Value || GameEnded.Value) return;
        ulong requesterId = rpcParams.Receive.SenderClientId;
        IsGamePaused.Value = true;
        PauseRequesterClientId.Value = requesterId;
        PauseStateChangedClientRpc(true, requesterId);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestResumeServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsGamePaused.Value) return;
        ulong requesterId = rpcParams.Receive.SenderClientId;
        ulong pauseRequesterId = PauseRequesterClientId.Value;

        if (IsServer || requesterId == pauseRequesterId)
        {
            IsGamePaused.Value = false;
            PauseRequesterClientId.Value = 0;
            PauseStateChangedClientRpc(false, 0);
        }
    }

    [ClientRpc]
    void PauseStateChangedClientRpc(bool isPaused, ulong requesterId) => ApplyPauseState(isPaused);

    private void ApplyPauseState(bool isPaused)
    {
        if (isPaused)
        {
            timerRunning = false;
            if (pausePanel != null) pausePanel.SetActive(true);
            SetGameBoardInteractable(false);
            Time.timeScale = 0f;
        }
        else
        {
            timerRunning = true;
            if (pausePanel != null) pausePanel.SetActive(false);
            SetGameBoardInteractable(true);
            Time.timeScale = 1f;
        }
    }

    private bool ShouldHandBeInteractable()
    {
        if (isMatchOver || localTurnSubmitted) return false;
        return true;
    }

    void SetGameBoardInteractable(bool enabled)
    {
        if (endTurnButton != null) endTurnButton.interactable = enabled;
        if (skipTurnButton != null) skipTurnButton.interactable = enabled;

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager != null)
        {
            localHandManager.SetAllCardsInteractable(enabled && ShouldHandBeInteractable());
        }
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            NetworkManager.Singleton.Shutdown();
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}