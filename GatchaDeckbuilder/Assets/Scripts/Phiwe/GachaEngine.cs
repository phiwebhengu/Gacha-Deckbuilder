using System;
using System.Collections.Generic;
using System.Linq;

public static class GachaEngine
{
    private static (Rarity tier, bool pityTriggered) RollTier(PullConfig config, PityState state, Random rng)
    {
        // Check Pity First
        if (config.pityThreshold > 0 && state.PullsSinceLegendary >= config.pityThreshold)
            return (Rarity.Legendary, true);

        // Normal Roll
        var tier = config.RollRarity(rng);

        // Optional: Soft pity boost could be added here if needed

        return (tier, false);
    }

    private static bool Resolve5050(PityState state, Random rng)
    {
        if (state.GuaranteedFeaturedNext)
        {
            state.GuaranteedFeaturedNext = false;
            return true;
        }

        bool won = rng.Next(0, 2) == 0;
        if (!won)
            state.GuaranteedFeaturedNext = true;

        return won;
    }

    public static PullResult PullAction(
        PullConfig config, PityState state, Random rng,
        List<ActionCardData> pool, int featuredCardId)
    {
        if (pool == null || pool.Count == 0)
        {
            // Return dummy result or throw error if pool is empty
            return new PullResult { Deck = DeckType.Action, CardName = "Error", Tier = Rarity.Common };
        }

        var (tier, pityTriggered) = RollTier(config, state, rng);
        string tierStr = tier.ToString();

        ActionCardData chosen;
        bool was5050 = tier == Rarity.Legendary;
        bool won5050 = false;

        if (was5050)
        {
            won5050 = Resolve5050(state, rng);

            var featuredCard = pool.FirstOrDefault(c => c.Id == featuredCardId);
            var otherLegendaries = pool.Where(c => c.Tier == tierStr && c.Id != featuredCardId).ToList();

            if (featuredCard != null && otherLegendaries.Count > 0)
            {
                chosen = won5050 ? featuredCard : PickRandom(otherLegendaries, rng);
            }
            else if (featuredCard != null)
            {
                // Only featured legendary exists
                chosen = featuredCard;
            }
            else
            {
                // Fallback: pick any legendary
                var legendaries = pool.Where(c => c.Tier == tierStr).ToList();
                if (legendaries.Count > 0)
                    chosen = PickRandom(legendaries, rng);
                else
                    chosen = PickRandom(pool, rng); // Ultimate fallback
            }

            state.RegisterLegendaryPull();
        }
        else
        {
            var tierPool = pool.Where(c => c.Tier == tierStr).ToList();
            if (tierPool.Count > 0)
                chosen = PickRandom(tierPool, rng);
            else
                chosen = PickRandom(pool, rng); // Fallback if specific tier missing

            state.RegisterNonLegendaryPull();
        }

        LogPull(DeckType.Action, tier, pityTriggered, was5050, won5050, chosen.Name);

        return new PullResult
        {
            Deck = DeckType.Action,
            Tier = tier,
            CardId = chosen.Id,
            CardName = chosen.Name,
            ActionData = chosen,
            PityTriggered = pityTriggered,
            Was5050Roll = was5050,
            Won5050 = won5050
        };
    }

    // Repeat similar safety checks for PullSupport...
    public static PullResult PullSupport(
        PullConfig config, PityState state, Random rng,
        List<SupportCardData> pool, int featuredCardId)
    {
        if (pool == null || pool.Count == 0)
        {
            return new PullResult { Deck = DeckType.Support, CardName = "Error", Tier = Rarity.Common };
        }

        var (tier, pityTriggered) = RollTier(config, state, rng);
        string tierStr = tier.ToString();

        SupportCardData chosen;
        bool was5050 = tier == Rarity.Legendary;
        bool won5050 = false;

        if (was5050)
        {
            won5050 = Resolve5050(state, rng);

            var featuredCard = pool.FirstOrDefault(c => c.Id == featuredCardId);
            var otherLegendaries = pool.Where(c => c.Tier == tierStr && c.Id != featuredCardId).ToList();

            if (featuredCard != null && otherLegendaries.Count > 0)
            {
                chosen = won5050 ? featuredCard : PickRandom(otherLegendaries, rng);
            }
            else if (featuredCard != null)
            {
                chosen = featuredCard;
            }
            else
            {
                var legendaries = pool.Where(c => c.Tier == tierStr).ToList();
                if (legendaries.Count > 0)
                    chosen = PickRandom(legendaries, rng);
                else
                    chosen = PickRandom(pool, rng);
            }

            state.RegisterLegendaryPull();
        }
        else
        {
            var tierPool = pool.Where(c => c.Tier == tierStr).ToList();
            if (tierPool.Count > 0)
                chosen = PickRandom(tierPool, rng);
            else
                chosen = PickRandom(pool, rng);

            state.RegisterNonLegendaryPull();
        }

        LogPull(DeckType.Support, tier, pityTriggered, was5050, won5050, chosen.Name);

        return new PullResult
        {
            Deck = DeckType.Support,
            Tier = tier,
            CardId = chosen.Id,
            CardName = chosen.Name,
            SupportData = chosen,
            PityTriggered = pityTriggered,
            Was5050Roll = was5050,
            Won5050 = won5050
        };
    }

    private static T PickRandom<T>(List<T> list, Random rng) => list[rng.Next(0, list.Count)];

    private static void LogPull(DeckType deck, Rarity tier, bool pity, bool was5050, bool won5050, string name)
    {
        string tag = pity ? " [PITY]" : "";
        string coin = was5050 ? (won5050 ? " [50/50 WON]" : " [50/50 LOST — next Legendary guaranteed]") : "";
        UnityEngine.Debug.Log($"[Pull] {deck} → {tier} → {name}{tag}{coin}");
    }
}