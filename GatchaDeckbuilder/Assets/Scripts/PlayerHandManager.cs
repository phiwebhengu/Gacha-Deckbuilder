using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform;  // Parent object representing the hand
    [SerializeField] private Canvas targetCanvas;          // Main UI Canvas
    [Tooltip("Invisible raycast target image activated during card reveal to block token/card clicks.")]
    [SerializeField] private GameObject clickBlockerOverlay;

    [Header("Reveal Animation Timings")]
    [Tooltip("Center world point override. If left null, Screen center will be used automatically.")]
    [SerializeField] private Transform centerPointTarget;
    [SerializeField] private float moveToCenterDuration = 0.45f;
    [SerializeField] private float shrinkDuration = 0.12f;
    [SerializeField] private float overshootDuration = 0.12f;
    [SerializeField] private float returnToNormalDuration = 0.15f;
    [SerializeField] private float postRevealPause = 0.35f;

    [Header("Fan Layout Settings")]
    [Tooltip("Maximum arc spread angle for the outer cards")]
    [SerializeField] private float maxFanAngle = 30f;

    [Tooltip("Horizontal spacing offset between cards")]
    [SerializeField] private float cardSpacing = 80f;

    [Tooltip("Slight downward dip for outer cards to create an arc")]
    [SerializeField] private float arcHeightDip = 15f;

    [Header("Animation Settings")]
    [Tooltip("Time it takes for a single card to reach the hand")]
    [SerializeField] private float cardMoveDuration = 0.4f;

    [Tooltip("Delay between spawning consecutive cards from the deck")]
    [SerializeField] private float dealDelay = 0.15f;

    [Header("Identity Config")]
    [Tooltip("Check this TRUE on the AI Hand Manager instance to skip close-up reveal sequences and screen shake.")]
    [SerializeField] private bool isAI = false;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private PityManager pityManager;
    [SerializeField] private PullConfig pullConfig; 


    private List<CardUI> cardsInHand = new List<CardUI>();

    private void Awake()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindObjectOfType<Canvas>();
            }
        }

        if (clickBlockerOverlay != null)
        {
            clickBlockerOverlay.SetActive(false);
        }
    }

    public void DealCardsFromTokens(int count, RectTransform spawnDeckTransform, DeckType deckType)
    {
        if (!isAI && timerManager != null)
        {
            timerManager.NotifyCardsDrawn();
        }

        StartCoroutine(Routine_DealCards(count, spawnDeckTransform, deckType));
    }

    private IEnumerator Routine_DealCards(int count, RectTransform spawnDeckTransform, DeckType deckType)
    {
        if (!isAI && clickBlockerOverlay != null)
        {
            clickBlockerOverlay.SetActive(true);
        }

        Vector3 screenCenterWorldPos = (centerPointTarget != null)
            ? centerPointTarget.position
            : targetCanvas.transform.position;

        for (int i = 0; i < count; i++)
        {
            GameObject newCardObj = Instantiate(cardPrefab, handTransform, false);

            if (targetCanvas != null && !newCardObj.transform.IsChildOf(targetCanvas.transform))
            {
                newCardObj.transform.SetParent(handTransform, false);
            }

            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
            CardUI cardScript = newCardObj.GetComponent<CardUI>();
            BalatroCardController controller = newCardObj.GetComponent<BalatroCardController>();

            if (cardRect == null || cardScript == null || controller == null)
            {
                Debug.LogError("[Hand Manager] Card Prefab is missing components!");
                yield break;
            }

            // Single initialization call for both Player and AI:
            controller.InitializeCardCategoryAndRarity(pityManager, null, pullConfig);

            cardRect.localScale = Vector3.one;
            cardRect.position = spawnDeckTransform.position;

            Vector3 localPos = cardRect.localPosition;
            localPos.z = 0f;
            cardRect.localPosition = localPos;

            newCardObj.transform.SetAsLastSibling();

            if (isAI)
            {
                cardsInHand.Add(cardScript);
                UpdateHandFanLayout();
            }
            else
            {
                controller.PrepareForUnrevealedSpawn();

                yield return StartCoroutine(controller.Routine_AnimateCenterReveal(
                    screenCenterWorldPos,
                    moveToCenterDuration,
                    shrinkDuration,
                    overshootDuration,
                    returnToNormalDuration
                ));

                yield return new WaitForSeconds(postRevealPause);

                cardsInHand.Add(cardScript);
                UpdateHandFanLayout();
            }

            yield return new WaitForSeconds(dealDelay);
        }

        if (!isAI && clickBlockerOverlay != null)
        {
            clickBlockerOverlay.SetActive(false);
        }
    }

    public void UpdateHandFanLayout()
    {
        int totalCards = cardsInHand.Count;
        if (totalCards == 0) return;

        for (int i = 0; i < totalCards; i++)
        {
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
        List<CardUI> selected = new List<CardUI>();
        foreach (CardUI card in cardsInHand)
        {
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

            StartCoroutine(card.AnimateToHand(targetPos, Quaternion.identity, controller.RestingScale, cardMoveDuration));
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
            if (card == null) continue;

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
                if (card != null) card.transform.localScale = Vector3.Lerp(Vector3.one, popScale, t);
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
                if (card != null) card.transform.localScale = Vector3.Lerp(popScale, Vector3.zero, t);
            }
            yield return null;
        }

        foreach (CardUI card in cardsToClear)
        {
            if (card != null) Destroy(card.gameObject);
        }
    }
}