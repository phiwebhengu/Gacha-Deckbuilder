using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Unity.Netcode;

public struct TurnResultData : INetworkSerializable
{
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

    [Header("System References")]
    [SerializeField] private PlayerHandManager handManager;
    [SerializeField] private DrawTimerManager timerManager;

    private int currentPlayerHP;
    private int currentOpponentHP;
    private Dictionary<ulong, int[]> pendingTurns = new Dictionary<ulong, int[]>();
    private bool isPlayer1 = true;

    private Vector3 originalCenterScale = Vector3.one;
    private Vector3 originalPlayerHPScale = Vector3.one;
    private Vector3 originalOpponentHPScale = Vector3.one;
    private Vector3 originalPlayerAttackScale = Vector3.one;
    private Vector3 originalPlayerDefenseScale = Vector3.one;
    private Vector3 originalOpponentAttackScale = Vector3.one;
    private Vector3 originalOpponentDefenseScale = Vector3.one;
    private Vector3 originalCamPos;

    public override void OnNetworkSpawn()
    {
        var allClients = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        allClients.Sort();
        isPlayer1 = (NetworkManager.LocalClientId == allClients[0]);

        currentPlayerHP = isPlayer1 ? Player1_HP.Value : Player2_HP.Value;
        currentOpponentHP = isPlayer1 ? Player2_HP.Value : Player1_HP.Value;
    }

