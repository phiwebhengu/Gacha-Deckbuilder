using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] public GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform;
    [SerializeField] private Canvas targetCanvas;

    [Header("Animation Settings")]
    [SerializeField] private float dealDelay = 0.15f;

    // ✨ NEW: JUICY REVEAL ANIMATION SETTINGS ✨
    [Header("Juicy Draw Reveal Settings")]
    [Tooltip("Total duration in seconds for the entire draw animation sequence.")]
    [SerializeField] private float totalDrawDuration = 2.0f;

    [Tooltip("How long the card holds/pauses at the center of the screen to reveal its stats.")]
    [SerializeField] private float centerPauseDuration = 0.6f;

    [Tooltip("Scale modifier when the card is presented in the center stage.")]
    [SerializeField] private float centerRevealScale = 1.25f;

    [Header("Fan Layout Settings")]
    [Tooltip("Horizontal spacing between cards along the X axis.")]
    [SerializeField] private float cardSpacing = 120f;

    [Tooltip("Maximum rotation angle (in degrees) for the outermost cards.")]
    [SerializeField] private float maxFanAngle = 18f;

    [Tooltip("Vertical drop along the Y axis to create an arched fan curve.")]
    [SerializeField] private float arcHeight = 35f;

    [Tooltip("Speed at which cards animate into their fanned positions.")]
    [SerializeField] private float fanLerpSpeed = 12f;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private GachaManager gachaManager;
    // ✨ NEW: Optional direct reference to JuiceFXManager if not using Singleton
    [SerializeField] private JuiceFXManager juiceFXManager;

    private List<BalatroCardController> cardsInHand = new List<BalatroCardController>();
    private List<BalatroCardController> submittedCards = new List<BalatroCardController>();
    private Queue<PullResult> pendingPulls = new Queue<PullResult>();

    private Coroutine activeFanCoroutine;

    private void OnEnable()
    {
        if (gachaManager != null)
            gachaManager.OnMyPullResolved += HandlePullResolved;
        if (timerManager != null)
        {
            timerManager.OnDrawPhaseChanged += HandleDrawPhaseChanged;
            HandleDrawPhaseChanged(timerManager.IsDrawPhaseActive);
        }
    }

    private void OnDisable()
    {
        if (gachaManager != null)
            gachaManager.OnMyPullResolved -= HandlePullResolved;
        if (timerManager != null)
            timerManager.OnDrawPhaseChanged -= HandleDrawPhaseChanged;
    }

    private void HandlePullResolved(PullResult result) => pendingPulls.Enqueue(result);

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

        // ✨ NEW: Find JuiceFXManager if missing
        if (juiceFXManager == null)
        {
            juiceFXManager = FindFirstObjectByType<JuiceFXManager>();
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

        for (int i = 0; i < count; i++)
        {
            if (deckToPull == DeckType.Support) gachaManager.RequestPullSupport();
            else gachaManager.RequestPullAction();

            PullResult result = null;
            float timeout = 2f, elapsed = 0f;

            while (pendingPulls.Count == 0 && elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (pendingPulls.Count > 0) result = pendingPulls.Dequeue();
            else
            {
                Debug.LogError($"[PlayerHandManager] Pull {i + 1} timed out after {timeout}s!");
                continue;
            }

            if (result == null)
            {
                Debug.LogError("[PlayerHandManager] Pull result is null after timeout!");
                yield break;
            }

            Transform spawnParent = targetCanvas != null ? targetCanvas.transform : handTransform;
            GameObject newCardObj = Instantiate(cardPrefab, spawnParent);
            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();

            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponent<CardVisual>();

            if (controller == null || visual == null)
            {
                Debug.LogError("[PlayerHandManager] Missing required components on card prefab! Destroying card and halting deal.");
                Destroy(newCardObj);
                yield break;
            }

            EndTurnManager endTurnManager = FindFirstObjectByType<EndTurnManager>();
            if (endTurnManager != null) controller.SetEndTurnManager(endTurnManager);

            Sprite sprite = GetCardSprite(result.CardId, result.Deck == DeckType.Action);
            if (result.Deck == DeckType.Action) visual.Setup(result.ActionData, sprite);
            else visual.Setup(result.SupportData, sprite);

            controller.SetCardId(result.CardId);
            if (result.Deck == DeckType.Action && result.ActionData != null)
                controller.SetCategory(result.ActionData.Role == "Attack" ? CardCategory.Attack : CardCategory.Defense);
            else if (result.Deck == DeckType.Support && result.SupportData != null)
            {
                controller.SetCategory(CardCategory.Support);
                controller.SetCardMetadata(result.SupportData.EffectType == "Forever");
            }

            if (timerManager != null)
            {
                controller.SetInteractable(!timerManager.IsDrawPhaseActive);
            }

            // PHASE 1: SPAWN AT BUTTON POSITION
            if (sourceButton != null)
            {
                cardRect.position = sourceButton.position;
            }
            else
            {
                cardRect.position = handTransform.position;
            }

            cardRect.localScale = Vector3.one * 0.3f;
            cardRect.rotation = Quaternion.identity;

            // PHASE 2: TRAVEL TO SCREEN CENTER & REVEAL
            Vector3 centerPosition = targetCanvas != null ? targetCanvas.transform.position : Vector3.zero;
            Vector3 targetCenterScale = Vector3.one * centerRevealScale;

            float movePhaseDuration = (totalDrawDuration - centerPauseDuration) * 0.5f;
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

            // ========================================================================
            // ✨ NEW: TRIGGER RARITY JUICE (SHAKE + AUDIO + UI FLASH) AT CENTER STAGE ✨
            // ========================================================================
            if (JuiceFXManager.Instance != null)
            {
                JuiceFXManager.Instance.TriggerRarityJuice(result.Tier);
            }
            else if (juiceFXManager != null)
            {
                juiceFXManager.TriggerRarityJuice(result.Tier);
            }

            // Play reveal shader / flip effects while centered
            visual.PlayReveal(result.Tier);

            // Pause at center to let player inspect card
            yield return new WaitForSeconds(centerPauseDuration);

            // PHASE 3: MOVE TO HAND & RE-FAN HAND LAYOUT
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

            newCardObj.transform.SetParent(handTransform, false);
            cardRect.localScale = Vector3.one;
            cardRect.anchoredPosition = Vector2.zero;

            cardsInHand.Add(controller);
            UpdateHandLayout();

            Debug.Log($"[PlayerHandManager] Added card '{controller.gameObject.name}' (Instance ID: {controller.GetEntityId()}) to internal hand list. Total: {cardsInHand.Count}");

            yield return new WaitForSeconds(dealDelay);
        }
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        return Resources.Load<Sprite>($"{folder}{cardId}");
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

        Debug.Log($"[PlayerHandManager] GetSelectedCards: Checking {cardsInHand.Count} cards in internal list.");
        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller == null) continue;

            Debug.Log($"[PlayerHandManager] - Checking: '{controller.gameObject.name}' (Card ID: {controller.CardId}), IsSelected: {controller.IsSelected}, IsInteractable: {controller.IsInteractable}");
            if (controller.IsSelected)
            {
                selected.Add(controller);
            }
        }

        Debug.Log($"[PlayerHandManager] Found {selected.Count} selected cards from internal list.");
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
                ids.Add(controller.CardId);
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

            controller.transform.SetParent(selectedHandTarget, true);
            controller.enabled = false;
        }

        UpdateHandLayout();
    }

    public void ReturnSubmittedCardsToHand(RectTransform containerTransform)
    {
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

            controller.transform.SetParent(handTransform, true);
            controller.enabled = true;

            if (controller.IsSelected)
            {
                controller.ForceDeselect();
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

        Debug.Log($"[PlayerHandManager] Draw phase active: {isDrawPhaseActive}. Cards interactable: {shouldBeInteractable}");
    }

    public void UpdateHandLayout()
    {
        CleanupHandList();

        if (cardsInHand.Count == 0) return;

        if (activeFanCoroutine != null)
            StopCoroutine(activeFanCoroutine);

        activeFanCoroutine = StartCoroutine(Routine_AnimateFanLayout());
    }

    private IEnumerator Routine_AnimateFanLayout()
    {
        int cardCount = cardsInHand.Count;

        Vector3[] targetPositions = new Vector3[cardCount];
        Quaternion[] targetRotations = new Quaternion[cardCount];

        if (cardCount == 1)
        {
            targetPositions[0] = Vector3.zero;
            targetRotations[0] = Quaternion.identity;
        }
        else
        {
            float totalWidth = cardSpacing * (cardCount - 1);
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < cardCount; i++)
            {
                float xPos = startX + (i * cardSpacing);
                float normalizedIndex = (2f * i / (cardCount - 1)) - 1f;
                float yPos = -Mathf.Pow(normalizedIndex, 2f) * arcHeight;
                float zAngle = -normalizedIndex * maxFanAngle;

                targetPositions[i] = new Vector3(xPos, yPos, 0f);
                targetRotations[i] = Quaternion.Euler(0f, 0f, zAngle);
            }
        }

        bool isAnimating = true;
        while (isAnimating)
        {
            isAnimating = false;

            for (int i = 0; i < cardsInHand.Count; i++)
            {
                if (cardsInHand[i] == null) continue;

                RectTransform cardRect = cardsInHand[i].GetComponent<RectTransform>();

                cardRect.anchoredPosition = Vector2.Lerp(
                    cardRect.anchoredPosition,
                    targetPositions[i],
                    Time.deltaTime * fanLerpSpeed
                );

                cardRect.localRotation = Quaternion.Slerp(
                    cardRect.localRotation,
                    targetRotations[i],
                    Time.deltaTime * fanLerpSpeed
                );

                if (Vector2.Distance(cardRect.anchoredPosition, targetPositions[i]) > 0.05f ||
                    Quaternion.Angle(cardRect.localRotation, targetRotations[i]) > 0.1f)
                {
                    isAnimating = true;
                }
            }

            yield return null;
        }

        for (int i = 0; i < cardsInHand.Count; i++)
        {
            if (cardsInHand[i] == null) continue;

            RectTransform cardRect = cardsInHand[i].GetComponent<RectTransform>();
            cardRect.anchoredPosition = targetPositions[i];
            cardRect.localRotation = targetRotations[i];
        }
    }
}