using System;
using System.Collections.Generic;
using System.Linq;

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

        ActionCardData chosen;
        bool was5050 = tier == Rarity.Legendary;
        bool won5050 = false;

        if (was5050)
        {
            won5050 = Resolve5050(state, rng);
            chosen = won5050
                ? pool.First(c => c.Id == featuredCardId)
                : PickRandom(pool.Where(c => c.Tier == tierStr && c.Id != featuredCardId).ToList(), rng);
            state.RegisterLegendaryPull();
        }
        else
        {
            chosen = PickRandom(pool.Where(c => c.Tier == tierStr).ToList(), rng);
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

    public static PullResult PullSupport(
        PullConfig config, PityState state, Random rng,
        List<SupportCardData> pool, int featuredCardId)
    {
        var (tier, pityTriggered) = RollTier(config, state, rng);
        string tierStr = tier.ToString();

        SupportCardData chosen;
        bool was5050 = tier == Rarity.Legendary;
        bool won5050 = false;

        if (was5050)
        {
            won5050 = Resolve5050(state, rng);
            chosen = won5050
                ? pool.First(c => c.Id == featuredCardId)
                : PickRandom(pool.Where(c => c.Tier == tierStr && c.Id != featuredCardId).ToList(), rng);
            state.RegisterLegendaryPull();
        }
        else
        {
            chosen = PickRandom(pool.Where(c => c.Tier == tierStr).ToList(), rng);
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