using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EndTurnManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private RectTransform selectedHandTransform;

    [Header("Hand Reference")]
    [SerializeField] private PlayerHandManager handManager;

    private void Start()
    {
        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }

        // Hide button on start
        UpdateEndTurnButtonVisibility();
    }

    // Call this whenever card selection changes or cards are dealt
    public void UpdateEndTurnButtonVisibility()
    {
        if (handManager == null || endTurnButton == null) return;

        bool hasSelectedCards = handManager.GetSelectedCards().Count > 0;
        endTurnButton.gameObject.SetActive(hasSelectedCards);
    }

    private void OnEndTurnClicked()
    {
        if (handManager == null || selectedHandTransform == null) return;

        List<CardUI> selectedCards = handManager.GetSelectedCards();

        if (selectedCards.Count == 0) return;

        Debug.Log($"[End Turn] Locking in {selectedCards.Count} selected cards.");

        // Move selected cards to the Selected Hand UI area
        handManager.SubmitSelectedCardsToHand(selectedHandTransform);

        // Hide button after submission
        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(false);
        }
    }
}