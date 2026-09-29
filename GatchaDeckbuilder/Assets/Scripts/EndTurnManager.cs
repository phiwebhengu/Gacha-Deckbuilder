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

public class EndTurnManager : NetworkBehaviour
{
    [Header("Health & Points Settings")]
    [SerializeField] private int startingHP = 20;

    [Header("Network State")]
    public NetworkVariable<int> Player1_HP = new NetworkVariable<int>(20, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Player2_HP = new NetworkVariable<int>(20, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ✅ NEW: Network state for Pause, Forfeit, and Game Over
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

    [Header("Center Difference Display")]
    [SerializeField] private TextMeshProUGUI centerDifferenceText;
    [SerializeField] private float countStepInterval = 0.08f;
    [SerializeField] private float popScaleMultiplier = 1.35f;
    [SerializeField] private float popDuration = 0.06f;
    [SerializeField] private float cardMovementWaitDelay = 0.5f;
    [SerializeField] private float phaseTransitionPause = 0.4f;

    [Header("Juice & Camera Shake Settings")]
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
    [SerializeField] private string gameType = "CardGame"; // Used for scene unloading logic

    private int currentPlayerHP;
    private int currentOpponentHP;
    private Dictionary<ulong, int[]> pendingTurns = new Dictionary<ulong, int[]>();
    private bool isPlayer1 = true;

    private bool isMatchOver = false;
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

    private PlayerHandManager _cachedLocalHandManager;
    private bool _hasCachedHandManager = false;

    public override void OnNetworkSpawn()
    {
        var allClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        allClients.Sort();
        isPlayer1 = (NetworkManager.LocalClientId == allClients[0]);

        currentPlayerHP = isPlayer1 ? Player1_HP.Value : Player2_HP.Value;
        currentOpponentHP = isPlayer1 ? Player2_HP.Value : Player1_HP.Value;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        // ✅ NEW: Listen for game over state changes from the server
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
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null) originalCamPos = mainCamera.transform.localPosition;

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
        pendingTurns[clientId] = selectedCardIds ?? Array.Empty<int>();
        Debug.Log($"[Server] Received turn from Client {clientId} with {selectedCardIds.Length} cards.");

        if (pendingTurns.Count >= 2)
        {
            ResolveAndBroadcastCombat();
        }
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
            foreach (int id in ids)
            {
                var actionCard = deckMgr.loadedActionCards?.Find(c => c.Id == id);
                if (actionCard != null)
                {
                    if (actionCard.Role == "Attack") attack += actionCard.Value;
                    else if (actionCard.Role == "Defense") defense += actionCard.Value;
                    continue;
                }

                var supportCard = deckMgr.loadedSupportCards?.Find(c => c.Id == id);
                if (supportCard == null)
                {
                    Debug.LogWarning($"[EndTurnManager] Card ID {id} not found in Action OR Support lists!");
                }
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
        yield return new WaitForSeconds(cardMovementWaitDelay);

        List<BalatroCardController> playerCards = GetControllersFromTransform(selectedHandTransform);
        List<BalatroCardController> opponentCards = InstantiateOpponentCards(result.opponentCardIds);

        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Attack);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerAttack, playerAttackText, originalPlayerAttackScale,
            result.opponentAttack, opponentAttackText, originalOpponentAttackScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, opponentCards);

        HighlightCardsByCategory(playerCards, opponentCards, CardCategory.Defense);
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
                StartCoroutine(Routine_PopText(centerDifferenceText.transform, originalCenterScale));
                yield return new WaitForSeconds(countStepInterval);
            }
        }
        yield return new WaitForSeconds(phaseTransitionPause);

        if (result.netDamageToPlayer > 0)
        {
            for (int i = 1; i <= result.netDamageToPlayer; i++)
            {
                currentPlayerHP = Mathf.Max(0, currentPlayerHP - 1);
                UpdateHPUI();
                if (playerHPText != null) StartCoroutine(Routine_PopText(playerHPText.transform, originalPlayerHPScale));
                StartCoroutine(Routine_CameraShake());
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        if (result.netDamageToOpponent > 0)
        {
            for (int i = 1; i <= result.netDamageToOpponent; i++)
            {
                currentOpponentHP = Mathf.Max(0, currentOpponentHP - 1);
                UpdateHPUI();
                if (opponentHPText != null) StartCoroutine(Routine_PopText(opponentHPText.transform, originalOpponentHPScale));
                StartCoroutine(Routine_CameraShake());
                yield return new WaitForSeconds(countStepInterval);
            }
        }

        currentPlayerHP = result.finalPlayerHP;
        currentOpponentHP = result.finalOpponentHP;
        UpdateHPUI();

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(Routine_CheckNextRoundOrEndGame());
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

        foreach (int cardId in opponentCardIds)
        {
            GameObject newCardObj = Instantiate(anyHandManager.cardPrefab, opponentSelectedHandTransform);
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponentInChildren<CardVisual>();

            if (controller != null && visual != null)
            {
                controller.SetCardId(cardId);
                controller.SetInteractable(false);

                var actionCard = deckMgr.loadedActionCards?.Find(c => c.Id == cardId);
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
                    }
                }
                opponentCards.Add(controller);
            }
        }
        return opponentCards;
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        string path = $"{folder}{cardId}";
        return Resources.Load<Sprite>(path);
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
        for (int step = 1; step <= maxSteps; step++)
        {
            if (step <= playerTargetVal && playerText != null)
            {
                playerText.text = step.ToString();
                StartCoroutine(Routine_PopText(playerText.transform, playerScale));
            }
            if (step <= opponentTargetVal && opponentText != null)
            {
                opponentText.text = step.ToString();
                StartCoroutine(Routine_PopText(opponentText.transform, opponentScale));
            }
            yield return new WaitForSeconds(countStepInterval);
        }
    }

    private IEnumerator Routine_CheckNextRoundOrEndGame()
    {
        // 1. HEALTH KNOCKOUT CHECK
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

        // 2. CONTINUE TO NEXT ROUND
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
            timerManager.RequestStartRound(currentRound);
        }
    }

    // ✅ UPDATED: Triggers game over and syncs network state
    private void TriggerLocalGameOver(bool isPlayerWinner, bool isDraw)
    {
        isMatchOver = true;
        GameEnded.Value = true; // ✅ Sync network state
        NotifyServerGameOverServerRpc();

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
        PlayerHandManager localHandManager = GetLocalHandManager();
        if (localHandManager != null && selectedHandTransform != null)
        {
            localHandManager.ReturnSubmittedCardsToHand(selectedHandTransform);
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

        yield return null;
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
    }

    private void UpdateHPUI()
    {
        if (playerHPText != null) playerHPText.text = $"{currentPlayerHP}";
        if (opponentHPText != null) opponentHPText.text = $"{currentOpponentHP}";
    }

    // ========================================================================
    // ✅ FORFEIT, PAUSE, AND RETURN TO LOBBY INTEGRATION
    // ========================================================================

    public void OnForfeit()
    {
        if (!IsSpawned || GameEnded.Value)
            return;

        // If game is paused, resume it first
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
        OnForfeit(); // Alias for consistency
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
        // Note: Ensure UIManager is a globally accessible singleton in your project
        if (UIManager.Instance != null)
        {
            UIManager.Instance.NotifyReturnedToLobby();
        }

        string sceneToUnload = "GameScene"; // Default fallback
        sceneToUnload = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(sceneToUnload);

        if (UIManager.Instance != null)
        {
            UIManager.Instance.NotifyReturnedToLobby();
        }
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

        // Optional: If you have a rewind button or other UI elements, handle them here
        // if (rewindButton != null) rewindButton.interactable = finalState;
    }
}