    private void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null) originalCamPos = mainCamera.transform.localPosition;

        CacheAndResetUI();
        UpdateEndTurnButtonVisibility();
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

    public void UpdateEndTurnButtonVisibility()
    {
        if (handManager == null)
        {
            Debug.LogWarning("[EndTurnManager] handManager is NOT assigned in the Inspector!");
            return;
        }
        if (endTurnButton == null)
        {
            Debug.LogWarning("[EndTurnManager] endTurnButton is NOT assigned in the Inspector!");
            return;
        }

        int selectedCount = handManager.GetSelectedCards().Count;
        Debug.Log($"[EndTurnManager] Checking visibility... Selected cards count: {selectedCount}");

        endTurnButton.gameObject.SetActive(selectedCount > 0);
    }

    public void OnEndTurnClicked()
    {
        if (handManager == null || selectedHandTransform == null) return;

        // Now returns List<BalatroCardController>
        List<BalatroCardController> playerSelectedCards = handManager.GetSelectedCards();
        if (playerSelectedCards.Count == 0) return;

        // Disable card interactions via the hand manager
        handManager.SetAllCardsInteractable(false);

        handManager.SubmitSelectedCardsToHand(selectedHandTransform);
        if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);

        int[] selectedIds = handManager.GetSelectedCardIds().ToArray();
        SubmitTurnServerRpc(selectedIds);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitTurnServerRpc(int[] selectedCardIds, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        pendingTurns[clientId] = selectedCardIds;

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
        if (deckMgr == null) return new TurnResultData();

        int myAttack = 0, myDefense = 0;
        int oppAttack = 0, oppDefense = 0;

        foreach (int id in myCardIds)
        {
            var card = deckMgr.loadedActionCards?.Find(c => c.Id == id);
            if (card != null)
            {
                if (card.Role == "Attack") myAttack += card.Value;
                else if (card.Role == "Defense") myDefense += card.Value;
            }
            else
            {
                var support = deckMgr.loadedSupportCards?.Find(c => c.Id == id);
                if (support != null)
                {
                    Debug.Log($"[Server] Processing Support Card: {support.Name}");
                }
            }
        }

        foreach (int id in opponentCardIds)
        {
            var card = deckMgr.loadedActionCards?.Find(c => c.Id == id);
            if (card != null)
            {
                if (card.Role == "Attack") oppAttack += card.Value;
                else if (card.Role == "Defense") oppDefense += card.Value;
            }
        }

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

        HighlightCardsByCategory(playerCards, new List<BalatroCardController>(), CardCategory.Attack);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerAttack, playerAttackText, originalPlayerAttackScale,
            result.opponentAttack, opponentAttackText, originalOpponentAttackScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, new List<BalatroCardController>());

        HighlightCardsByCategory(playerCards, new List<BalatroCardController>(), CardCategory.Defense);
        yield return StartCoroutine(Routine_CountUpPair(
            result.playerDefense, playerDefenseText, originalPlayerDefenseScale,
            result.opponentDefense, opponentDefenseText, originalOpponentDefenseScale
        ));
        yield return new WaitForSeconds(phaseTransitionPause);
        ResetCardHighlights(playerCards, new List<BalatroCardController>());

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
        // Implementation depends on how you want to reset highlights on BalatroCardController
    }

    private List<BalatroCardController> GetControllersFromTransform(RectTransform container)
    {
        List<BalatroCardController> list = new List<BalatroCardController>();
        if (container == null) return list;

        BalatroCardController[] controllers = container.GetComponentsInChildren<BalatroCardController>();
        foreach (BalatroCardController ctrl in controllers)
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
        if (currentOpponentHP <= 0 && currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] BOTH PLAYERS KNOCKED OUT! DRAW GAME!");
            yield return StartCoroutine(Routine_ClearAllSubmittedCards());
            yield break;
        }
        if (currentOpponentHP <= 0)
        {
            Debug.Log("[Match Over] PLAYER WINS BY KNOCKOUT!");
            yield return StartCoroutine(Routine_ClearAllSubmittedCards());
            yield break;
        }
        if (currentPlayerHP <= 0)
        {
            Debug.Log("[Match Over] OPPONENT WINS BY KNOCKOUT!");
            yield return StartCoroutine(Routine_ClearAllSubmittedCards());
            yield break;
        }

        yield return StartCoroutine(Routine_ClearAllSubmittedCards());
        ResetTurnUI();

        if (timerManager != null)
        {
            // Re-enable card interactions for the next round
            handManager.SetAllCardsInteractable(true);
        }
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
        if (handManager != null && selectedHandTransform != null)
        {
            yield return StartCoroutine(Routine_ProcessContainerCleanup(handManager, selectedHandTransform));
        }
    }

    private IEnumerator Routine_ProcessContainerCleanup(PlayerHandManager ownerHandManager, RectTransform selectedTransform)
    {
        BalatroCardController[] cardsInContainer = selectedTransform.GetComponentsInChildren<BalatroCardController>();
        List<BalatroCardController> immediateCardsToDestroy = new List<BalatroCardController>();

        foreach (BalatroCardController controller in cardsInContainer)
        {
            if (controller == null) continue;
            if (controller.Category == CardCategory.Support && !controller.IsForever)
            {
                immediateCardsToDestroy.Add(controller);
            }
        }

        if (immediateCardsToDestroy.Count > 0)
        {
            yield return StartCoroutine(Routine_AnimateImmediateCardsDisappearance(immediateCardsToDestroy));
        }

        ownerHandManager.ReturnSubmittedCardsToHand(selectedTransform);
    }

    private IEnumerator Routine_AnimateImmediateCardsDisappearance(List<BalatroCardController> cardsToClear)
    {
        float popUpDuration = 0.12f;
        float shrinkDuration = 0.18f;
        Vector3 popScale = new Vector3(1.3f, 1.3f, 1f);

        float elapsed = 0f;
        while (elapsed < popUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / popUpDuration;
            foreach (BalatroCardController controller in cardsToClear)
            {
                if (controller != null) controller.transform.localScale = Vector3.Lerp(Vector3.one, popScale, t);
            }
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            foreach (BalatroCardController controller in cardsToClear)
            {
                if (controller != null) controller.transform.localScale = Vector3.Lerp(popScale, Vector3.zero, t);
            }
            yield return null;
        }

        foreach (BalatroCardController controller in cardsToClear)
        {
            if (controller != null) Destroy(controller.gameObject);
        }
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
            Vector2 randomOffset = Random.insideUnitCircle * shakeIntensity;
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
}