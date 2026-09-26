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

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private GachaManager gachaManager;

    private List<BalatroCardController> cardsInHand = new List<BalatroCardController>();
    private List<BalatroCardController> submittedCards = new List<BalatroCardController>();
    private Queue<PullResult> pendingPulls = new Queue<PullResult>();

    private void OnEnable()
    {
        if (gachaManager != null)
            gachaManager.OnMyPullResolved += HandlePullResolved;
    }

    private void OnDisable()
    {
        if (gachaManager != null)
            gachaManager.OnMyPullResolved -= HandlePullResolved;
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
    }

    public void DealCardsFromTokens(int count, DeckType deckToPull)
    {
        timerManager?.NotifyCardsDrawn();
        StartCoroutine(Routine_DealCards(count, deckToPull));
    }

    private IEnumerator Routine_DealCards(int count, DeckType deckToPull)
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

            GameObject newCardObj = Instantiate(cardPrefab, handTransform);
            if (targetCanvas != null && !newCardObj.transform.IsChildOf(targetCanvas.transform))
                newCardObj.transform.SetParent(handTransform, false);

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

            visual.PlayReveal(result.Tier);
            newCardObj.GetComponent<RectTransform>().localScale = Vector3.one;

            cardsInHand.Add(controller);
            // DIAGNOSTIC LOG: Confirms the card was added to the list with its Instance ID
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
        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller != null) controller.SetInteractable(state);
        }
    }

    public List<BalatroCardController> GetSelectedCards()
    {
        List<BalatroCardController> selected = new List<BalatroCardController>();

        // DIAGNOSTIC LOG: Dumps the entire state of the internal list
        Debug.Log($"[PlayerHandManager] GetSelectedCards: Checking {cardsInHand.Count} cards in internal list.");
        foreach (BalatroCardController controller in cardsInHand)
        {
            Debug.Log($"[PlayerHandManager] - Checking: '{controller.gameObject.name}' (Instance ID: {controller.GetEntityId()}), IsSelected: {controller.IsSelected}, IsInteractable: {controller.IsInteractable}");
            if (controller != null && controller.IsSelected)
            {
                selected.Add(controller);
            }
        }

        Debug.Log($"[PlayerHandManager] Found {selected.Count} selected cards from internal list.");
        return selected;
    }

    public List<int> GetSelectedCardIds()
    {
        List<int> ids = new List<int>();
        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller != null && controller.IsSelected)
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
    }

    public void ReturnSubmittedCardsToHand(RectTransform containerTransform)
    {
        List<BalatroCardController> cardsToReturn = new List<BalatroCardController>(submittedCards);

        foreach (BalatroCardController controller in cardsToReturn)
        {
            if (controller == null) continue;

            submittedCards.Remove(controller);
            cardsInHand.Add(controller);

            controller.transform.SetParent(handTransform, true);
            controller.enabled = true;

            if (controller.IsSelected)
            {
                controller.ToggleSelection(); // Deselect it upon return
            }
        }
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
}