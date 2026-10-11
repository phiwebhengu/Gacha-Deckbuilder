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

    [Header("Scene Settings")]
    [SerializeField] private string gameType = "Game";

    [Header("Match Settings")]
    [SerializeField] private int maxRounds = 4;

    private int currentPlayerHP;
    private int currentOpponentHP;
    private Dictionary<ulong, int[]> pendingTurns = new Dictionary<ulong, int[]>();
    private bool isPlayer1 = true;

    private bool isMatchOver = false;
    private bool isResolvingCombat = false;

    // How many of the opponent's played cards return to their hand after this round's resolution.
    private int opponentCardsReturningToHand = 0;

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

        GameEnded.OnValueChanged += OnGameEndedChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
        GameEnded.OnValueChanged -= OnGameEndedChanged;
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

    public void UpdateEndTurnButtonVisibility()
    {
        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager == null || endTurnButton == null) return;

        int selectedCount = localHandManager.GetSelectedCards().Count;
        endTurnButton.gameObject.SetActive(selectedCount > 0);
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

        localHandManager.SetAllCardsInteractable(false);
        localHandManager.SubmitSelectedCardsToHand(selectedHandTransform);

        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);

        SubmitTurnServerRpc(selectedIds);
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

        var resultA = CalculateTurnResultFromIds(cardsA, cardsB);
        resultA.playerCardIds = cardsA;
        resultA.opponentCardIds = cardsB;

        int currentHpA = aIsPlayer1 ? Player1_HP.Value : Player2_HP.Value;
        int currentHpB = aIsPlayer1 ? Player2_HP.Value : Player1_HP.Value;

        resultA.finalPlayerHP = Mathf.Max(0, currentHpA - resultA.netDamageToPlayer);
        resultA.finalOpponentHP = Mathf.Max(0, currentHpB - resultA.netDamageToOpponent);

        if (aIsPlayer1)
        {
            Player1_HP.Value = resultA.finalPlayerHP;
            Player2_HP.Value = resultA.finalOpponentHP;
        }
        else
        {
            Player2_HP.Value = resultA.finalPlayerHP;
            Player1_HP.Value = resultA.finalOpponentHP;
        }

        SendResultToClient(idA, resultA);

        var resultB = CalculateTurnResultFromIds(cardsB, cardsA);
        resultB.playerCardIds = cardsB;
        resultB.opponentCardIds = cardsA;
        resultB.finalPlayerHP = resultA.finalOpponentHP;
        resultB.finalOpponentHP = resultA.finalPlayerHP;
        SendResultToClient(idB, resultB);

        pendingTurns.Clear();
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

    private TurnResultData CalculateTurnResultFromIds(int[] myCardIds, int[] opponentCardIds)
    {
        var deckMgr = FindFirstObjectByType<DeckManager>();
        if (deckMgr == null)
        {
            Debug.LogError("[EndTurnManager] DeckManager not found on Server! Damage calculation will fail.");
            return new TurnResultData();
        }

        int myAttack = 0, myDefense = 0;
        int oppAttack = 0, oppDefense = 0;

        void ProcessCards(int[] ids, ref int attack, ref int defense)
        {
            foreach (int netId in ids)
            {
                int id = CardNetId.Decode(netId);

                if (CardNetId.IsSupport(netId))
                {
                    // Support cards add no attack/defense here (a clashing Action Id used to be counted by mistake).
                    var supportCard = deckMgr.loadedSupportCards?.Find(c => c.Id == id);
                    if (supportCard == null)
                        Debug.LogWarning($"[EndTurnManager] Support card ID {id} not found in Support list!");
                    continue;
                }

                var actionCard = deckMgr.loadedActionCards?.Find(c => c.Id == id);
                if (actionCard == null)
                {
                    Debug.LogWarning($"[EndTurnManager] Action card ID {id} not found in Action list!");
                    continue;
                }

                if (actionCard.Role == "Attack") attack += actionCard.Value;
                else if (actionCard.Role == "Defense") defense += actionCard.Value;
            }
        }

        ProcessCards(myCardIds, ref myAttack, ref myDefense);
        ProcessCards(opponentCardIds, ref oppAttack, ref oppDefense);

        int damageToOpponent = Mathf.Max(0, myAttack - oppDefense);
        int damageToMe = Mathf.Max(0, oppAttack - myDefense);

        return new TurnResultData
        {
            playerAttack = myAttack,
            playerDefense = myDefense,
            opponentAttack = oppAttack,
            opponentDefense = oppDefense,
            netDamageToPlayer = damageToMe,
            netDamageToOpponent = damageToOpponent,
            finalPlayerHP = 0,
            finalOpponentHP = 0
        };
    }

    private IEnumerator Routine_ResolveCombatSequence(TurnResultData result)
    {
        isResolvingCombat = true;
        opponentCardsReturningToHand = CountOpponentCardsReturningToHand(result.opponentCardIds);
        yield return new WaitForSeconds(cardMovementWaitDelay);

        List<BalatroCardController> playerCards = GetControllersFromTransform(selectedHandTransform);
        List<BalatroCardController> opponentCards = InstantiateOpponentCards(result.opponentCardIds);

        // --- ATTACK PHASE ---
        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Attack);
        PlayHighlightSFX(CardCategory.Attack);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerAttack, playerAttackText, originalPlayerAttackScale,
            result.opponentAttack, opponentAttackText, originalOpponentAttackScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        // --- DEFENSE PHASE ---
        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Defense);
        PlayHighlightSFX(CardCategory.Defense);
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

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(Routine_CheckNextRoundOrEndGame());
        isResolvingCombat = false;
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
    private int CountOpponentCardsReturningToHand(int[] opponentCardIds)
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
        isMatchOver = true;
        StopAllCoroutines();

        bool isLocalPlayerWinner = (NetworkManager.LocalClientId != forfeiterId);

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = isLocalPlayerWinner ? "VICTORY! (Opponent Forfeited)" : "DEFEAT (You Forfeited)";
        }

        if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(true);
    }

    public void OnGoToLobby()
    {
        StartCoroutine(ReturnToLobbyRoutine());
    }

    System.Collections.IEnumerator ReturnToLobbyRoutine()
    {
        StopAllCoroutines();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        string lobbySceneName = "Lobby";
        UnityEngine.SceneManagement.SceneManager.LoadScene(lobbySceneName);

        yield return null;
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

    void SetGameBoardInteractable(bool enabled)
    {
        bool finalState = enabled && !IsGamePaused.Value;

        if (endTurnButton != null)
        {
            endTurnButton.interactable = finalState;
        }

        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager != null)
        {
            localHandManager.SetAllCardsInteractable(finalState);
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