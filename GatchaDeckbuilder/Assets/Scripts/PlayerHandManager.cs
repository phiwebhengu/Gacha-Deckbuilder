using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform;
    [SerializeField] private Canvas targetCanvas;

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

    private List<CardUI> cardsInHand = new List<CardUI>();

    private void Awake()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null) targetCanvas = FindObjectOfType<Canvas>();
        }
    }

    public void DealPulledCards(List<PullResult> results, RectTransform spawnDeckTransform)
    {
        if (!isAI && timerManager != null)
        {
            timerManager.NotifyCardsDrawn();
        }

        StartCoroutine(Routine_DealCards(results, spawnDeckTransform));
    }

    private IEnumerator Routine_DealCards(List<PullResult> results, RectTransform spawnDeckTransform)
    {
        foreach (PullResult result in results)
        {
            GameObject newCardObj = Instantiate(cardPrefab, handTransform, false);

            if (targetCanvas != null && !newCardObj.transform.IsChildOf(targetCanvas.transform))
                newCardObj.transform.SetParent(handTransform, false);

            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
            CardUI cardScript = newCardObj.GetComponent<CardUI>();
            CardVisual visual = newCardObj.GetComponent<CardVisual>();

            if (cardRect == null || cardScript == null)
            {
                Debug.LogError("[Hand Manager] Card Prefab is missing RectTransform or CardUI script!");
                yield break;
            }

            if (visual != null)
            {
                Sprite sprite = result.Deck == DeckType.Action
                    ? CardAssetRegistry.Instance.GetActionSprite(result.CardId)
                    : CardAssetRegistry.Instance.GetSupportSprite(result.CardId);

                if (result.Deck == DeckType.Action)
                    visual.Setup(result.ActionData, sprite);
                else
                    visual.Setup(result.SupportData, sprite);
            }

            cardRect.localScale = Vector3.one;
            cardRect.position = spawnDeckTransform.position;
            Vector3 localPos = cardRect.localPosition;
            localPos.z = 0f;
            cardRect.localPosition = localPos;

            newCardObj.transform.SetAsLastSibling();
            cardsInHand.Add(cardScript);

            PullRevealJuice juice = newCardObj.GetComponent<PullRevealJuice>();
            if (juice != null) juice.PlayReveal(result.Tier);

            UpdateHandFanLayout();
            yield return new WaitForSeconds(dealDelay);
        }
    }

    private void UpdateHandFanLayout()
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
            if (controller != null && controller.IsSelected) selected.Add(card);
        }
        return selected;
    }
}