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

    // Played cards that a Support effect removes from the hand after the round (e.g. Kid Kujirai). Network ids.
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

/// <summary>
/// Action and Support cards can share the same numeric Id (each CSV has its own numbering), so an Id on its own
/// doesn't say which deck a card is from. Support cards are sent over the network with an offset so the receiver
/// (server damage math + opponent card display) always looks in the right deck.
/// </summary>
public static class CardNetId
{
    public const int SupportOffset = 10000;
    public static int Encode(int cardId, bool isSupport) => isSupport ? cardId + SupportOffset : cardId;
    public static bool IsSupport(int netId) => netId >= SupportOffset;
    public static int Decode(int netId) => netId >= SupportOffset ? netId - SupportOffset : netId;
}

public class EndTurnManager : NetworkBehaviour
{
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
    [Header("Incremental Points Sound")]
    [SerializeField] private AudioClip pointCountSFX;
    [SerializeField] private float minPitch = 0.9f;
    [SerializeField] private float maxPitch = 1.8f;

    [Header("Combat Damage Sound")]
    [SerializeField] private AudioClip damageSFX;

    [Header("Card Highlight Sounds")]
    [SerializeField] private AudioClip attackHighlightSFX;
    [SerializeField] private AudioClip defenseHighlightSFX;
    [SerializeField] private AudioClip supportHighlightSFX;

    [Header("✨ JUICE: Damage Flash UI ✨")]
    [Tooltip("Full screen red overlay or vignette Image component for player taking damage.")]
    [SerializeField] private Image damageFlashImage;
    [SerializeField] private float flashInDuration = 0.05f;
    [SerializeField] private float flashOutDuration = 0.25f;
    [SerializeField][Range(0f, 1f)] private float maxFlashAlpha = 0.6f;

    [Header("✨ JUICE: Damage UI Shake Settings ✨")]
    [Tooltip("Optional parent panel or transform overrides for health shake. If null, defaults to playerHPText/opponentHPText RectTransforms.")]
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
    [Tooltip("Assign the Card Prefab here to ensure opponent cards can be instantiated reliably. Falls back to PlayerHandManager's prefab if left empty.")]
    [SerializeField] private GameObject opponentCardPrefab;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private Button returnToLobbyButton;

