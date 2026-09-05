using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TokenSelectionManager : MonoBehaviour
{
    [Header("Token Pool")]
    public List<TokenButton> tokenButtons = new List<TokenButton>();
    public Transform tokenContainer; // Horizontal Layout Group container holding the buttons

    [Header("Selection State")]
    private int hoveredTokenCount = 0;
    private int selectedTokenCount = 0;

    [Header("Hand Reference")]
    [SerializeField] private PlayerHandManager handManager;
    private void Start()
    {
        InitializeTokenButtons();
    }

    private void InitializeTokenButtons()
    {
        // If buttons are already placed under a container, register them automatically
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

    // Highlights token 1 up to 'count'
    public void HoverTokensUpTo(int count)
    {
        hoveredTokenCount = count;
        UpdateTokenVisuals();
    }

    // Resets highlight when mouse leaves the UI buttons
    public void ClearHover()
    {
        hoveredTokenCount = 0;
        UpdateTokenVisuals();
    }

    // Called when a button is clicked
    public void SelectTokens(int count)
    {
        selectedTokenCount = count;
        Debug.Log($"[Token System] Tokens Selected: {selectedTokenCount}. Dealing {selectedTokenCount} cards!");

        if (handManager != null)
        {
            handManager.DealCardsFromTokens(selectedTokenCount);
        }
    }

    private void UpdateTokenVisuals()
    {
        for (int i = 0; i < tokenButtons.Count; i++)
        {
            // Highlight button if its index is <= the currently hovered token
            bool shouldHighlight = (i + 1) <= hoveredTokenCount;
            tokenButtons[i].Highlight(shouldHighlight);
        }
    }
}