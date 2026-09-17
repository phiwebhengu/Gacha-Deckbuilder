using System.Collections;
using System.Linq;
using UnityEngine;

public class MatchPullSetup : MonoBehaviour
{
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private PlayerPullController playerAPull;
    [SerializeField] private PlayerPullController playerBPull;

    private IEnumerator Start()
    {
        yield return new WaitUntil(() =>
            deckManager.loadedActionCards != null && deckManager.loadedActionCards.Count > 0 &&
            deckManager.loadedSupportCards != null && deckManager.loadedSupportCards.Count > 0);

        var rng = new System.Random();
        var legendaryActions = deckManager.loadedActionCards.Where(c => c.Tier == "Legendary").ToList();
        var legendarySupports = deckManager.loadedSupportCards.Where(c => c.Tier == "Legendary").ToList();

        int featuredAction = legendaryActions[rng.Next(0, legendaryActions.Count)].Id;
        int featuredSupport = legendarySupports[rng.Next(0, legendarySupports.Count)].Id;

        Debug.Log($"[MatchSetup] Featured Action card this match: ID {featuredAction} | Featured Support card: ID {featuredSupport}");

        playerAPull.SetFeaturedCards(featuredAction, featuredSupport);
        playerBPull.SetFeaturedCards(featuredAction, featuredSupport);
    }
}