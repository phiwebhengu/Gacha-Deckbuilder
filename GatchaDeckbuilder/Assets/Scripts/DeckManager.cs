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

    void Start()
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

            if (loadedActionCards.Count > 0)
            {
                Debug.Log($"Example: First card is '{loadedActionCards[0].Name}' ({loadedActionCards[0].Category})");
            }
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

            if (loadedSupportCards.Count > 0)
            {
                Debug.Log($"Example: First card is '{loadedSupportCards[0].Name}' ({loadedSupportCards[0].Tier})");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ Support Deck CSV is not assigned in the Inspector!");
        }
    }
}