    [Header("Pause & Forfeit UI")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button forfeitButton;
    [SerializeField] private Button pauseButton;

    [Header("Leave To Lobby (mid-match or after it ends)")]
    [SerializeField] private GameObject leaveConfirmPanel;

    [Header("Scene Settings")]
    [SerializeField] private string gameType = "Game";
    [SerializeField] private string lobbySceneName = "Lobby";

    [Header("Match Settings")]
    [SerializeField] private int maxRounds = 10;

    [Header("Pop-up Font (Futura)")]
    [Tooltip("TMP Font Asset used for the reaction pop-ups and the 'can't play that' message.")]
    [SerializeField] private TMP_FontAsset popupFont;
    [SerializeField] private float reactionPopupFontSize = 40f;
    [Tooltip("Optional. A TMP text you styled yourself (font, size). Each reaction pop-up is a copy of it. Leave it in the scene (inactive); drag it here.")]
    [SerializeField] private TextMeshProUGUI reactionPopupTemplate;

    [Header("Skip Turn (shown when the hand has only Weapon / Artefact cards)")]
    [Tooltip("Optional. Leave empty and a copy of the End Turn button is created next to it.")]
    [SerializeField] private Button skipTurnButton;
    private bool drawPhaseSeen = false;   // true once this round's draw window has been and gone
    private float nextSkipCheck = 0f;

    private int currentPlayerHP;
    private int currentOpponentHP;
    private Dictionary<ulong, int[]> pendingTurns = new Dictionary<ulong, int[]>();
    private bool isPlayer1 = true;

    private bool isMatchOver = false;
    private bool isResolvingCombat = false;
    private bool leavingToLobby = false;

    // True from the moment this player ends their turn until the next round starts. Used so that un-pausing
    // doesn't re-enable cards that are supposed to stay locked.
    private bool localTurnSubmitted = false;

    // How many of the opponent's played cards return to their hand after this round's resolution.
    private int opponentCardsReturningToHand = 0;

    // This player's played cards that a Support effect removes after the round (set when the result arrives).
    private int[] pendingOwnDiscardNetIds = null;

    // Server only: Support state that carries over between rounds (e.g. Stormterror's Lair), per player.
    private readonly Dictionary<ulong, SupportMatchState> supportStates = new Dictionary<ulong, SupportMatchState>();
    private GachaManager cachedGachaManager;

    // What each player played in the round that was just resolved. The gacha side of the Support effects (free pulls,
    // Seymour, Frostmoon Enclave...) is applied from this when both players are ready for the next round.
    private readonly List<GachaManager.RoundEndInfo> pendingRoundEnd = new List<GachaManager.RoundEndInfo>();

    // Server-side: which clients have finished watching the combat animation and are ready for the next round.
    private readonly HashSet<ulong> clientsReadyForNextRound = new HashSet<ulong>();

    // Juice bookkeeping (one running pop per text, one camera shake, a fixed "home" position per shaken rect)
    private readonly Dictionary<Transform, Coroutine> activePops = new Dictionary<Transform, Coroutine>();
    private readonly Dictionary<RectTransform, Vector2> restPositions = new Dictionary<RectTransform, Vector2>();
    private Coroutine activeCameraShake;
    private int currentRound = 1;
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

    public override void OnNetworkSpawn()
    {
        // The host is always Player 1. (ConnectedClientsIds is a server-side list, so don't rely on it from a joining client.)
        isPlayer1 = IsServer;

        currentPlayerHP = isPlayer1 ? Player1_HP.Value : Player2_HP.Value;
        currentOpponentHP = isPlayer1 ? Player2_HP.Value : Player1_HP.Value;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }
        else
        {
            // Joining player: notice when the host disappears (they left, closed the game, or the connection dropped).
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleDisconnectedFromHost;
        }

        GameEnded.OnValueChanged += OnGameEndedChanged;
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

        // On a joining player the networked scene objects despawn when the host goes away. Depending on the order
        // Netcode runs things in, this can happen before the disconnect callback, so handle it here too.
        // (Ignored when we are leaving ourselves, or the match has already ended.)
        if (!IsServer) ShowMatchEndedByDisconnect("DISCONNECTED (Host left the match)");
    }

    private void OnGameEndedChanged(bool oldVal, bool newVal)
    {
        if (newVal && !isMatchOver)
        {
            // The host can finish its animations first and flip GameEnded while this client is still playing its
            // own combat sequence. Let the local sequence finish; it will show the real VICTORY/DEFEAT/DRAW text.
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
            Debug.Log($"[Server] Client {clientId} disconnected during turn. Clearing pending state.");
        }

        clientsReadyForNextRound.Remove(clientId);

        // The other player disconnected mid-match (closed the game / lost connection): the remaining player wins.
        // (A player who leaves on purpose forfeits first, so the match is already over and this does nothing.)
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

        // GameEnded is server-write-only. This also locks the server against further turn submissions.
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

        currentRound = 1;

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

        int myPlayerSlot = isPlayer1 ? 1 : 2;
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
        Debug.LogWarning("[EndTurnManager] Could not find matching slot, using first available PlayerHandManager.");
        _cachedLocalHandManager = FindFirstObjectByType<PlayerHandManager>();
        _hasCachedHandManager = true;
        return _cachedLocalHandManager;
    }

    /// <summary>True once the match has ended (locally or on the server).</summary>
    public bool IsMatchOver => isMatchOver || (IsSpawned && GameEnded.Value);

    public void UpdateEndTurnButtonVisibility()
    {
        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || endTurnButton == null) return;

