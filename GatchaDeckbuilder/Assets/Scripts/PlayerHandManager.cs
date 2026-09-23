using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform; // Must have HorizontalLayoutGroup
    [SerializeField] private Canvas targetCanvas;

    [Header("Animation Settings")]
    [SerializeField] private float dealDelay = 0.15f;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private GachaManager gachaManager;
    [SerializeField] private EndTurnManager endTurnManager;

    // Changed from List<CardUI> to List<BalatroCardController>
    private List<BalatroCardController> cardsInHand = new List<BalatroCardController>();

    private void Awake()
    {
        if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
    }

    public void DealCardsFromTokens(int count, DeckType deckToPull)
    {
        timerManager?.NotifyCardsDrawn();
        StartCoroutine(Routine_DealCards(count, deckToPull));
    }

    private IEnumerator Routine_DealCards(int count, DeckType deckToPull)
    {
        if (gachaManager == null)
        {
            Debug.LogError("[PlayerHandManager] gachaManager is null!");
            yield break;
        }

        for (int i = 0; i < count; i++)
        {
            if (deckToPull == DeckType.Support) gachaManager.RequestPullSupport();
            else gachaManager.RequestPullAction();

            PullResult result = null;
            Action<PullResult> onPullResolved = (res) => { result = res; };
            gachaManager.OnMyPullResolved += onPullResolved;

            float timeout = 2f, elapsed = 0f;
            while (result == null && elapsed < timeout) { yield return null; elapsed += Time.deltaTime; }
            gachaManager.OnMyPullResolved -= onPullResolved;

            if (result == null)
            {
                Debug.LogError("[PlayerHandManager] Pull result is null after timeout!");
                yield break;
            }

            GameObject newCardObj = Instantiate(cardPrefab, handTransform);
            if (targetCanvas != null && !newCardObj.transform.IsChildOf(targetCanvas.transform))
                newCardObj.transform.SetParent(handTransform, false);

            // Removed CardUI reference, relying directly on BalatroCardController and CardVisual
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponent<CardVisual>();

            Debug.Log($"[PlayerHandManager] Dealt card {i + 1}. Controller: {controller != null}, Visual: {visual != null}");

            if (controller == null || visual == null)
            {
                Debug.LogError("[PlayerHandManager] Missing required components on card prefab! Destroying card and halting deal.");
                Destroy(newCardObj);
                yield break;
            }

            if (endTurnManager != null)
            {
                controller.SetEndTurnManager(endTurnManager);
            }

            Sprite sprite = GetCardSprite(result.CardId, result.Deck == DeckType.Action);
            if (result.Deck == DeckType.Action) visual.Setup(result.ActionData, sprite);
            else visual.Setup(result.SupportData, sprite);

            controller.SetCardId(result.CardId);
            if (result.Deck == DeckType.Support) controller.SetCardMetadata(result.SupportData.EffectType == "Forever");

            visual.PlayReveal(result.Tier);

            // INSTANT SCALE (No animation)
            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
            cardRect.localScale = Vector3.one;

            cardsInHand.Add(controller);
            Debug.Log($"[PlayerHandManager] Successfully added card to hand. Total cards in hand: {cardsInHand.Count}");

            yield return new WaitForSeconds(dealDelay);
        }
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        return Resources.Load<Sprite>($"{folder}{cardId}");
    }

    private void CleanupNullCards()
    {
        int initialCount = cardsInHand.Count;
        // Safely remove only null references
        cardsInHand.RemoveAll(controller => controller == null);

        if (cardsInHand.Count < initialCount)
        {
            Debug.LogWarning($"[PlayerHandManager] Cleaned up {initialCount - cardsInHand.Count} null card references. Something is destroying cards prematurely.");
        }
    }

    public void SetAllCardsInteractable(bool state)
    {
        CleanupNullCards();
        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller != null) controller.SetInteractable(state);
        }
    }

    // Return type changed from List<CardUI> to List<BalatroCardController>
    public List<BalatroCardController> GetSelectedCards()
    {
        CleanupNullCards();
        Debug.Log($"[PlayerHandManager] GetSelectedCards called. cardsInHand.Count = {cardsInHand.Count}");

        List<BalatroCardController> selected = new List<BalatroCardController>();
        foreach (BalatroCardController controller in cardsInHand)
        {
            if (controller != null && controller.IsSelected)
            {
                selected.Add(controller);
            }
        }

        Debug.Log($"[PlayerHandManager] Found {selected.Count} selected cards.");
        return selected;
    }

    public List<int> GetSelectedCardIds()
    {
        CleanupNullCards();
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
            controller.transform.SetParent(selectedHandTarget, true);
            controller.enabled = false;
        }
        Debug.Log($"[PlayerHandManager] Submitted cards. Remaining in hand: {cardsInHand.Count}");
    }

    public void ReturnSubmittedCardsToHand(RectTransform containerTransform)
    {
        if (containerTransform == null) return;

        List<BalatroCardController> submittedCards = new List<BalatroCardController>(containerTransform.GetComponentsInChildren<BalatroCardController>());

        foreach (BalatroCardController controller in submittedCards)
        {
            if (controller == null) continue;

            controller.transform.SetParent(handTransform, true);
            controller.enabled = true;

            if (controller.IsSelected)
            {
                controller.ToggleSelection();
            }

            if (!cardsInHand.Contains(controller))
            {
                cardsInHand.Add(controller);
            }
        }
        Debug.Log($"[PlayerHandManager] Returned cards to hand. Total in hand: {cardsInHand.Count}");
    }

    public void ClearSubmittedCardsJuicy(RectTransform containerTransform, float delay = 0f) => StartCoroutine(Routine_ClearCardsJuicy(containerTransform, delay));

    private IEnumerator Routine_ClearCardsJuicy(RectTransform containerTransform, float delay)
    {
        if (containerTransform == null) yield break;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        List<BalatroCardController> cardsToClear = new List<BalatroCardController>(containerTransform.GetComponentsInChildren<BalatroCardController>());
        foreach (BalatroCardController controller in cardsToClear)
        {
            if (controller != null)
            {
                cardsInHand.Remove(controller);
                Destroy(controller.gameObject);
            }
        }
        CleanupNullCards();
        Debug.Log($"[PlayerHandManager] Cleared submitted cards. Remaining in hand: {cardsInHand.Count}");
    }
}