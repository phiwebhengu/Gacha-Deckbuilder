using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

public class MatchPullSetup : NetworkBehaviour
{
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private DrawTimerManager drawTimerManager;

    private IEnumerator Start()
    {
        var gachaManager = FindFirstObjectByType<GachaManager>();
        bool offline = gachaManager != null && gachaManager.isOfflineAIRival;

        if (!IsServer && !offline) yield break; // Online, only the server picks the featured cards

        // 1. Wait for decks to finish loading
        yield return new WaitUntil(() =>
            deckManager != null &&
            deckManager.loadedActionCards != null && deckManager.loadedActionCards.Count > 0 &&
            deckManager.loadedSupportCards != null && deckManager.loadedSupportCards.Count > 0);

        // Give joining clients an extra frame to fully synchronize and spawn UI components
        yield return new WaitForSeconds(0.75f);

        var rng = new System.Random();

        // 2. Find all Legendary cards
        var legendaryActions = deckManager.loadedActionCards.Where(c => c.Tier.ToString().Equals("Legendary", System.StringComparison.OrdinalIgnoreCase)).ToList();
        var legendarySupports = deckManager.loadedSupportCards.Where(c => c.Tier.ToString().Equals("Legendary", System.StringComparison.OrdinalIgnoreCase)).ToList();

        if (legendaryActions.Count == 0 || legendarySupports.Count == 0)
        {
            Debug.LogError("[MatchSetup] No legendary cards found in decks! Check your CSV files.");
            yield break;
        }

        // 3. Pick random featured cards
        int featuredAction = legendaryActions[rng.Next(0, legendaryActions.Count)].Id;
        int featuredSupport = legendarySupports[rng.Next(0, legendarySupports.Count)].Id;

        Debug.Log($"[MatchSetup] Featured Action card this match: ID {featuredAction} | Featured Support card: ID {featuredSupport}");

        // 4. Tell the GachaManager about the featured cards.
        // Offline, the DrawTimerManager starts Round 1 by itself (its AI mode), so only the featured cards are set here.
        if (offline)
        {
            gachaManager.SetFeaturedCards(featuredAction, featuredSupport);
            yield break;
        }

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