using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHandManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform deckTransform;  // Position where cards spawn
    [SerializeField] private RectTransform handTransform;  // Parent object representing the hand
    [SerializeField] private Canvas targetCanvas;          // Your main UI Canvas

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

    private List<CardUI> cardsInHand = new List<CardUI>();

    private void Awake()
    {
        // Auto-find Canvas if not manually assigned
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindObjectOfType<Canvas>();
            }
        }
    }

    public void DealCardsFromTokens(int count)
    {
        StartCoroutine(Routine_DealCards(count));
    }

    private IEnumerator Routine_DealCards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // 1. Force instantiation directly as a child of handTransform (which must be inside Canvas)
            GameObject newCardObj = Instantiate(cardPrefab, handTransform, false);

            // Re-parent explicitly to ensure UI rendering pipeline detects it under Canvas
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

            // 2. Set scale to 1 and position at Deck UI location
            cardRect.localScale = Vector3.one;
            cardRect.position = deckTransform.position;

            // Reset Z coordinate so it doesn't clip behind the Canvas plane
            Vector3 localPos = cardRect.localPosition;
            localPos.z = 0f;
            cardRect.localPosition = localPos;

            // 3. Bring card to the front of the UI draw order
            newCardObj.transform.SetAsLastSibling();

            cardsInHand.Add(cardScript);

            // 4. Update dynamic fan positions
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

            // Animate card into hand
            StartCoroutine(cardsInHand[i].AnimateToHand(targetPosition, targetRotation, cardMoveDuration));

            // Ensure cards draw left-to-right correctly
            cardsInHand[i].transform.SetAsLastSibling();
        }
    }
}