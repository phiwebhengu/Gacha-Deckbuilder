using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

public class MatchPullSetup : NetworkBehaviour
{
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private DrawTimerManager drawTimerManager; // ADD THIS

    private IEnumerator Start()
    {
        if (!IsServer) yield break; // Only run on server

        // 1. Wait for decks to finish loading
        yield return new WaitUntil(() =>
            deckManager != null &&
            deckManager.loadedActionCards != null && deckManager.loadedActionCards.Count > 0 &&
            deckManager.loadedSupportCards != null && deckManager.loadedSupportCards.Count > 0);

        var rng = new System.Random();

        // 2. Find all Legendary cards
        var legendaryActions = deckManager.loadedActionCards.Where(c => c.Tier == "Legendary").ToList();
        var legendarySupports = deckManager.loadedSupportCards.Where(c => c.Tier == "Legendary").ToList();

        if (legendaryActions.Count == 0 || legendarySupports.Count == 0)
        {
            Debug.LogError("[MatchSetup] No legendary cards found in decks! Check your CSV files.");
            yield break;
        }

        // 3. Pick random featured cards
        int featuredAction = legendaryActions[rng.Next(0, legendaryActions.Count)].Id;
        int featuredSupport = legendarySupports[rng.Next(0, legendarySupports.Count)].Id;

        Debug.Log($"[MatchSetup] Featured Action card this match: ID {featuredAction} | Featured Support card: ID {featuredSupport}");

        // 4. Tell the GachaManager about the featured cards
        var gachaManager = FindFirstObjectByType<GachaManager>();
        if (gachaManager != null && gachaManager.IsServer)
        {
            gachaManager.SetFeaturedCards(featuredAction, featuredSupport);

            // 5. START THE ROUND TIMER
            if (drawTimerManager != null)
            {
                Debug.Log("[MatchSetup] Starting Round 1 timer...");
                drawTimerManager.RequestStartRound(1);
            }
            else
            {
                Debug.LogError("[MatchSetup] DrawTimerManager not assigned!");
            }
        }
        else
        {
            Debug.LogWarning("[MatchSetup] GachaManager not found or this client is not the server.");
        }
    }
}