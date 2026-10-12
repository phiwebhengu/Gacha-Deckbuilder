using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHandManager : MonoBehaviour
{
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
    [SerializeField] private float centerRevealScale = 1.25f;

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

    // Results from the server, in the order they were requested. A null entry means "server refused this pull".
    private Queue<PullResult> pendingPulls = new Queue<PullResult>();
    // If we stop waiting for a pull (timeout), its late answer must be dropped so the queue stays aligned.
    private int abandonedRequests = 0;

    private Coroutine activeFanCoroutine;
    private Coroutine activePlayedRowCoroutine;

    // Read-only access so the opponent's card-back fan (GachaUI) can use exactly the same layout numbers.
    public float CardSpacing => cardSpacing;
    public float MaxFanAngle => maxFanAngle;
    public float ArcHeight => arcHeight;
    public float FanLerpSpeed => fanLerpSpeed;
    private EndTurnManager cachedEndTurnManager;

    // ---------- Sprite cache ----------
    // Resources.Load is synchronous and hitches the frame the first time a big card texture is loaded.
    // We warm this cache in the background at scene start, and everything else reads from it.
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
        if (gachaManager != null)
        {
            gachaManager.OnMyPullResolved += HandlePullResolved;
            gachaManager.OnPullFailed += HandlePullFailed;
        }
        if (timerManager != null)
        {
            timerManager.OnDrawPhaseChanged += HandleDrawPhaseChanged;
            HandleDrawPhaseChanged(timerManager.IsDrawPhaseActive);
        }
    }

    private void OnDisable()
    {
        if (gachaManager != null)
        {
            gachaManager.OnMyPullResolved -= HandlePullResolved;
            gachaManager.OnPullFailed -= HandlePullFailed;
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

        if (gachaManager == null)
        {
            gachaManager = FindFirstObjectByType<GachaManager>();
            if (gachaManager == null) Debug.LogError("[PlayerHandManager] Could not find GachaManager in the scene!");
        }

        if (juiceFXManager == null)
        {
            juiceFXManager = FindFirstObjectByType<JuiceFXManager>();
        }

        // Disable layout group if present to prevent position conflict
        DisableLayoutGroupsOnHand();
    }

    private void Start()
    {
        StartCoroutine(Routine_PreloadSprites());
    }

    private IEnumerator Routine_PreloadSprites()
    {
        yield return null; // let DeckManager finish loading the CSVs
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
        StartCoroutine(Routine_DealCards(count, deckToPull, sourceButton));
    }

    private IEnumerator Routine_DealCards(int count, DeckType deckToPull, RectTransform sourceButton)
    {
        if (gachaManager == null) { Debug.LogError("[PlayerHandManager] gachaManager is null!"); yield break; }

        // Validate the prefab BEFORE spending a token on the server.
        if (cardPrefab == null ||
            cardPrefab.GetComponent<BalatroCardController>() == null ||
            cardPrefab.GetComponentInChildren<CardVisual>(true) == null)
        {
            Debug.LogError("[PlayerHandManager] cardPrefab is missing or lacks BalatroCardController / CardVisual! Cancelling deal.");
            yield break;
        }

        if (cachedEndTurnManager == null) cachedEndTurnManager = FindFirstObjectByType<EndTurnManager>();

        for (int i = 0; i < count; i++)
        {
            // ---- 1. ASK THE SERVER *IMMEDIATELY* ----
            if (deckToPull == DeckType.Support) gachaManager.RequestPullSupport();
            else gachaManager.RequestPullAction();

            // ---- 2. SPAWN THE CARD *IMMEDIATELY* (hides the network round trip for the joining player) ----
            Transform spawnParent = targetCanvas != null ? targetCanvas.transform : handTransform;
            GameObject newCardObj = Instantiate(cardPrefab, spawnParent);
            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponentInChildren<CardVisual>(true);

            if (cachedEndTurnManager != null) controller.SetEndTurnManager(cachedEndTurnManager);

            controller.LockForFlight(); // no hover/click events while flying (they fought the flight animation)
            if (cardBackSprite != null && visual.cardImage != null) visual.cardImage.sprite = cardBackSprite;

            cardRect.position = sourceButton != null ? sourceButton.position : handTransform.position;
            cardRect.localScale = Vector3.one * 0.3f;
            cardRect.rotation = Quaternion.identity;

            // ---- 3. FLY TO SCREEN CENTER (while the server answers) ----
            Vector3 centerPosition = targetCanvas != null ? targetCanvas.transform.position : Vector3.zero;
            Vector3 targetCenterScale = Vector3.one * centerRevealScale;

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

            // ---- 4. WAIT FOR THE RESULT (usually already here by now) ----
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
                // Server refused the pull (out of tokens / not ready). Remove the placeholder card.
                Destroy(newCardObj);
                continue;
            }

            // ---- 5. REVEAL ----
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
                // Always mark Support cards as Support, even if the CSV row wasn't found, because the deck type
                // is what gets sent to the server / opponent.
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

            // Re-parent KEEPING the world position. (SetParent(..., false) kept the old canvas-space numbers,
            // so the card teleported before sliding into the fan.)
            Vector3 arrivalWorldPos = cardRect.position;
            newCardObj.transform.SetParent(handTransform, true);

            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.position = arrivalWorldPos;
            cardRect.localScale = Vector3.one;

            // Re-check interactability NOW. It used to be decided at spawn time, so a card still animating when
            // the draw window closed stayed locked forever.
            controller.SetInteractable(timerManager == null || !timerManager.IsDrawPhaseActive);

            controller.SelectionChanged -= OnCardSelectionChanged;
            controller.SelectionChanged += OnCardSelectionChanged;

            cardsInHand.Add(controller);
            UpdateHandLayout();

            yield return new WaitForSeconds(dealDelay);
        }
    }

    private void OnCardSelectionChanged(BalatroCardController card) => UpdateHandLayout();

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
                // Encode which deck the card is from (Action and Support Ids can overlap).
                ids.Add(CardNetId.Encode(controller.CardId, controller.Category == CardCategory.Support));
            }
        }
        return ids;
    }

    public void SubmitSelectedCardsToHand(RectTransform selectedHandTarget)
    {
        List<BalatroCardController> selectedCards = GetSelectedCards();
        foreach (BalatroCardController controller in selectedCards)
        {
            if (controller == null) continue;

            cardsInHand.Remove(controller);
            submittedCards.Add(controller);

            // Locked-in cards get a neutral look (no leftover "selected" scale/green tint) so the combat
            // highlights come back to the same look the opponent's revealed cards have.
            controller.ResetVisualsOnly();

            controller.transform.SetParent(selectedHandTarget, true);
            controller.enabled = false;
        }

        UpdateHandLayout();
        LayoutPlayedCards(selectedHandTarget);
    }

    /// <summary>
    /// Locked-in cards sit in a plain horizontal row (no arc, no tilt), centred on the played-cards area.
    /// </summary>
    private void LayoutPlayedCards(RectTransform target)
    {
        if (activePlayedRowCoroutine != null)
        {
            StopCoroutine(activePlayedRowCoroutine);
            activePlayedRowCoroutine = null;
        }

        submittedCards.RemoveAll(c => c == null);
        if (target == null || submittedCards.Count == 0) return;

        // A layout group on the played area would fight the manual positions (same as on the hand).
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
        // Snapshot: the live list changes when cards are returned / discarded.
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

    /// <summary>
    /// Removes spent "immediate" support cards from the submitted list AND destroys them.
    /// (Destroy() only takes effect at the end of the frame, so destroying them from outside left them
    /// in our lists for one more layout pass, which put a gap in the fan.)
    /// </summary>
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

    /// <summary>
    /// Destroys specific played cards instead of returning them to the hand, one card per entry.
    /// Ids are network ids (Support ids are offset, see CardNetId). Used by Support effects that remove a card
    /// after the round, e.g. Kid Kujirai.
    /// </summary>
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

            // 1. Re-parent to hand transform (keep world position so the card visibly flies back into the fan)
            controller.transform.SetParent(handTransform, true);

            // 2. Enable controller AND all attached UI components
            controller.enabled = true;
            MonoBehaviour[] scripts = controller.GetComponents<MonoBehaviour>();
            foreach (var script in scripts)
            {
                if (script != null) script.enabled = true;
            }

            // 3. Re-enable interactability & raycast target
            controller.SetInteractable(true);

            // 4. Reset scale, hover flags, and deselection flags cleanly
            controller.ResetCardState();

            // 5. Ensure scale and anchors are completely normalized
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
        bool shouldBeInteractable = !isDrawPhaseActive;
        SetAllCardsInteractable(shouldBeInteractable);
    }

    // ============================ FAN LAYOUT ============================

    public void UpdateHandLayout()
    {
        CleanupHandList();

        // Always stop the old animation first, even when the hand is now empty. Otherwise a running
        // animation keeps dragging cards that were just moved to the "submitted" area.
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
        // Work on a snapshot. The live list can be modified mid-animation (cards destroyed / added),
        // which used to shift indices and make cards jump to someone else's slot.
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

            // Selected cards are lifted as part of the layout (this also covers a hand of exactly one card).
            if (cards[i] != null && cards[i].IsSelected)
            {
                yPos += cards[i].SelectYOffset;
            }

            targetPositions[i] = new Vector2(xPos, yPos);
            targetRotations[i] = Quaternion.Euler(0f, 0f, zAngle);
        }

        // NOTE: scale is intentionally NOT touched here any more. CardJuice owns hover/selected scale; the fan used to
        // pull every card back to 1.0 each frame, which made hovered/selected cards jitter and kept this loop alive forever.
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

        // Final snap
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