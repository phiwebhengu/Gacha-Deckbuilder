using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform handTransform;  // Parent object representing the hand
    [SerializeField] private Canvas targetCanvas;          // Main UI Canvas

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
    [SerializeField] private bool isAI = false;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;

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
        for (int i = 0; i < count; i++)
        {
            // 1. Force instantiation directly as child of handTransform
            GameObject newCardObj = Instantiate(cardPrefab, handTransform, false);

            if (targetCanvas != null && !newCardObj.transform.IsChildOf(targetCanvas.transform))
            {
                newCardObj.transform.SetParent(handTransform, false);
            }

            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
            CardUI cardScript = newCardObj.GetComponent<CardUI>();

            if (cardRect == null || cardScript == null)
            {
                Debug.LogError("[Hand Manager] Card Prefab is missing RectTransform or CardUI script!");
                yield break;
            }

            // 2. Position card at chosen Deck UI location
            cardRect.localScale = Vector3.one;
            cardRect.position = spawnDeckTransform.position;

            // Reset Z coordinate to avoid UI clipping
            Vector3 localPos = cardRect.localPosition;
            localPos.z = 0f;
            cardRect.localPosition = localPos;

            // 3. Bring card to front of canvas
            newCardObj.transform.SetAsLastSibling();

            cardsInHand.Add(cardScript);

            // 4. Update dynamic fan layout
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

            // Rotation
            float zRotation = -normalizedIndex * maxFanAngle;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, zRotation);

            // Horizontal position
            float xPos = normalizedIndex * (cardSpacing * Mathf.Min(totalCards, 8));

            // Arc dip
            float yPos = -Mathf.Abs(normalizedIndex) * arcHeightDip;

            Vector3 targetPosition = new Vector3(xPos, yPos, 0f);

            // Scale fetch
            BalatroCardController controller = cardsInHand[i].GetComponent<BalatroCardController>();
            Vector3 targetScale = (controller != null) ? controller.RestingScale : Vector3.one;

            // Animate into fan layout
            StartCoroutine(cardsInHand[i].AnimateToHand(targetPosition, targetRotation, targetScale, cardMoveDuration));

            // Draw order left-to-right
            cardsInHand[i].transform.SetAsLastSibling();
        }
    }

    // Helper to retrieve currently selected cards[cite: 4]
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

    // Moves selected cards out of the hand into the Selected Hand transform
    public void SubmitSelectedCardsToHand(RectTransform selectedHandTarget)
    {
        List<CardUI> selectedCards = GetSelectedCards();

        for (int i = 0; i < selectedCards.Count; i++)
        {
            CardUI card = selectedCards[i];

            // Remove from current active hand layout list[cite: 4]
            cardsInHand.Remove(card);

            // Reparent to the Selected Hand UI area[cite: 4]
            card.transform.SetParent(selectedHandTarget, true);

            // Disable Balatro controller interactions once locked in
            BalatroCardController controller = card.GetComponent<BalatroCardController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            // Calculate horizontal offset spacing inside the selected hand area
            float spacing = 90f;
            float xPos = (i - (selectedCards.Count - 1) / 2f) * spacing;
            Vector3 targetPos = new Vector3(xPos, 0f, 0f);

            StartCoroutine(card.AnimateToHand(targetPos, Quaternion.identity, controller.RestingScale, cardMoveDuration));
        }

        // Re-fan the remaining cards left in the player hand[cite: 4]
        UpdateHandFanLayout();
    }
}