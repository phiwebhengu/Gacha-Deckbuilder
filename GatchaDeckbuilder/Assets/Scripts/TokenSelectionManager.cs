using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TokenSelectionManager : MonoBehaviour
{
    [Header("Identity Config")]
    [Tooltip("Check this if this Token Manager belongs to the AI Rival")]
    [SerializeField] private bool isAI = false;

    [Header("Token Pool")]
    public List<TokenButton> tokenButtons = new List<TokenButton>();
    public Transform tokenContainer;

    [Header("Hand Reference")]
    [SerializeField] private PlayerHandManager handManager;

    [Header("System References")]
    [SerializeField] private DrawTimerManager timerManager;
    [SerializeField] private PityManager pityManager;
    [SerializeField] private PlayerPullController pullController;

    public int RemainingTokenCount => tokenButtons.Count;

    private void Awake()
    {
        if (pityManager == null) pityManager = FindFirstObjectByType<PityManager>();
        if (pullController == null) pullController = FindFirstObjectByType<PlayerPullController>();
    }

    private void Start()
    {
        InitializeTokenButtons();
    }

    public void InitializeTokenButtons()
    {
        if (tokenContainer != null)
        {
            tokenButtons.Clear();
            int index = 1;
            foreach (Transform child in tokenContainer)
            {
                TokenButton btn = child.GetComponent<TokenButton>();
                if (btn != null)
                {
                    btn.Setup(index, this);
                    tokenButtons.Add(btn);
                    index++;
                }
            }
        }
    }

    /// <summary>
    /// Maintains remaining token pool across rounds without resetting the balance.
    /// </summary>
    public void ResetTokensForNewRound()
    {
        tokenButtons.RemoveAll(btn => btn == null);
        ReindexTokens();

        Debug.Log($"[Token System] {(isAI ? "AI" : "Player")} entering new round with {tokenButtons.Count} remaining tokens.");
    }

    /// <summary>
    /// Called when a player clicks directly on a deck. Consumes 1 token and draws 1 card.
    /// </summary>
    public void OnDeckSelected(DeckButton deck)
    {
        tokenButtons.RemoveAll(btn => btn == null);

        if (tokenButtons.Count <= 0)
        {
            Debug.LogWarning($"[Token System] {(isAI ? "AI" : "Player")} has no tokens left to draw cards!");
            return;
        }

        // Deal 1 card passing the entire deck object
        if (handManager != null)
        {
            handManager.DealCardsFromTokens(1, deck);
        }

        // Consume and destroy 1 token from the collection
        DepleteSingleToken();

        // Notify timer if human player
        if (!isAI && timerManager != null)
        {
            timerManager.NotifyCardsDrawn();
        }

        // Update pity UI if reference exists
        if (pityManager != null && pullController != null)
        {
            if (deck.Type == DeckType.Action)
            {
                pityManager.UpdatePityDisplay(pullController.ActionPityCount);
            }
            else
            {
                pityManager.UpdatePityDisplay(pullController.SupportPityCount);
            }
        }
    }

    public void ForceAutoDrawSingleToken(DeckButton targetDeck)
    {
        OnDeckSelected(targetDeck);
    }

    private void DepleteSingleToken()
    {
        if (tokenButtons.Count == 0) return;

        int lastIndex = tokenButtons.Count - 1;
        TokenButton btnToDestroy = tokenButtons[lastIndex];
        tokenButtons.RemoveAt(lastIndex);

        if (btnToDestroy != null)
        {
            Destroy(btnToDestroy.gameObject);
        }

        ReindexTokens();
    }

    private void ReindexTokens()
    {
        for (int i = 0; i < tokenButtons.Count; i++)
        {
            if (tokenButtons[i] != null)
            {
                tokenButtons[i].Setup(i + 1, this);
            }
        }
    }
}