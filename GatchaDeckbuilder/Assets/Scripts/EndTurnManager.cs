using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EndTurnManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private RectTransform selectedHandTransform;

    [Header("Player & Rival References")]
    [SerializeField] private PlayerHandManager handManager;
    [SerializeField] private AIRivalController aiRival;

    private void Start()
    {
        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }

        UpdateEndTurnButtonVisibility();
    }

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

        Debug.Log($"[End Turn] Locking in player's {selectedCards.Count} selected cards.");

        // 1. Submit human player's cards to Selected Hand
        handManager.SubmitSelectedCardsToHand(selectedHandTransform);

        // 2. Submit AI Rival's cards simultaneously
        if (aiRival != null)
        {
            aiRival.SubmitRivalHand();
        }

        // 3. Hide button after submission
        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(false);
        }
    }
}