        int selectedCount = localHandManager.GetSelectedCards().Count;
        endTurnButton.gameObject.SetActive(selectedCount > 0);
    }

    // ============================ SKIP TURN ============================

    private void SetupSkipButton()
    {
        if (skipTurnButton == null && endTurnButton != null)
        {
            // No button assigned: make one by copying the End Turn button, so it already matches your style (and font).
            skipTurnButton = Instantiate(endTurnButton, endTurnButton.transform.parent);
            skipTurnButton.name = "SkipTurnButton";
            skipTurnButton.onClick = new Button.ButtonClickedEvent(); // drop the copied End Turn click

            TMP_Text label = skipTurnButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "Skip Turn";

            RectTransform rt = skipTurnButton.GetComponent<RectTransform>();
            rt.anchoredPosition += new Vector2(-(rt.sizeDelta.x + 30f), 0f); // next to End Turn
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

        // The skip button is only for the play phase, i.e. after this round's draw window has finished.
        if (timerManager != null && timerManager.IsDrawPhaseActive) drawPhaseSeen = true;

        if (Time.unscaledTime < nextSkipCheck) return;
        nextSkipCheck = Time.unscaledTime + 0.25f;

        bool show = IsSpawned && !isMatchOver && !localTurnSubmitted && !isResolvingCombat
                    && drawPhaseSeen && timerManager != null && !timerManager.IsDrawPhaseActive
                    && LocalPlayerHasNoPlayableCard();

        if (skipTurnButton.gameObject.activeSelf != show) skipTurnButton.gameObject.SetActive(show);
    }

    /// <summary>True when every card in the player's hand is a Weapon or Artefact (or the hand is empty).</summary>
    private bool LocalPlayerHasNoPlayableCard()
    {
        DeckManager deck = FindFirstObjectByType<DeckManager>();
        if (deck == null || deck.loadedActionCards == null) return false;

        foreach (BalatroCardController card in FindObjectsByType<BalatroCardController>(FindObjectsSortMode.None))
        {
            if (card == null || !card.IsInteractable) continue;
            if (selectedHandTransform != null && card.transform.IsChildOf(selectedHandTransform)) continue;
            if (opponentSelectedHandTransform != null && card.transform.IsChildOf(opponentSelectedHandTransform)) continue;

            if (card.Category == CardCategory.Support) return false;          // Support cards can always be played

            ActionCardData data = deck.loadedActionCards.Find(c => c.Id == card.CardId);
            if (data == null) return false;                                    // unknown card: don't offer a skip

            string cat = (data.Category ?? "").Trim();
            bool needsPartner = cat.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
                             || cat.Equals("Artefact", StringComparison.OrdinalIgnoreCase);
            if (!needsPartner) return false;                                   // a Character or Monster is playable
        }
        return true;
    }

    public void OnSkipTurnClicked()
    {
        if (localTurnSubmitted || isMatchOver) return;

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || !LocalPlayerHasNoPlayableCard()) return;

        // Put back any card the player had selected, then lock the hand like a normal End Turn.
        foreach (BalatroCardController c in localHandManager.GetSelectedCards()) c.ForceDeselect();

        localTurnSubmitted = true;
        drawPhaseSeen = false;
        localHandManager.SetAllCardsInteractable(false);

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
        if (skipTurnButton != null) skipTurnButton.gameObject.SetActive(false);

        SubmitTurnServerRpc(Array.Empty<int>());   // an empty turn: no cards played this round
    }

    public void OnEndTurnClicked()
    {
        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || selectedHandTransform == null) return;

        int[] selectedIds = localHandManager.GetSelectedCardIds().ToArray();
        if (selectedIds.Length == 0)
        {
            Debug.LogWarning("[EndTurnManager] No selected card IDs found! Aborting turn.");
            return;
        }

        // Weapons need a Character, Artefacts need a card of the same element (see CardRules).
        DeckManager rulesDeck = FindFirstObjectByType<DeckManager>();
        if (rulesDeck != null && !CardRules.IsPlayAllowed(selectedIds, rulesDeck.loadedActionCards, out string invalidReason))
        {
            ShowInvalidPlayMessage(invalidReason);
            return; // nothing is submitted; the player keeps their selection and can change it
        }

        localTurnSubmitted = true;
        drawPhaseSeen = false;
        if (skipTurnButton != null) skipTurnButton.gameObject.SetActive(false);
        localHandManager.SetAllCardsInteractable(false);
        localHandManager.SubmitSelectedCardsToHand(selectedHandTransform);

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);

        SubmitTurnServerRpc(selectedIds);
    }

    // ---------- "you can't play that" message ----------
    [Header("Invalid Play Message (optional)")]
    [Tooltip("Shows why a selection can't be played. Leave empty and a simple text is created above the End Turn button.")]
    [SerializeField] private TextMeshProUGUI invalidPlayText;
    [SerializeField] private float invalidPlayMessageSeconds = 3f;
    private Coroutine invalidPlayRoutine;

    private void ShowInvalidPlayMessage(string message)
    {
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
        if (isMatchOver || GameEnded.Value)
        {
            Debug.LogWarning($"[Server] Ignoring turn submission from Client {rpcParams.Receive.SenderClientId} because match is over.");
            return;
        }

        ulong clientId = rpcParams.Receive.SenderClientId;

        // Ignore a repeat submission from the same player (it would otherwise remove card backs twice).
        if (pendingTurns.ContainsKey(clientId)) return;

        pendingTurns[clientId] = selectedCardIds ?? Array.Empty<int>();
        Debug.Log($"[Server] Received turn from Client {clientId} with {pendingTurns[clientId].Length} cards.");

        // Tell the other player how many cards just left this player's hand. ONLY the count is sent, never the
        // card IDs, so what was played stays hidden until the reveal.
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
        // Opponent's cards moved from their hand to the table: remove that many card backs.
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
        if (deckMgr == null)
        {
            Debug.LogError("[EndTurnManager] DeckManager not found on Server! Damage calculation will fail.");
        }

        // One calculation for BOTH players, so the two clients always agree. Attack/defense totals and damage already
        // include every Support effect (see SupportEffectResolver for the rules).
        SupportEffectResolver.Resolve(
            cardsA, cardsB, deckMgr,
            GetSupportState(idA), GetSupportState(idB),
            out SideOutcome outA, out SideOutcome outB);

        LogSupportNotes(idA, outA);
        LogSupportNotes(idB, outB);

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

        // Token rewards from Support effects (Paimon, Vanarana, Katheryne).
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

    private void LogSupportNotes(ulong clientId, SideOutcome outcome)
    {
        foreach (string note in outcome.Notes)
        {
            Debug.Log($"[Support] Client {clientId}: {note}");
        }
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

        // --- ATTACK PHASE ---
        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Attack);
        PlayHighlightSFX(CardCategory.Attack);
        ShowReactionPopups(result, true);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerAttack, playerAttackText, originalPlayerAttackScale,
            result.opponentAttack, opponentAttackText, originalOpponentAttackScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        // --- DEFENSE PHASE ---
        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Defense);
        PlayHighlightSFX(CardCategory.Defense);
        ShowReactionPopups(result, false);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerDefense, playerDefenseText, originalPlayerDefenseScale,
            result.opponentDefense, opponentDefenseText, originalOpponentDefenseScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        // --- DIFFERENCE STEP ---
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

        // --- PLAYER DAMAGE PHASE ---
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
                        targetTargetRect.localRotation = Quaternion.identity; // Reset rotation if interrupted
                    }
                    activePlayerShakeCoroutine = StartCoroutine(Routine_UIRectShake(targetTargetRect, damageShakeDuration, damageShakeMagnitude, damageShakeRotation, () => activePlayerShakeCoroutine = null));
                }

                ShakeCamera();
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        // --- OPPONENT DAMAGE PHASE ---
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
                        targetTargetRect.localRotation = Quaternion.identity; // Reset rotation if interrupted
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

        // Numbers for the Outcome page (this player's point of view).
        MatchStats.RecordRound(result.netDamageToOpponent, result.netDamageToPlayer);

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(Routine_CheckNextRoundOrEndGame());
        isResolvingCombat = false;
    }
    // ---------- elemental reaction pop-ups ("Overload +1 ATK") ----------
    private void ShowReactionPopups(TurnResultData result, bool attackPhase)
    {
        DeckManager deck = FindFirstObjectByType<DeckManager>();
        if (deck == null) return;

        SpawnReactionPopups(result.playerCardIds, false, attackPhase, playerAttackText, playerDefenseText);
        SpawnReactionPopups(result.opponentCardIds, true, attackPhase, opponentAttackText, opponentDefenseText);
    }

    // With a Reaction Popup Template the text stays exactly where the template is and reactions are shown one after the
    // other (the opponent's are marked "Opponent:"). Without a template they float above the Attack / Defense numbers.
    private float reactionPopupFreeAt = 0f;

    private void SpawnReactionPopups(int[] netIds, bool isOpponent, bool attackPhase, TextMeshProUGUI attackText, TextMeshProUGUI defenseText)
    {
        if (netIds == null) return;

        // Same rule as the server (SupportEffectResolver): elements of the played Action cards -> reactions.
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

            float delay;
            float lifetime;
            if (reactionPopupTemplate != null)
            {
                // one fixed spot: wait until the previous pop-up is gone
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

    private void PlayFixedDamageSFX()
    {
        SFXPool.Play(damageSFX, 1f);
    }

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

    // Same rule the local player's hand uses: Action cards and "Forever" Support cards go back to the hand,
    // "Immediate" Support cards are spent and gone.
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

        // Cards a Support effect removes after the round (e.g. Kid Kujirai) don't go back to the hand either.
        if (opponentDiscardedIds != null) returning = Mathf.Max(0, returning - opponentDiscardedIds.Length);

        return returning;
    }

    private List<BalatroCardController> InstantiateOpponentCards(int[] opponentCardIds)
    {
        List<BalatroCardController> opponentCards = new List<BalatroCardController>();
        if (opponentCardIds == null || opponentSelectedHandTransform == null)
            return opponentCards;

        var deckMgr = FindFirstObjectByType<DeckManager>();
        if (deckMgr == null)
        {
            Debug.LogError("[InstantiateOpponentCards] DeckManager not found!");
            return opponentCards;
        }

        PlayerHandManager anyHandManager = FindFirstObjectByType<PlayerHandManager>();
        if (anyHandManager == null || anyHandManager.cardPrefab == null)
        {
            Debug.LogError("[InstantiateOpponentCards] No hand manager / cardPrefab found.");
            return opponentCards;
        }

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

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        return PlayerHandManager.GetCardSpriteCached(cardId, isActionCard);
    }

    private List<BalatroCardController> GetControllersFromTransform(RectTransform container)
    {
        List<BalatroCardController> list = new List<BalatroCardController>();
        if (container == null) return list;

        foreach (BalatroCardController ctrl in container.GetComponentsInChildren<BalatroCardController>())
        {
            if (ctrl != null) list.Add(ctrl);
        }
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

            // Pooled so the rising pitch doesn't bend the previous step's sound that is still playing.
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
            Debug.Log("[Match Over] BOTH PLAYERS KNOCKED OUT! DRAW GAME!");
            TriggerLocalGameOver(false, true);
            yield break;
        }
        if (currentOpponentHP <= 0)
        {
            Debug.Log("[Match Over] PLAYER WINS BY KNOCKOUT!");
            TriggerLocalGameOver(true, false);
            yield break;
        }
        if (currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] OPPONENT WINS BY KNOCKOUT!");
            TriggerLocalGameOver(false, false);
            yield break;
        }

        if (currentRound >= maxRounds)
        {
            Debug.Log($"[Match Over] MAX ROUNDS ({maxRounds}) REACHED! Evaluating winner based on HP.");
            EvaluateWinnerByHP();
            yield break;
        }

        yield return StartCoroutine(Routine_ClearAllSubmittedCards());
        localTurnSubmitted = false;
        ResetTurnUI();

        if (timerManager != null)
        {
            PlayerHandManager localHandManager = GetLocalHandManager();
            if (localHandManager != null)
            {
                localHandManager.SetAllCardsInteractable(true);
            }

            currentRound++;

            // Only the host could start rounds, and it did so as soon as *the host's* animations finished, which
            // is earlier than the joining player's. The new round (and its countdown) then started on top of the
            // joiner's unfinished combat animation. Now each player reports "ready" and the server starts the
            // round when both have.
            ReadyForNextRoundServerRpc(currentRound);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReadyForNextRoundServerRpc(int nextRound, ServerRpcParams rpcParams = default)
    {
        if (GameEnded.Value) return;

        clientsReadyForNextRound.Add(rpcParams.Receive.SenderClientId);

        if (clientsReadyForNextRound.Count >= NetworkManager.Singleton.ConnectedClientsIds.Count)
        {
            clientsReadyForNextRound.Clear();

            // Both players finished the resolution animation: hand out the pull/economy Support effects
            // (free cards, Seymour, Frostmoon Enclave...) BEFORE the next draw window opens.
            ApplyGachaRoundEnd();

            if (timerManager != null) timerManager.RequestStartRound(nextRound);
        }
    }

    private void EvaluateWinnerByHP()
    {
        if (currentPlayerHP > currentOpponentHP)
        {
            Debug.Log("[Match Over] PLAYER WINS BY HP!");
            TriggerLocalGameOver(true, false);
        }
        else if (currentOpponentHP > currentPlayerHP)
        {
            Debug.Log("[Match Over] OPPONENT WINS BY HP!");
            TriggerLocalGameOver(false, false);
        }
        else
        {
            Debug.Log("[Match Over] DRAW BY HP!");
            TriggerLocalGameOver(false, true);
        }
    }

    private void TriggerLocalGameOver(bool isPlayerWinner, bool isDraw)
    {
        isMatchOver = true;
        isResolvingCombat = false;

        // GameEnded is server-write-only. A joining client writing to it throws an exception, which skipped the
        // rest of this method (so the client never saw its game-over screen). Clients ask the server instead.
        if (IsServer) GameEnded.Value = true;
        else NotifyServerGameOverServerRpc();

        StopAllCoroutines();

        if (gameOverPanel != null) gameOverPanel.SetActive(true);

        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            if (isDraw)
            {
                resultText.text = "DRAW";
            }
            else if (isPlayerWinner)
            {
                resultText.text = "VICTORY!";
            }
            else
            {
                resultText.text = "DEFEAT";
            }
        }

        if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(true);
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyServerGameOverServerRpc(ServerRpcParams rpcParams = default)
    {
        isMatchOver = true;
        GameEnded.Value = true;
        Debug.Log($"[Server] Match over flagged by client {rpcParams.Receive.SenderClientId}. Locking further turns.");
    }

    private void ResetTurnUI()
    {
        if (playerAttackText != null) playerAttackText.text = "0";
        if (playerDefenseText != null) playerDefenseText.text = "0";
        if (opponentAttackText != null) opponentAttackText.text = "0";
        if (opponentDefenseText != null) opponentDefenseText.text = "0";
        if (centerDifferenceText != null) centerDifferenceText.text = "";

        // New round: the skip button waits for this round's draw window again.
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
                // Removes spent "immediate" supports from the hand manager's lists AND destroys them, so the
                // returning cards aren't fanned around a card that is about to disappear (that left a gap).
                localHandManager.DiscardSubmittedImmediateSupports();

                // Cards a Support effect removes this round (e.g. Kid Kujirai) are destroyed instead of returning.
                localHandManager.DiscardSubmittedCardsByNetId(pendingOwnDiscardNetIds);
                pendingOwnDiscardNetIds = null;

                localHandManager.ReturnSubmittedCardsToHand(selectedHandTransform);
            }
            else
            {
                foreach (var card in selectedHandTransform.GetComponentsInChildren<BalatroCardController>())
                {
                    if (card != null && card.Category == CardCategory.Support && !card.IsForever)
                        Destroy(card.gameObject);
                }
            }

            yield return new WaitForSeconds(0.3f);
        }

        if (opponentSelectedHandTransform != null)
        {
            BalatroCardController[] oppCards = opponentSelectedHandTransform.GetComponentsInChildren<BalatroCardController>();
            foreach (var card in oppCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
        }

        // The opponent's non-spent cards go back to their hand, so their card-back stack grows back by that many.
        // Spent Immediate Support cards are not added back, so they stay gone from the stack.
        if (opponentCardsReturningToHand > 0)
        {
            foreach (GachaUI ui in FindObjectsByType<GachaUI>(FindObjectsSortMode.None))
                ui.AddOpponentCardBacks(opponentCardsReturningToHand);
            opponentCardsReturningToHand = 0;
        }

        yield return null;
    }

    // Only ONE pop per text at a time. A pop lasts longer than the count interval, so overlapping pops used to
    // write to the same scale at once and the older one snapped the newer one back to base size (flicker).
    private void PopText(Transform textTransform, Vector3 baseScale)
    {
        if (textTransform == null) return;

        if (activePops.TryGetValue(textTransform, out Coroutine running) && running != null)
            StopCoroutine(running);

        textTransform.localScale = baseScale;
        activePops[textTransform] = StartCoroutine(Routine_PopText(textTransform, baseScale));
    }

    // Same idea for the camera: overlapping shakes used to reset the camera while another one was still running.
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

        // Flash In
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

        // Fade Out
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

        // Always return to the SAME rest position. Reading the current position here meant that when a shake was
        // interrupted mid-way (every damage tick), the next shake treated the displaced position as "home" and the
        // HP text slowly drifted across the screen.
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
            // Reset position and force rotation back to 0 on all axes
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
        if (!IsSpawned || GameEnded.Value)
            return;

        if (IsGamePaused.Value && IsServer)
        {
            IsGamePaused.Value = false;
            PauseRequesterClientId.Value = 0;
            Time.timeScale = 1f;
        }

        SubmitForfeitServerRpc();
    }

    public void PlayerForfeit()
    {
        OnForfeit();
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitForfeitServerRpc(ServerRpcParams rpcParams = default)
    {
        if (GameEnded.Value)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        var allClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        allClients.Sort();

        int forfeitingPlayer = (senderClientId == allClients[0]) ? 1 : 2;
        int winner = (forfeitingPlayer == 1) ? 2 : 1;

        Debug.Log($"Player {forfeitingPlayer} forfeited. Player {winner} wins.");

        GameEnded.Value = true;
        isMatchOver = true;

        GameOverDueToForfeitClientRpc(winner, senderClientId);
    }

    [ClientRpc]
    void GameOverDueToForfeitClientRpc(int winner, ulong forfeiterId)
    {
        // If we are the one leaving, don't show a game-over screen or stop the coroutine that is taking us to the lobby.
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

    // ============================ LEAVE TO LOBBY ============================

    private bool IsMatchRunning => IsSpawned && !isMatchOver && !GameEnded.Value;

    // The pause menu freezes time (Time.timeScale = 0) on BOTH players. Always un-freeze before leaving or showing
    // a game-over screen, otherwise the lobby scene would load frozen.
    private void ResetPauseState()
    {
        Time.timeScale = 1f;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (leaveConfirmPanel != null) leaveConfirmPanel.SetActive(false);
    }

    /// <summary>
    /// Hook this to a "Leave" button. Mid-match it asks for confirmation first (if a confirm panel is assigned),
    /// because leaving counts as a forfeit. After the match it just leaves.
    /// </summary>
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

    /// <summary>
    /// Leaves for the lobby right now. Works mid-match (counts as a forfeit, the opponent is told they won)
    /// and after the match has ended (no forfeit needed).
    /// </summary>
    public void OnGoToLobby()
    {
        if (leavingToLobby) return;
        leavingToLobby = true;

        bool matchWasRunning = IsMatchRunning;

        isMatchOver = true;
        isResolvingCombat = false;

        // Stop the combat animation etc. The leave routine is started AFTER this, so it isn't stopped too.
        StopAllCoroutines();
        ResetPauseState();

        // Mid-match: tell the server so the opponent gets a proper VICTORY screen instead of a dead game.
        if (matchWasRunning) SubmitForfeitServerRpc();

        StartCoroutine(Routine_LeaveToLobby(matchWasRunning));
    }

    private IEnumerator Routine_LeaveToLobby(bool sentForfeit)
    {
        // Let the forfeit message actually leave before the connection closes (messages go out at the end of the frame).
        if (sentForfeit) yield return new WaitForSecondsRealtime(0.35f);

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            nm.Shutdown();

            // Wait (briefly) for the shutdown to finish before changing scene.
            float waited = 0f;
            while (nm != null && nm.IsListening && waited < 2f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene(lobbySceneName);
    }

    public void RequestPause()
    {
        if (!IsSpawned || GameEnded.Value)
            return;

        if (Time.time - lastPauseRequestTime < PAUSE_COOLDOWN)
        {
            Debug.Log("Pause on cooldown");
            return;
        }

        lastPauseRequestTime = Time.time;
        RequestPauseServerRpc();
    }

    public void RequestResume()
    {
        if (!IsSpawned || GameEnded.Value)
            return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        if (IsServer || PauseRequesterClientId.Value == localClientId)
        {
            RequestResumeServerRpc();
        }
        else
        {
            Debug.Log("Only the player who paused can resume the game");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPauseServerRpc(ServerRpcParams rpcParams = default)
    {
        if (IsGamePaused.Value || GameEnded.Value)
        {
            Debug.Log("Game is already paused or ended");
            return;
        }

        ulong requesterId = rpcParams.Receive.SenderClientId;
        Debug.Log($"Player {requesterId} requested pause");

        IsGamePaused.Value = true;
        PauseRequesterClientId.Value = requesterId;
        PauseStateChangedClientRpc(true, requesterId);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestResumeServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsGamePaused.Value)
        {
            Debug.Log("Game is not paused");
            return;
        }

        ulong requesterId = rpcParams.Receive.SenderClientId;
        ulong pauseRequesterId = PauseRequesterClientId.Value;

        if (IsServer || requesterId == pauseRequesterId)
        {
            Debug.Log($"Resuming game. Requested by: {requesterId}");
            IsGamePaused.Value = false;
            PauseRequesterClientId.Value = 0;
            PauseStateChangedClientRpc(false, 0);
        }
        else
        {
            Debug.Log($"Player {requesterId} cannot resume - only player {pauseRequesterId} can");
        }
    }

    [ClientRpc]
    void PauseStateChangedClientRpc(bool isPaused, ulong requesterId)
    {
        if (isPaused)
        {
            timerRunning = false;
            if (pausePanel != null)
                pausePanel.SetActive(true);

            SetGameBoardInteractable(false);
            Time.timeScale = 0f;
        }
        else
        {
            timerRunning = true;
            if (pausePanel != null)
                pausePanel.SetActive(false);

            SetGameBoardInteractable(true);
            Time.timeScale = 1f;
        }
    }

    // Should the player's hand cards be clickable right now, ignoring the pause menu?
    // (Not during the draw window, not after they've ended their turn, not once the match is over.)
    private bool ShouldHandBeInteractable()
    {
        if (isMatchOver || localTurnSubmitted) return false;
        if (timerManager != null && timerManager.IsDrawPhaseActive) return false;
        return true;
    }

    void SetGameBoardInteractable(bool enabled)
    {
        // `enabled` is the pause state this client was just told about, so use it directly.
        // This used to also read IsGamePaused.Value. That is a NetworkVariable, and the update that says "resumed"
        // can reach this client AFTER the resume message does, so on resume it still held the old value (true),
        // the board stayed locked and the player could not interact after pressing Resume.
        if (endTurnButton != null)
        {
            endTurnButton.interactable = enabled;
        }
        if (skipTurnButton != null) skipTurnButton.interactable = enabled;

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager != null)
        {
            localHandManager.SetAllCardsInteractable(enabled && ShouldHandBeInteractable());
        }
    }

    public void QuitGame()
    {
        Debug.Log("Quitting game...");

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