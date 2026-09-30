using UnityEngine;
using System.Collections.Generic;

public class DeckManager : MonoBehaviour
{
    [Header("CSV File References")]
    [Tooltip("Drag and drop ActionDeck.csv here")]
    public TextAsset actionDeckCsv;

    [Tooltip("Drag and drop SupportDeck.csv here")]
    public TextAsset supportDeckCsv;

    [Header("Loaded Data (Visible in Inspector for debugging)")]
    public List<ActionCardData> loadedActionCards;
    public List<SupportCardData> loadedSupportCards;

    void Awake()
    {
        LoadDecks();
    }

    void LoadDecks()
    {
        // Load Action Deck
        if (actionDeckCsv != null)
        {
            loadedActionCards = CardLoader.LoadActionDeck(actionDeckCsv);
            Debug.Log($"✅ Successfully loaded {loadedActionCards.Count} Action Cards.");
        }
        else
        {
            Debug.LogWarning("⚠️ Action Deck CSV is not assigned in the Inspector!");
        }

        // Load Support Deck
        if (supportDeckCsv != null)
        {
            loadedSupportCards = CardLoader.LoadSupportDeck(supportDeckCsv);
            Debug.Log($"✅ Successfully loaded {loadedSupportCards.Count} Support Cards.");
        }
        else
        {
            Debug.LogWarning("⚠️ Support Deck CSV is not assigned in the Inspector!");
        }
    }

    public Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        // Assumes sprites are named "1", "2", "18", etc. inside Assets/Resources/CardSprites/
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        return Resources.Load<Sprite>($"{folder}{cardId}");
    }
}
