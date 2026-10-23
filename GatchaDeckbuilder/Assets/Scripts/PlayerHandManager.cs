using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHandManager : MonoBehaviour
{
    [Header("Game Mode Settings")]
    [SerializeField] public bool isOfflineAIRival = false;

    [Header("UI References")]
    [SerializeField] public GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform;
    [SerializeField] private Canvas targetCanvas;

    [Header("Animation Settings")]
    [SerializeField] private float dealDelay = 0.15f;

    [Header("Juicy Draw Reveal Settings")]
    [Tooltip("Total duration in seconds for the entire draw animation sequence.")]
    [SerializeField] private float totalDrawDuration = 2.0f;

    [Tooltip("How long the card holds/pauses at the center of the screen to reveal its stats.")]
    [SerializeField] private float centerPauseDuration = 0.6f;

    [Tooltip("Scale modifier when the card is presented in the center stage.")]
    [SerializeField] private float revealScale = 2.2f;

    [Header("Network Latency Handling")]
    [Tooltip("OPTIONAL. Sprite shown while the card is flying to the center and the server hasn't answered yet. " +
             "If empty, whatever sprite the card prefab has by default is shown.")]
    [SerializeField] private Sprite cardBackSprite;

    [Tooltip("How long to wait for the server's pull result before giving up on a card (seconds).")]
    [SerializeField] private float pullTimeout = 8f;

    [Header("Fan Layout Settings")]
    [Tooltip("Horizontal spacing between cards along the X axis.")]
    [SerializeField] private float cardSpacing = 120f;

    [Tooltip("Maximum rotation angle (in degrees) for the outermost cards.")]
    [SerializeField] private float maxFanAngle = 18f;

    [Tooltip("Vertical drop along the Y axis to create an arched fan curve.")]
    [SerializeField] private float arcHeight = 35f;

    [Tooltip("Speed at which cards animate into their fanned positions.")]
    [SerializeField] private float fanLerpSpeed = 12f;

    [Header("Played Cards Layout")]
    [Tooltip("Horizontal gap between the cards you've locked in for the turn. They sit in a straight row: no arc, no tilt.")]
    [SerializeField] private float playedCardSpacing = 120f;

    [Tooltip("How fast played cards slide into their row.")]
    [SerializeField] private float playedCardLerpSpeed = 14f;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private GachaManager gachaManager;
    [SerializeField] private JuiceFXManager juiceFXManager;

    private List<BalatroCardController> cardsInHand = new List<BalatroCardController>();
    private List<BalatroCardController> submittedCards = new List<BalatroCardController>();

    // Results from the server (or local generator in AI mode), in the order requested.
    private Queue<PullResult> pendingPulls = new Queue<PullResult>();
    private int abandonedRequests = 0;

    private Coroutine activeFanCoroutine;
    private Coroutine activePlayedRowCoroutine;

    private class BonusCard
    {
        public PullResult Result;
        public string Reason;
    }
    private readonly Queue<BonusCard> bonusQueue = new Queue<BonusCard>();
    private Coroutine bonusRoutine;
    private int dealsInProgress = 0;

    public float CardSpacing => cardSpacing;
    public float MaxFanAngle => maxFanAngle;
    public float ArcHeight => arcHeight;
    public float FanLerpSpeed => fanLerpSpeed;
    private EndTurnManager cachedEndTurnManager;

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    public static Sprite GetCardSpriteCached(int cardId, bool isActionCard)
    {
        string path = (isActionCard ? "CardSprites/Action/" : "CardSprites/Support/") + cardId;
        if (!spriteCache.TryGetValue(path, out Sprite sprite) || sprite == null)
        {
            sprite = Resources.Load<Sprite>(path);
            if (sprite != null) spriteCache[path] = sprite;
        }
        return sprite;
    }

    private void OnEnable()
    {
        if (gachaManager != null && !isOfflineAIRival)
        {
            gachaManager.OnMyPullResolved += HandlePullResolved;
            gachaManager.OnPullFailed += HandlePullFailed;
            gachaManager.OnMyBonusCardGranted += HandleBonusCardGranted;
            gachaManager.OnMyHandCardsRemoved += HandleHandCardsRemoved;
        }
        if (timerManager != null)
        {
            timerManager.OnDrawPhaseChanged += HandleDrawPhaseChanged;
            HandleDrawPhaseChanged(timerManager.IsDrawPhaseActive);
        }
    }

    private void OnDisable()
    {
        if (gachaManager != null && !isOfflineAIRival)
        {
            gachaManager.OnMyPullResolved -= HandlePullResolved;
            gachaManager.OnPullFailed -= HandlePullFailed;
            gachaManager.OnMyBonusCardGranted -= HandleBonusCardGranted;
            gachaManager.OnMyHandCardsRemoved -= HandleHandCardsRemoved;
        }
        if (timerManager != null)
            timerManager.OnDrawPhaseChanged -= HandleDrawPhaseChanged;
    }

    private void HandlePullResolved(PullResult result)
    {
        if (abandonedRequests > 0) { abandonedRequests--; return; }
        pendingPulls.Enqueue(result);
    }

    private void HandlePullFailed(string reason)
    {
        if (abandonedRequests > 0) { abandonedRequests--; return; }
        pendingPulls.Enqueue(null);
    }

    private void Awake()
    {
        if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
        if (timerManager == null)
        {
            timerManager = FindFirstObjectByType<DrawTimerManager>();
            if (timerManager == null) Debug.LogError("[PlayerHandManager] Could not find DrawTimerManager in the scene!");
        }

        if (gachaManager == null && !isOfflineAIRival)
        {
            gachaManager = FindFirstObjectByType<GachaManager>();
            if (gachaManager == null) Debug.LogWarning("[PlayerHandManager] Could not find GachaManager in the scene!");
        }

        if (juiceFXManager == null)
        {
            juiceFXManager = FindFirstObjectByType<JuiceFXManager>();
        }

        DisableLayoutGroupsOnHand();
    }

    private void Start()
    {
        StartCoroutine(Routine_PreloadSprites());
    }

    private IEnumerator Routine_PreloadSprites()
    {
        yield return null;
        var deck = FindFirstObjectByType<DeckManager>();
        if (deck == null) yield break;

        if (deck.loadedActionCards != null)
        {
            var seen = new HashSet<int>();
            foreach (var c in deck.loadedActionCards)
                if (seen.Add(c.Id)) yield return StartCoroutine(PreloadOne(c.Id, true));
        }

        if (deck.loadedSupportCards != null)
        {
            var seen = new HashSet<int>();
            foreach (var c in deck.loadedSupportCards)
                if (seen.Add(c.Id)) yield return StartCoroutine(PreloadOne(c.Id, false));
        }
    }

    private IEnumerator PreloadOne(int id, bool isAction)
    {
        string path = (isAction ? "CardSprites/Action/" : "CardSprites/Support/") + id;
        if (spriteCache.ContainsKey(path)) yield break;

        ResourceRequest request = Resources.LoadAsync<Sprite>(path);
        yield return request;
        if (request.asset is Sprite sprite) spriteCache[path] = sprite;
    }

    private void DisableLayoutGroupsOnHand()
    {
        if (handTransform != null)
        {
            LayoutGroup layoutGroup = handTransform.GetComponent<LayoutGroup>();
            if (layoutGroup != null)
            {
                Debug.LogWarning("[PlayerHandManager] Disabling LayoutGroup on handTransform to allow manual fanning math.");
                layoutGroup.enabled = false;
            }
        }
    }

    public void DealCardsFromTokens(int count, DeckType deckToPull, RectTransform sourceButton = null)
    {
        timerManager?.NotifyCardsDrawn();
        StartCoroutine(Routine_DealCardsTracked(count, deckToPull, sourceButton));
    }

    private IEnumerator Routine_DealCardsTracked(int count, DeckType deckToPull, RectTransform sourceButton)
    {
        dealsInProgress++;
        try
        {
            yield return StartCoroutine(Routine_DealCards(count, deckToPull, sourceButton));
        }
        finally
        {
            dealsInProgress = Mathf.Max(0, dealsInProgress - 1);
        }
    }

    private IEnumerator Routine_DealCards(int count, DeckType deckToPull, RectTransform sourceButton)
    {
        if (!isOfflineAIRival && gachaManager == null)
        {
            Debug.LogError("[PlayerHandManager] gachaManager is null!");
            yield break;
        }

        if (cardPrefab == null ||
            cardPrefab.GetComponent<BalatroCardController>() == null ||
            cardPrefab.GetComponentInChildren<CardVisual>(true) == null)
        {
            Debug.LogError("[PlayerHandManager] cardPrefab is missing or lacks BalatroCardController / CardVisual! Cancelling deal.");
            yield break;
        }

        if (cachedEndTurnManager == null) cachedEndTurnManager = FindFirstObjectByType<EndTurnManager>();

        if (cachedEndTurnManager != null && cachedEndTurnManager.IsMatchOver) yield break;

        GachaUI gachaUi = FindFirstObjectByType<GachaUI>();
        if (gachaUi != null && !gachaUi.CanPayForPull(deckToPull))
        {
            Debug.Log("[PlayerHandManager] Not enough tokens (or match over): no card dealt.");
            yield break;
        }

        for (int i = 0; i < count; i++)
        {
            // ---- 1. REQUEST OR GENERATE CARD PULL ----
            if (isOfflineAIRival)
            {
                DeckManager deckMgr = FindFirstObjectByType<DeckManager>();
                PullResult localResult = null;

                if (deckMgr != null)
                {
                    if (deckToPull == DeckType.Action && deckMgr.loadedActionCards != null && deckMgr.loadedActionCards.Count > 0)
                    {
                        var cardData = deckMgr.loadedActionCards[UnityEngine.Random.Range(0, deckMgr.loadedActionCards.Count)];

                        // Parse cardData.Tier (string or enum) safely into the Rarity enum
                        Rarity cardTier = Rarity.Common;
                        if (cardData.Tier != null)
                        {
                            Enum.TryParse(cardData.Tier.ToString(), true, out cardTier);
                        }

                        localResult = new PullResult
                        {
                            CardId = cardData.Id,
                            Deck = DeckType.Action,
                            Tier = cardTier,
                            ActionData = cardData
                        };
                    }
                    else if (deckToPull == DeckType.Support && deckMgr.loadedSupportCards != null && deckMgr.loadedSupportCards.Count > 0)
                    {
                        var cardData = deckMgr.loadedSupportCards[UnityEngine.Random.Range(0, deckMgr.loadedSupportCards.Count)];

                        Rarity cardTier = Rarity.Common;
                        if (cardData.Tier != null)
                        {
                            Enum.TryParse(cardData.Tier.ToString(), true, out cardTier);
                        }

                        localResult = new PullResult
                        {
                            CardId = cardData.Id,
                            Deck = DeckType.Support,
                            Tier = cardTier,
                            SupportData = cardData
                        };
                    }
                }

                if (localResult == null)
                {
                    Debug.LogError("[PlayerHandManager] Failed to generate local offline card pull!");
                    yield break;
                }

                pendingPulls.Enqueue(localResult);
            }
            else
            {
                if (deckToPull == DeckType.Support) gachaManager.RequestPullSupport();
                else gachaManager.RequestPullAction();
            }

            // ---- 2. SPAWN CARD PREFAB ----
            Transform spawnParent = targetCanvas != null ? targetCanvas.transform : handTransform;
            GameObject newCardObj = Instantiate(cardPrefab, spawnParent);
            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponentInChildren<CardVisual>(true);

            if (cachedEndTurnManager != null) controller.SetEndTurnManager(cachedEndTurnManager);

            controller.LockForFlight();
            if (cardBackSprite != null && visual.cardImage != null) visual.cardImage.sprite = cardBackSprite;

            cardRect.position = sourceButton != null ? sourceButton.position : handTransform.position;
            cardRect.localScale = Vector3.one * 0.3f;
            cardRect.rotation = Quaternion.identity;

            // ---- 3. FLY TO SCREEN CENTER ----
            Vector3 centerPosition = targetCanvas != null ? targetCanvas.transform.position : Vector3.zero;
            Vector3 targetCenterScale = Vector3.one * revealScale;

            float movePhaseDuration = Mathf.Max(0.01f, (totalDrawDuration - centerPauseDuration) * 0.5f);
            float t = 0f;

            Vector3 startPos = cardRect.position;
            Vector3 startScale = cardRect.localScale;

            while (t < movePhaseDuration)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / movePhaseDuration);

                cardRect.position = Vector3.Lerp(startPos, centerPosition, progress);
                cardRect.localScale = Vector3.Lerp(startScale, targetCenterScale, progress);
                yield return null;
            }

            cardRect.position = centerPosition;
            cardRect.localScale = targetCenterScale;

            // ---- 4. WAIT FOR RESULT ----
            float waited = 0f;
            while (pendingPulls.Count == 0 && waited < pullTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (pendingPulls.Count == 0)
            {
                Debug.LogError($"[PlayerHandManager] Pull {i + 1} timed out after {pullTimeout}s!");
                abandonedRequests++;
                Destroy(newCardObj);
                continue;
            }

            PullResult result = pendingPulls.Dequeue();
            if (result == null)
            {
                Destroy(newCardObj);
                continue;
            }

            // ---- 5. REVEAL CARD ----
            bool isAction = result.Deck == DeckType.Action;
            Sprite sprite = GetCardSpriteCached(result.CardId, isAction);

            if (isAction && result.ActionData != null) visual.Setup(result.ActionData, sprite);
            else if (!isAction && result.SupportData != null) visual.Setup(result.SupportData, sprite);
            else if (visual.cardImage != null && sprite != null) visual.cardImage.sprite = sprite;

            controller.SetCardId(result.CardId);
            if (isAction && result.ActionData != null)
                controller.SetCategory(result.ActionData.Role == "Attack" ? CardCategory.Attack : CardCategory.Defense);
            else if (!isAction)
            {
                controller.SetCategory(CardCategory.Support);
                if (result.SupportData != null)
                    controller.SetCardMetadata(result.SupportData.EffectType == "Forever");
            }

            if (JuiceFXManager.Instance != null)
            {
                JuiceFXManager.Instance.TriggerRarityJuice(result.Tier);
            }
            else if (juiceFXManager != null)
            {
                juiceFXManager.TriggerRarityJuice(result.Tier);
            }

            visual.PlayReveal(result.Tier);

            yield return new WaitForSeconds(centerPauseDuration);

            if (result.AutoDiscard)
            {
                yield return StartCoroutine(Routine_ShrinkAndDestroy(newCardObj));
                continue;
            }

            // ---- 6. MOVE TO HAND ----
            t = 0f;
            startPos = cardRect.position;
            startScale = cardRect.localScale;

            while (t < movePhaseDuration)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / movePhaseDuration);

                cardRect.position = Vector3.Lerp(startPos, handTransform.position, progress);
                cardRect.localScale = Vector3.Lerp(startScale, Vector3.one, progress);
                yield return null;
            }

            Vector3 arrivalWorldPos = cardRect.position;
            newCardObj.transform.SetParent(handTransform, true);

            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.position = arrivalWorldPos;
            cardRect.localScale = Vector3.one;

            controller.SetInteractable(timerManager == null || !timerManager.IsDrawPhaseActive);

            controller.SelectionChanged -= OnCardSelectionChanged;
            controller.SelectionChanged += OnCardSelectionChanged;

            cardsInHand.Add(controller);
            UpdateHandLayout();

            yield return new WaitForSeconds(dealDelay);
        }
    }

    private void OnCardSelectionChanged(BalatroCardController card) => UpdateHandLayout();

    // ============================ SUPPORT EFFECT CARDS ============================

    private void HandleBonusCardGranted(PullResult result, string reason)
    {
        if (result == null) return;

        bonusQueue.Enqueue(new BonusCard { Result = result, Reason = reason });

        if (bonusRoutine == null) bonusRoutine = StartCoroutine(Routine_ProcessBonusQueue());
    }

    private IEnumerator Routine_ProcessBonusQueue()
    {
        while (bonusQueue.Count > 0)
        {
            float waited = 0f;
            while (dealsInProgress > 0 && waited < 4f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            BonusCard next = bonusQueue.Dequeue();
            yield return StartCoroutine(Routine_ReceiveBonusCard(next.Result, next.Reason));
        }

        bonusRoutine = null;
    }

    private IEnumerator Routine_ReceiveBonusCard(PullResult result, string reason)
    {
        if (cardPrefab == null ||
            cardPrefab.GetComponent<BalatroCardController>() == null ||
            cardPrefab.GetComponentInChildren<CardVisual>(true) == null)
        {
            Debug.LogError("[PlayerHandManager] cardPrefab is missing or lacks BalatroCardController / CardVisual! Can't show a bonus card.");
            yield break;
        }

        if (cachedEndTurnManager == null) cachedEndTurnManager = FindFirstObjectByType<EndTurnManager>();

        Debug.Log($"[PlayerHandManager] Bonus card: {result.CardName} ({reason})");

        Transform spawnParent = targetCanvas != null ? targetCanvas.transform : handTransform;
        GameObject newCardObj = Instantiate(cardPrefab, spawnParent);
        RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
        BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
        CardVisual visual = newCardObj.GetComponentInChildren<CardVisual>(true);

        if (cachedEndTurnManager != null) controller.SetEndTurnManager(cachedEndTurnManager);
        controller.LockForFlight();
        if (cardBackSprite != null && visual.cardImage != null) visual.cardImage.sprite = cardBackSprite;

        cardRect.position = handTransform.position;
        cardRect.localScale = Vector3.one * 0.3f;
        cardRect.rotation = Quaternion.identity;

        Vector3 centerPosition = targetCanvas != null ? targetCanvas.transform.position : Vector3.zero;
        Vector3 targetCenterScale = Vector3.one * revealScale;
        float movePhaseDuration = Mathf.Max(0.01f, (totalDrawDuration - centerPauseDuration) * 0.5f);

        float t = 0f;
        Vector3 startPos = cardRect.position;
        Vector3 startScale = cardRect.localScale;

        while (t < movePhaseDuration)
        {
            if (newCardObj == null) yield break;
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / movePhaseDuration);
            cardRect.position = Vector3.Lerp(startPos, centerPosition, progress);
            cardRect.localScale = Vector3.Lerp(startScale, targetCenterScale, progress);
            yield return null;
        }

        cardRect.position = centerPosition;
        cardRect.localScale = targetCenterScale;

        bool isAction = result.Deck == DeckType.Action;
        Sprite sprite = GetCardSpriteCached(result.CardId, isAction);

        if (isAction && result.ActionData != null) visual.Setup(result.ActionData, sprite);
        else if (!isAction && result.SupportData != null) visual.Setup(result.SupportData, sprite);
        else if (visual.cardImage != null && sprite != null) visual.cardImage.sprite = sprite;

        controller.SetCardId(result.CardId);
        if (isAction && result.ActionData != null)
            controller.SetCategory(result.ActionData.Role == "Attack" ? CardCategory.Attack : CardCategory.Defense);
        else if (!isAction)
        {
            controller.SetCategory(CardCategory.Support);
            if (result.SupportData != null)
                controller.SetCardMetadata(result.SupportData.EffectType == "Forever");
        }

        if (JuiceFXManager.Instance != null) JuiceFXManager.Instance.TriggerRarityJuice(result.Tier);
        else if (juiceFXManager != null) juiceFXManager.TriggerRarityJuice(result.Tier);

        visual.PlayReveal(result.Tier);

        yield return new WaitForSeconds(centerPauseDuration);

        if (newCardObj == null) yield break;

        if (result.AutoDiscard)
        {
            yield return StartCoroutine(Routine_ShrinkAndDestroy(newCardObj));
            yield break;
        }

        t = 0f;
        startPos = cardRect.position;
        startScale = cardRect.localScale;

        while (t < movePhaseDuration)
        {
            if (newCardObj == null) yield break;
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / movePhaseDuration);
            cardRect.position = Vector3.Lerp(startPos, handTransform.position, progress);
            cardRect.localScale = Vector3.Lerp(startScale, Vector3.one, progress);
            yield return null;
        }

        Vector3 arrivalWorldPos = cardRect.position;
        newCardObj.transform.SetParent(handTransform, true);

        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.position = arrivalWorldPos;
        cardRect.localScale = Vector3.one;

        controller.SetInteractable(timerManager == null || !timerManager.IsDrawPhaseActive);

        controller.SelectionChanged -= OnCardSelectionChanged;
        controller.SelectionChanged += OnCardSelectionChanged;

        cardsInHand.Add(controller);
        UpdateHandLayout();

        yield return new WaitForSeconds(dealDelay);
    }

    private IEnumerator Routine_ShrinkAndDestroy(GameObject cardObj)
    {
        if (cardObj == null) yield break;

        Transform tr = cardObj.transform;
        Vector3 startScale = tr.localScale;
        float t = 0f;
        const float duration = 0.25f;

        while (t < duration)
        {
            if (cardObj == null) yield break;
            t += Time.unscaledDeltaTime;
            tr.localScale = Vector3.Lerp(startScale, Vector3.zero, Mathf.Clamp01(t / duration));
            yield return null;
        }

        if (cardObj != null) Destroy(cardObj);
    }

    private void HandleHandCardsRemoved(int[] netIds)
    {
        DiscardHandCardsByNetId(netIds);
    }

    public void DiscardHandCardsByNetId(int[] netIds)
    {
        if (netIds == null || netIds.Length == 0) return;

        CleanupHandList();
        bool removedAny = false;

        foreach (int netId in netIds)
        {
            for (int i = 0; i < cardsInHand.Count; i++)
            {
                BalatroCardController card = cardsInHand[i];
                if (card == null) continue;

                if (CardNetId.Encode(card.CardId, card.Category == CardCategory.Support) == netId)
                {
                    cardsInHand.RemoveAt(i);
                    card.SetInteractable(false);
                    StartCoroutine(Routine_ShrinkAndDestroy(card.gameObject));
                    removedAny = true;
                    break;
                }
            }
        }

        if (removedAny) UpdateHandLayout();
    }

    public void SetAllCardsInteractable(bool state)
    {
        CleanupHandList();

        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller != null) controller.SetInteractable(state);
        }
    }

    public List<BalatroCardController> GetSelectedCards()
    {
        CleanupHandList();

        List<BalatroCardController> selected = new List<BalatroCardController>();

        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller == null) continue;

            if (controller.IsSelected)
            {
                selected.Add(controller);
            }
        }

        return selected;
    }

    public List<int> GetSelectedCardIds()
    {
        CleanupHandList();

        List<int> ids = new List<int>();
        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller == null) continue;

            if (controller.IsSelected)
            {
                ids.Add(CardNetId.Encode(controller.CardId, controller.Category == CardCategory.Support));
            }
        }
        return ids;
    }

    public bool HasPlayableCard(List<ActionCardData> actionCards)
    {
        CleanupHandList();
        foreach (BalatroCardController c in cardsInHand)
        {
            if (c == null) continue;
            if (c.Category == CardCategory.Support) return true;

            ActionCardData data = actionCards != null ? actionCards.Find(a => a.Id == c.CardId) : null;
            if (data == null) return true;
            string cat = (data.Category ?? "").Trim();
            if (!cat.Equals("Weapon", System.StringComparison.OrdinalIgnoreCase) &&
                !cat.Equals("Artefact", System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public void SubmitSelectedCardsToHand(RectTransform selectedHandTarget)
    {
        List<BalatroCardController> selectedCards = GetSelectedCards();
        foreach (BalatroCardController controller in selectedCards)
        {
            if (controller == null) continue;

            cardsInHand.Remove(controller);
            submittedCards.Add(controller);

            controller.ResetVisualsOnly();

            controller.transform.SetParent(selectedHandTarget, true);
            controller.enabled = false;
        }

        UpdateHandLayout();
        LayoutPlayedCards(selectedHandTarget);
    }

    private void LayoutPlayedCards(RectTransform target)
    {
        if (activePlayedRowCoroutine != null)
        {
            StopCoroutine(activePlayedRowCoroutine);
            activePlayedRowCoroutine = null;
        }

        submittedCards.RemoveAll(c => c == null);
        if (target == null || submittedCards.Count == 0) return;

        LayoutGroup layoutGroup = target.GetComponent<LayoutGroup>();
        if (layoutGroup != null)
        {
            Debug.LogWarning("[PlayerHandManager] Disabling LayoutGroup on the played-cards area so the cards can be laid out in a straight row.");
            layoutGroup.enabled = false;
        }

        activePlayedRowCoroutine = StartCoroutine(Routine_AnimatePlayedRow(target));
    }

    private IEnumerator Routine_AnimatePlayedRow(RectTransform target)
    {
        List<BalatroCardController> cards = new List<BalatroCardController>(submittedCards);
        int count = cards.Count;

        float startX = -playedCardSpacing * (count - 1) * 0.5f;

        bool animating = true;
        while (animating)
        {
            animating = false;
            float blend = 1f - Mathf.Exp(-playedCardLerpSpeed * Time.unscaledDeltaTime);

            for (int i = 0; i < count; i++)
            {
                BalatroCardController card = cards[i];
                if (card == null || card.transform.parent != target) continue;

                RectTransform rt = card.Rect;
                Vector2 targetPos = new Vector2(startX + (i * playedCardSpacing), 0f);

                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, targetPos, blend);
                rt.localRotation = Quaternion.Slerp(rt.localRotation, Quaternion.identity, blend);

                if (Vector2.Distance(rt.anchoredPosition, targetPos) > 0.05f ||
                    Quaternion.Angle(rt.localRotation, Quaternion.identity) > 0.1f)
                {
                    animating = true;
                }
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            BalatroCardController card = cards[i];
            if (card == null || card.transform.parent != target) continue;

            RectTransform rt = card.Rect;
            rt.anchoredPosition = new Vector2(startX + (i * playedCardSpacing), 0f);
            rt.localRotation = Quaternion.identity;
        }

        activePlayedRowCoroutine = null;
    }

    public void DiscardSubmittedImmediateSupports()
    {
        for (int i = submittedCards.Count - 1; i >= 0; i--)
        {
            BalatroCardController c = submittedCards[i];
            if (c == null) { submittedCards.RemoveAt(i); continue; }

            if (c.Category == CardCategory.Support && !c.IsForever)
            {
                Debug.Log($"[PlayerHandManager] Discarding immediate support card: {c.gameObject.name}");
                submittedCards.RemoveAt(i);
                Destroy(c.gameObject);
            }
        }
    }

    public void DiscardSubmittedCardsByNetId(int[] netIds)
    {
        if (netIds == null || netIds.Length == 0) return;

        foreach (int netId in netIds)
        {
            for (int i = submittedCards.Count - 1; i >= 0; i--)
            {
                BalatroCardController c = submittedCards[i];
                if (c == null) { submittedCards.RemoveAt(i); continue; }

                if (CardNetId.Encode(c.CardId, c.Category == CardCategory.Support) == netId)
                {
                    submittedCards.RemoveAt(i);
                    Destroy(c.gameObject);
                    break;
                }
            }
        }
    }

    public void ReturnSubmittedCardsToHand(RectTransform containerTransform)
    {
        if (activePlayedRowCoroutine != null)
        {
            StopCoroutine(activePlayedRowCoroutine);
            activePlayedRowCoroutine = null;
        }

        submittedCards.RemoveAll(c => c == null);

        List<BalatroCardController> cardsToReturn = new List<BalatroCardController>(submittedCards);

        foreach (BalatroCardController controller in cardsToReturn)
        {
            if (controller == null) continue;

            submittedCards.Remove(controller);

            if (!cardsInHand.Contains(controller))
            {
                cardsInHand.Add(controller);
            }

            RectTransform cardRect = controller.GetComponent<RectTransform>();
            Vector3 worldPos = cardRect != null ? cardRect.position : Vector3.zero;

            controller.transform.SetParent(handTransform, true);

            controller.enabled = true;
            MonoBehaviour[] scripts = controller.GetComponents<MonoBehaviour>();
            foreach (var script in scripts)
            {
                if (script != null) script.enabled = true;
            }

            controller.SetInteractable(true);
            controller.ResetCardState();

            if (cardRect != null)
            {
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.position = worldPos;
                cardRect.localScale = Vector3.one;
            }
        }

        UpdateHandLayout();
    }

    private void CleanupHandList()
    {
        cardsInHand.RemoveAll(c => c == null);
    }

    public void ClearSubmittedCardsJuicy(RectTransform containerTransform, float delay = 0f) => StartCoroutine(Routine_ClearCardsJuicy(delay));

    private IEnumerator Routine_ClearCardsJuicy(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        List<BalatroCardController> cardsToClear = new List<BalatroCardController>(submittedCards);
        foreach (BalatroCardController controller in cardsToClear)
        {
            if (controller != null)
            {
                submittedCards.Remove(controller);
                Destroy(controller.gameObject);
            }
        }
    }

    private void HandleDrawPhaseChanged(bool isDrawPhaseActive)
    {
        // Cards should be interactable during draw phase AND combat selection phase
        bool shouldBeInteractable = true;
        SetAllCardsInteractable(shouldBeInteractable);
    }

    // ============================ FAN LAYOUT ============================

    public void UpdateHandLayout()
    {
        CleanupHandList();

        if (activeFanCoroutine != null)
        {
            StopCoroutine(activeFanCoroutine);
            activeFanCoroutine = null;
        }

        if (cardsInHand.Count == 0) return;

        activeFanCoroutine = StartCoroutine(Routine_AnimateFanLayout());
    }

    private bool IsInHand(BalatroCardController card)
    {
        return card != null && card.transform.parent == handTransform;
    }

    private IEnumerator Routine_AnimateFanLayout()
    {
        List<BalatroCardController> cards = new List<BalatroCardController>(cardsInHand);
        int cardCount = cards.Count;

        Vector2[] targetPositions = new Vector2[cardCount];
        Quaternion[] targetRotations = new Quaternion[cardCount];

        for (int i = 0; i < cardCount; i++)
        {
            float xPos = 0f, yPos = 0f, zAngle = 0f;

            if (cardCount > 1)
            {
                float totalWidth = cardSpacing * (cardCount - 1);
                xPos = (-totalWidth * 0.5f) + (i * cardSpacing);

                float normalizedIndex = (2f * i / (cardCount - 1)) - 1f;
                yPos = -(normalizedIndex * normalizedIndex) * arcHeight;
                zAngle = -normalizedIndex * maxFanAngle;
            }

            if (cards[i] != null && cards[i].IsSelected)
            {
                yPos += cards[i].SelectYOffset;
            }

            targetPositions[i] = new Vector2(xPos, yPos);
            targetRotations[i] = Quaternion.Euler(0f, 0f, zAngle);
        }

        bool isAnimating = true;
        while (isAnimating)
        {
            isAnimating = false;
            float blend = 1f - Mathf.Exp(-fanLerpSpeed * Time.unscaledDeltaTime);

            for (int i = 0; i < cardCount; i++)
            {
                if (!IsInHand(cards[i])) continue;

                RectTransform cardRect = cards[i].Rect;

                cardRect.anchoredPosition = Vector2.Lerp(cardRect.anchoredPosition, targetPositions[i], blend);
                cardRect.localRotation = Quaternion.Slerp(cardRect.localRotation, targetRotations[i], blend);

                if (Vector2.Distance(cardRect.anchoredPosition, targetPositions[i]) > 0.05f ||
                    Quaternion.Angle(cardRect.localRotation, targetRotations[i]) > 0.1f)
                {
                    isAnimating = true;
                }
            }

            yield return null;
        }

        for (int i = 0; i < cardCount; i++)
        {
            if (!IsInHand(cards[i])) continue;

            RectTransform cardRect = cards[i].Rect;
            cardRect.anchoredPosition = targetPositions[i];
            cardRect.localRotation = targetRotations[i];
        }

        activeFanCoroutine = null;
    }
}