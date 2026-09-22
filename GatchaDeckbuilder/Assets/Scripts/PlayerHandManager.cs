using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform;
    [SerializeField] private Canvas targetCanvas;
    [Tooltip("Invisible raycast target image activated during card reveal to block token/card clicks.")]
    [SerializeField] private GameObject clickBlockerOverlay;

    [Header("Reveal Animation Timings")]
    [SerializeField] private Transform centerPointTarget;
    [SerializeField] private float postRevealPause = 0.35f;

    [Header("Fan Layout Settings")]
    [SerializeField] private float maxFanAngle = 30f;
    [SerializeField] private float cardSpacing = 80f;
    [SerializeField] private float arcHeightDip = 15f;

    [Header("Animation Settings")]
    [SerializeField] private float cardMoveDuration = 0.4f;
    [SerializeField] private float dealDelay = 0.15f;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private GachaManager gachaManager; // Replaced undefined pullController
    [SerializeField] private DeckManager deckManager;   // Added to fetch sprites

    private List<CardUI> cardsInHand = new List<CardUI>();

    private void Awake()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<Canvas>();
            }
        }

        if (clickBlockerOverlay != null)
        {
            clickBlockerOverlay.SetActive(false);
        }
    }

    public void DealCardsFromTokens(int count, DeckType deckToPull)
    {
        // Removed isAI check: Always notify the timer when the local player draws
        if (timerManager != null)
        {
            timerManager.NotifyCardsDrawn();
        }

        StartCoroutine(Routine_DealCards(count, deckToPull));
    }

    private IEnumerator Routine_DealCards(int count, DeckType deckToPull)
    {
        if (gachaManager == null)
        {
            Debug.LogError("[Hand Manager] GachaManager not assigned!");
            yield break;
        }

        if (clickBlockerOverlay != null)
            clickBlockerOverlay.SetActive(true);

        for (int i = 0; i < count; i++)
        {
            // 1. Request the pull based on the enum
            if (deckToPull == DeckType.Support)
                gachaManager.RequestPullSupport();
            else
                gachaManager.RequestPullAction();

            // 2. Wait for the server to resolve the pull and send the ClientRpc event
            PullResult result = null;
            Action<PullResult> onPullResolved = (res) => { result = res; };
            gachaManager.OnMyPullResolved += onPullResolved;

            float timeout = 2f;
            float elapsed = 0f;
            while (result == null && elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            gachaManager.OnMyPullResolved -= onPullResolved;

            if (result == null)
            {
                Debug.LogError("[Hand Manager] Pull timed out or failed!");
                yield break;
            }

            // 3. Instantiate the card
            GameObject newCardObj = Instantiate(cardPrefab, handTransform, false);

            if (targetCanvas != null && !newCardObj.transform.IsChildOf(targetCanvas.transform))
            {
                newCardObj.transform.SetParent(handTransform, false);
            }

            CardUI cardScript = newCardObj.GetComponent<CardUI>();
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();
            CardVisual visual = newCardObj.GetComponent<CardVisual>();

            if (cardScript == null || controller == null || visual == null)
            {
                Debug.LogError("[Hand Manager] Card Prefab is missing CardUI, BalatroCardController, or CardVisual!");
                Destroy(newCardObj);
                yield break;
            }

            // 4. Setup the visual using the new CardVisual component
            Sprite sprite = GetCardSprite(result.CardId, result.Deck == DeckType.Action);
            if (result.Deck == DeckType.Action)
                visual.Setup(result.ActionData, sprite);
            else
                visual.Setup(result.SupportData, sprite);

            controller.SetCardId(result.CardId);

            if (result.Deck == DeckType.Support)
            {
                controller.SetCardMetadata(result.SupportData.EffectType == "Forever");
            }

            // 5. Play the reveal animation
            visual.PlayReveal(result.Tier);

            // Wait for the reveal animation to complete (max duration is ~0.35s for Legendary) + pause
            yield return new WaitForSeconds(0.4f + postRevealPause);

            // 6. Add to hand and fan out
            cardsInHand.Add(cardScript);
            UpdateHandFanLayout();

            yield return new WaitForSeconds(dealDelay);
        }

        if (clickBlockerOverlay != null)
        {
            clickBlockerOverlay.SetActive(false);
        }
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        // Fallback to Resources.load matching your DeckManager structure
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        return Resources.Load<Sprite>($"{folder}{cardId}");
    }

    private void CleanupNullCards()
    {
        cardsInHand.RemoveAll(card => card == null || card.gameObject == null);
    }

    public void UpdateHandFanLayout()
    {
        CleanupNullCards();

        int totalCards = cardsInHand.Count;
        if (totalCards == 0) return;

        for (int i = 0; i < totalCards; i++)
        {
            if (cardsInHand[i] == null) continue;

            float normalizedIndex = (totalCards > 1) ? ((float)i / (totalCards - 1)) - 0.5f : 0f;

            float zRotation = -normalizedIndex * maxFanAngle;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, zRotation);

            float xPos = normalizedIndex * (cardSpacing * Mathf.Min(totalCards, 8));
            float yPos = -Mathf.Abs(normalizedIndex) * arcHeightDip;

            Vector3 targetPosition = new Vector3(xPos, yPos, 0f);

            BalatroCardController controller = cardsInHand[i].GetComponent<BalatroCardController>();
            Vector3 targetScale = (controller != null) ? controller.RestingScale : Vector3.one;

            StartCoroutine(cardsInHand[i].AnimateToHand(targetPosition, targetRotation, targetScale, cardMoveDuration));

            cardsInHand[i].transform.SetAsLastSibling();
        }
    }

    public List<CardUI> GetSelectedCards()
    {
        CleanupNullCards();

        List<CardUI> selected = new List<CardUI>();
        foreach (CardUI card in cardsInHand)
        {
            if (card == null) continue;

            BalatroCardController controller = card.GetComponent<BalatroCardController>();
            if (controller != null && controller.IsSelected)
            {
                selected.Add(card);
            }
        }
        return selected;
    }

    public void SubmitSelectedCardsToHand(RectTransform selectedHandTarget)
    {
        List<CardUI> selectedCards = GetSelectedCards();

        for (int i = 0; i < selectedCards.Count; i++)
        {
            CardUI card = selectedCards[i];
            if (card == null) continue;

            cardsInHand.Remove(card);
            card.transform.SetParent(selectedHandTarget, true);

            BalatroCardController controller = card.GetComponent<BalatroCardController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            float spacing = 90f;
            float xPos = (i - (selectedCards.Count - 1) / 2f) * spacing;
            Vector3 targetPos = new Vector3(xPos, 0f, 0f);

            StartCoroutine(card.AnimateToHand(targetPos, Quaternion.identity, (controller != null) ? controller.RestingScale : Vector3.one, cardMoveDuration));
        }

        UpdateHandFanLayout();
    }

    public void ReturnSubmittedCardsToHand(RectTransform containerTransform)
    {
        if (containerTransform == null) return;

        List<CardUI> submittedCards = new List<CardUI>(containerTransform.GetComponentsInChildren<CardUI>());
        if (submittedCards.Count == 0) return;

        foreach (CardUI card in submittedCards)
        {
            if (card == null || card.gameObject == null) continue;

            card.transform.SetParent(handTransform, true);

            BalatroCardController controller = card.GetComponent<BalatroCardController>();
            if (controller != null)
            {
                controller.enabled = true;
                if (controller.IsSelected)
                {
                    controller.ToggleSelection();
                }
            }

            if (!cardsInHand.Contains(card))
            {
                cardsInHand.Add(card);
            }
        }

        UpdateHandFanLayout();
    }

    public void ClearSubmittedCardsJuicy(RectTransform containerTransform, float delay = 0f)
    {
        StartCoroutine(Routine_ClearCardsJuicy(containerTransform, delay));
    }

    private IEnumerator Routine_ClearCardsJuicy(RectTransform containerTransform, float delay)
    {
        if (containerTransform == null) yield break;

        if (delay > 0f) yield return new WaitForSeconds(delay);

        List<CardUI> cardsToClear = new List<CardUI>(containerTransform.GetComponentsInChildren<CardUI>());
        if (cardsToClear.Count == 0) yield break;

        float popUpDuration = 0.12f;
        float shrinkDuration = 0.18f;
        Vector3 popScale = new Vector3(1.3f, 1.3f, 1f);

        float elapsed = 0f;
        while (elapsed < popUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / popUpDuration;

            foreach (CardUI card in cardsToClear)
            {
                if (card != null && card.gameObject != null)
                {
                    card.transform.localScale = Vector3.Lerp(Vector3.one, popScale, t);
                }
            }
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;

            foreach (CardUI card in cardsToClear)
            {
                if (card != null && card.gameObject != null)
                {
                    card.transform.localScale = Vector3.Lerp(popScale, Vector3.zero, t);
                }
            }
            yield return null;
        }

        foreach (CardUI card in cardsToClear)
        {
            if (card != null)
            {
                cardsInHand.Remove(card);
                Destroy(card.gameObject);
            }
        }

        CleanupNullCards();
    }
    public List<int> GetSelectedCardIds()
    {
        CleanupNullCards();
        List<int> ids = new List<int>();
        foreach (CardUI card in cardsInHand)
        {
            if (card == null) continue;
            BalatroCardController controller = card.GetComponent<BalatroCardController>();
            if (controller != null && controller.IsSelected)
            {
                ids.Add(controller.CardId);
            }
        }
        return ids;
    }
}