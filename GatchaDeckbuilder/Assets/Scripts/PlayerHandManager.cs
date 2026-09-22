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

    [Header("Pull System Reference")]
    [Tooltip("The real pull engine — reads real card data, handles pity and the 50/50 correctly.")]
    [SerializeField] private PlayerPullController pullController;

    [Header("Reveal Animation Timings")]
    [SerializeField] private Transform centerPointTarget;
    [SerializeField] private float moveToCenterDuration = 0.45f;
    [SerializeField] private float shrinkDuration = 0.12f;
    [SerializeField] private float overshootDuration = 0.12f;
    [SerializeField] private float returnToNormalDuration = 0.15f;
    [SerializeField] private float postRevealPause = 0.35f;

    [Header("Fan Layout Settings")]
    [SerializeField] private float maxFanAngle = 30f;
    [SerializeField] private float cardSpacing = 80f;
    [SerializeField] private float arcHeightDip = 15f;

    [Header("Animation Settings")]
    [SerializeField] private float cardMoveDuration = 0.4f;
    [SerializeField] private float dealDelay = 0.15f;

    [Header("Identity Config")]
    [SerializeField] private bool isAI = false;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private PityManager pityManager;

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

    public int GetHandCount()
    {
        CleanupNullCards();
        return cardsInHand.Count;
    }

    public void DealCardsFromTokens(int count, DeckButton selectedDeck)
    {
        if (!isAI && timerManager != null)
        {
            timerManager.NotifyCardsDrawn();
        }

        StartCoroutine(Routine_DealCards(count, selectedDeck));
    }

    private IEnumerator Routine_DealCards(int count, DeckButton selectedDeck)
    {
        if (selectedDeck == null)
        {
            Debug.LogError("[Hand Manager] Selected Deck is null!");
            yield break;
        }

        if (pullController == null)
        {
            Debug.LogError("[Hand Manager] No PlayerPullController assigned — cannot perform a real pull.");
            yield break;
        }

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

            PullResult result = selectedDeck.IsSupportDeck
                ? pullController.PullSupport()
                : pullController.PullAction();

            CardCategory cardCategory;
            int value = 0;
            string effect = "";
            bool isForever = false;

            if (result.Deck == DeckType.Action)
            {
                cardCategory = result.ActionData.Role == "Attack" ? CardCategory.Attack : CardCategory.Defense;
                value = result.ActionData.Value;
            }
            else
            {
                cardCategory = CardCategory.Support;
                effect = result.SupportData.Effect;
                isForever = result.SupportData.EffectType == "Forever";
            }

            controller.ApplyPulledCardData(result.CardName, cardCategory, result.Tier, value, effect, isForever);

            if (pityManager != null)
            {
                pityManager.RegisterPull(result.Tier);
            }

            cardRect.localScale = Vector3.one;
            cardRect.position = selectedDeck.DeckTransform.position;

            Vector3 localPos = cardRect.localPosition;
            localPos.z = 0f;
            cardRect.localPosition = localPos;

            newCardObj.transform.SetAsLastSibling();

            if (isAI)
            {
                controller.SetCardObscured(true);
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
                controller.RevealCardVisuals();
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
}