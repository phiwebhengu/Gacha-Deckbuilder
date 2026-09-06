using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TokenSelectionManager : MonoBehaviour
{
    [Header("Token Pool")]
    public List<TokenButton> tokenButtons = new List<TokenButton>();
    public Transform tokenContainer;

    [Header("Selection & Staging State")]
    private int hoveredTokenCount = 0;
    private int stagedTokenCount = 0;

    [Header("Hand Reference")]
    [SerializeField] private PlayerHandManager handManager;

    [SerializeField] private DrawTimerManager timerManager;
    public int StagedTokenCount => stagedTokenCount;

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

    public void HoverTokensUpTo(int count)
    {
        hoveredTokenCount = Mathf.Clamp(count, 0, tokenButtons.Count);
        UpdateTokenVisuals();
    }

    public void ClearHover()
    {
        hoveredTokenCount = 0;
        UpdateTokenVisuals();
    }

    // Step 1: Stage tokens (keep them highlighted until a deck is clicked)
    public void SelectTokens(int count)
    {
        stagedTokenCount = Mathf.Clamp(count, 0, tokenButtons.Count);
        Debug.Log($"[Token System] Staged {stagedTokenCount} tokens. Select a deck to draw cards!");
        UpdateTokenVisuals();
    }

    // Step 2: Deck clicked -> Deal cards & deplete staged tokens
    public void OnDeckSelected(DeckButton deck)
    {
        if (stagedTokenCount <= 0)
        {
            Debug.LogWarning("[Token System] Select a token count before picking a deck!");
            return;
        }

        int countToSpend = stagedTokenCount;
        stagedTokenCount = 0;

        if (handManager != null)
        {
            handManager.DealCardsFromTokens(countToSpend, deck.DeckTransform, deck.Type);
        }

        DepleteTokens(countToSpend);

        // Notify timer that player successfully drew cards
        if (timerManager != null)
        {
            timerManager.NotifyCardsDrawn();
        }
    }

    public void ForceAutoDrawSingleToken(DeckButton targetDeck)
    {
        if (tokenButtons.Count == 0 || targetDeck == null) return;

        Debug.Log($"[Token System] Executing Penalty: Drawing 1 card from {targetDeck.Type} Deck.");

        if (handManager != null)
        {
            handManager.DealCardsFromTokens(1, targetDeck.DeckTransform, targetDeck.Type);
        }

        DepleteTokens(1);
    }

    private void DepleteTokens(int countToDeplete)
    {
        ClearHover();

        for (int i = countToDeplete - 1; i >= 0; i--)
        {
            TokenButton btnToDestroy = tokenButtons[i];
            tokenButtons.RemoveAt(i);
            Destroy(btnToDestroy.gameObject);
        }

        ReindexTokens();
    }

    private void ReindexTokens()
    {
        for (int i = 0; i < tokenButtons.Count; i++)
        {
            tokenButtons[i].Setup(i + 1, this);
        }
    }

    private void UpdateTokenVisuals()
    {
        for (int i = 0; i < tokenButtons.Count; i++)
        {
            // Highlight if hovered OR if currently staged for purchase
            bool shouldHighlight = (i + 1) <= hoveredTokenCount || (i + 1) <= stagedTokenCount;
            tokenButtons[i].Highlight(shouldHighlight);
        }
    }
}