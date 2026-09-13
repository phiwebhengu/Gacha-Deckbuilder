using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro; // Added for TextMeshPro HP displays

public class EndTurnManager : MonoBehaviour
{
    [Header("Health & Points Settings")]
    [SerializeField] private int playerMaxHP = 20;
    [SerializeField] private int aiMaxHP = 20;

    [Header("HP UI References (Optional)")]
    [SerializeField] private TextMeshProUGUI playerHPText;
    [SerializeField] private TextMeshProUGUI aiHPText;

    [Header("UI References")]
    [SerializeField] private Button endTurnButton;
    [SerializeField] private RectTransform selectedHandTransform;

    [Tooltip("Transparent UI Image with Raycast Target enabled to block card interaction after locking in.")]
    [SerializeField] private Image raycastBlockerImage;

    [Header("Player & Rival References")]
    [SerializeField] private PlayerHandManager handManager;
    [SerializeField] private AIRivalController aiRival;

    // Current runtime HP state
    private int currentPlayerHP;
    private int currentAIHP;

    public int CurrentPlayerHP => currentPlayerHP;
    public int CurrentAIHP => currentAIHP;

    private void Start()
    {
        // Initialize starting HP values
        currentPlayerHP = playerMaxHP;
        currentAIHP = aiMaxHP;
        UpdateHPUI();

        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }

        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.gameObject.SetActive(false);
        }

        UpdateEndTurnButtonVisibility();
    }

    private void Update()
    {
        // Check for 'R' key press using the new Input System
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
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

        List<CardUI> playerSelectedCards = handManager.GetSelectedCards();
        if (playerSelectedCards.Count == 0) return;

        Debug.Log($"[End Turn] Locking in player's {playerSelectedCards.Count} selected cards.");

        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.gameObject.SetActive(true);
        }

        // 1. Calculate Player Total Attack Value before reparenting
        int playerTotalAttack = CalculateTotalAttack(playerSelectedCards);

        // 2. Submit human player's cards to Selected Hand
        handManager.SubmitSelectedCardsToHand(selectedHandTransform);

        // 3. Submit AI Rival's cards and calculate AI Total Attack Value
        int aiTotalAttack = 0;
        if (aiRival != null)
        {
            PlayerHandManager aiHandManager = aiRival.GetComponentInChildren<PlayerHandManager>();
            if (aiHandManager != null)
            {
                List<CardUI> aiSelectedCards = aiHandManager.GetSelectedCards();
                aiTotalAttack = CalculateTotalAttack(aiSelectedCards);
            }

            aiRival.SubmitRivalHand();
        }

        // 4. Resolve round evaluation and apply difference as HP reduction
        ResolveRoundCombat(playerTotalAttack, aiTotalAttack);

        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(false);
        }
    }

    private int CalculateTotalAttack(List<CardUI> cards)
    {
        int total = 0;

        foreach (CardUI card in cards)
        {
            BalatroCardController cardController = card.GetComponent<BalatroCardController>();
            if (cardController != null && cardController.Category == CardCategory.Attack)
            {
                total += cardController.CardValue;
            }
        }

        return total;
    }

    /// <summary>
    /// Calculates hand difference and reduces HP of the lower-scoring side.
    /// </summary>
    private void ResolveRoundCombat(int playerScore, int aiScore)
    {
        int difference = Mathf.Abs(playerScore - aiScore);

        Debug.Log($"[Round Result] Scores -> Player: {playerScore} | AI Rival: {aiScore} (Difference: {difference})");

        if (playerScore > aiScore)
        {
            currentAIHP = Mathf.Max(0, currentAIHP - difference);
            Debug.Log($"[Round Result] Player wins! AI loses {difference} HP. AI HP remaining: {currentAIHP}");
        }
        else if (aiScore > playerScore)
        {
            currentPlayerHP = Mathf.Max(0, currentPlayerHP - difference);
            Debug.Log($"[Round Result] AI Rival wins! Player loses {difference} HP. Player HP remaining: {currentPlayerHP}");
        }
        else
        {
            Debug.Log("[Round Result] IT'S A TIE! No HP was deducted.");
        }

        UpdateHPUI();

        // Check Win/Loss states
        if (currentAIHP <= 0)
        {
            Debug.Log("[Game Over] PLAYER VICTORY!");
        }
        else if (currentPlayerHP <= 0)
        {
            Debug.Log("[Game Over] AI RIVAL VICTORY!");
        }
    }

    private void UpdateHPUI()
    {
        if (playerHPText != null)
        {
            playerHPText.text = $"{currentPlayerHP}";
        }

        if (aiHPText != null)
        {
            aiHPText.text = $"{currentAIHP}";
        }
    }

    public void ResetTurnBlocker()
    {
        if (raycastBlockerImage != null)
        {
            raycastBlockerImage.gameObject.SetActive(false);
        }
    }
}