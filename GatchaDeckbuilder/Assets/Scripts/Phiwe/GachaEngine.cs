using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;
public static class GachaEngine
{
    private static (Rarity tier, bool pityTriggered) RollTier(PullConfig config, PityState state, Random rng)
    {
        if (state.PullsSinceLegendary >= config.pityThreshold)
            return (Rarity.Legendary, true);

        return (config.RollRarity(rng), false);
    }

    private static bool Resolve5050(PityState state, Random rng)
    {
        if (state.GuaranteedFeaturedNext)
        {
            state.GuaranteedFeaturedNext = false;
            return true;
        }

        bool won = rng.Next(0, 2) == 0;
        if (!won) state.GuaranteedFeaturedNext = true;
        return won;
    }

    public static PullResult PullAction(
        PullConfig config, PityState state, Random rng,
        List<ActionCardData> pool, int featuredCardId)
    {
        var (tier, pityTriggered) = RollTier(config, state, rng);
        string tierStr = tier.ToString();

        ActionCardData chosen = null;
        bool was5050 = tier == Rarity.Legendary;
        bool won5050 = false;

        if (was5050)
        {
            won5050 = Resolve5050(state, rng);
            if (won5050)
            {
                // Safe search with FirstOrDefault
                chosen = pool.FirstOrDefault(c => c.Id == featuredCardId);

                // Fallback if ID was not found in pool
                if (chosen == null)
                {
                    Debug.LogError($"[GachaEngine] Featured Action Card ID {featuredCardId} not found in pool! Picked fallback.");
                    chosen = PickRandom(pool.Where(c => c.Tier == tierStr).ToList(), rng, pool);
                }
            }
            else
            {
                var nonFeaturedPool = pool.Where(c => c.Tier == tierStr && c.Id != featuredCardId).ToList();
                chosen = PickRandom(nonFeaturedPool, rng, pool);
            }
            state.RegisterLegendaryPull();
        }
        else
        {
            var tierPool = pool.Where(c => c.Tier == tierStr).ToList();
            chosen = PickRandom(tierPool, rng, pool);
            state.RegisterNonLegendaryPull();
        }

        LogPull(DeckType.Action, tier, pityTriggered, was5050, won5050, chosen?.Name ?? "UNKNOWN");

        return new PullResult
        {
            Deck = DeckType.Action,
            Tier = tier,
            CardId = chosen?.Id ?? -1,
            CardName = chosen?.Name ?? "Blank Card",
            ActionData = chosen,
            PityTriggered = pityTriggered,
            Was5050Roll = was5050,
            Won5050 = won5050
        };
    }

    public static PullResult PullSupport(
        PullConfig config, PityState state, Random rng,
        List<SupportCardData> pool, int featuredCardId)
    {
        var (tier, pityTriggered) = RollTier(config, state, rng);
        string tierStr = tier.ToString();

        SupportCardData chosen = null;
        bool was5050 = tier == Rarity.Legendary;
        bool won5050 = false;

        if (was5050)
        {
            won5050 = Resolve5050(state, rng);
            if (won5050)
            {
                // Safe search with FirstOrDefault
                chosen = pool.FirstOrDefault(c => c.Id == featuredCardId);

                // Fallback if ID was not found in pool
                if (chosen == null)
                {
                    Debug.LogError($"[GachaEngine] Featured Support Card ID {featuredCardId} not found in pool! Picked fallback.");
                    chosen = PickRandom(pool.Where(c => c.Tier == tierStr).ToList(), rng, pool);
                }
            }
            else
            {
                var nonFeaturedPool = pool.Where(c => c.Tier == tierStr && c.Id != featuredCardId).ToList();
                chosen = PickRandom(nonFeaturedPool, rng, pool);
            }
            state.RegisterLegendaryPull();
        }
        else
        {
            var tierPool = pool.Where(c => c.Tier == tierStr).ToList();
            chosen = PickRandom(tierPool, rng, pool);
            state.RegisterNonLegendaryPull();
        }

        LogPull(DeckType.Support, tier, pityTriggered, was5050, won5050, chosen?.Name ?? "UNKNOWN");

        return new PullResult
        {
            Deck = DeckType.Support,
            Tier = tier,
            CardId = chosen?.Id ?? -1,
            CardName = chosen?.Name ?? "Blank Card",
            SupportData = chosen,
            PityTriggered = pityTriggered,
            Was5050Roll = was5050,
            Won5050 = won5050
        };
    }

    private static T PickRandom<T>(List<T> filteredList, Random rng, List<T> fallbackList)
    {
        // If filtered pool is valid, pick from it
        if (filteredList != null && filteredList.Count > 0)
        {
            return filteredList[rng.Next(0, filteredList.Count)];
        }

        // Fallback to absolute full pool if filtered pool is empty
        if (fallbackList != null && fallbackList.Count > 0)
        {
            Debug.LogWarning("[GachaEngine] Filtered pool was empty! Falling back to full pool.");
            return fallbackList[rng.Next(0, fallbackList.Count)];
        }

        return default;
    }

    private static void LogPull(DeckType deck, Rarity tier, bool pity, bool was5050, bool won5050, string name)
    {
        string tag = pity ? " [PITY]" : "";
        string coin = was5050 ? (won5050 ? " [50/50 WON]" : " [50/50 LOST — next Legendary guaranteed]") : "";
        Debug.Log($"[Pull] {deck} → {tier} → {name}{tag}{coin}");
    }
}