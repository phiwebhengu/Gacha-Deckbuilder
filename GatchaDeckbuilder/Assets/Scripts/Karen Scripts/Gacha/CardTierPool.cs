using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CardTierPool<T>
{
    public const float LEGENDARY_CHANCE = 0.10f; // 10%
    public const float RARE_CHANCE = 0.30f;      // 30%
                                                 // Common makes up the remaining 60%.

    private readonly List<T> legendaryCards = new List<T>();
    private readonly List<T> rareCards = new List<T>();
    private readonly List<T> commonCards = new List<T>();

    private readonly int pityThreshold;
    private int pullsSinceLastLegendary = 0;

    public int PityThreshold => pityThreshold;
    public int PullsSinceLastLegendary => pullsSinceLastLegendary;
    public int PullsUntilGuaranteedLegendary => Mathf.Max(0, pityThreshold - pullsSinceLastLegendary);

    public int LegendaryCount => legendaryCards.Count;
    public int RareCount => rareCards.Count;
    public int CommonCount => commonCards.Count;

    public CardTierPool(List<T> allCards, Func<T, string> tierSelector, int pityThreshold = 5)
    {
        this.pityThreshold = pityThreshold;

        foreach (var card in allCards)
        {
            switch (tierSelector(card))
            {
                case "Legendary":
                    legendaryCards.Add(card);
                    break;
                case "Rare":
                    rareCards.Add(card);
                    break;
                case "Common":
                    commonCards.Add(card);
                    break;
                default:
                    Debug.LogWarning($"CardTierPool: unknown tier '{tierSelector(card)}' — card skipped.");
                    break;
            }
        }
    }

    /// <summary>
    /// Rolls a single card from the pool, applying tier odds and pity.
    /// Call this once per token spent.
    /// </summary>
    public T Pull()
    {
        pullsSinceLastLegendary++;

        bool pityTriggered = pullsSinceLastLegendary >= pityThreshold;
        float roll = UnityEngine.Random.value; // [0, 1)

        string tier;
        if (pityTriggered || roll < LEGENDARY_CHANCE)
        {
            tier = "Legendary";
        }
        else if (roll < LEGENDARY_CHANCE + RARE_CHANCE)
        {
            tier = "Rare";
        }
        else
        {
            tier = "Common";
        }

        T result = PickFromTier(tier);

        if (tier == "Legendary")
            pullsSinceLastLegendary = 0;

        return result;
    }

    private T PickFromTier(string tier)
    {
        List<T> bucket = tier switch
        {
            "Legendary" => legendaryCards,
            "Rare" => rareCards,
            _ => commonCards
        };

        if (bucket.Count == 0)
        {
            // Safety net so a pull never silently fails if a tier is empty
            // in the CSV (e.g. no Legendary rows defined yet).
            bucket = commonCards.Count > 0 ? commonCards
                   : rareCards.Count > 0 ? rareCards
                   : legendaryCards;

            if (bucket == null || bucket.Count == 0)
            {
                Debug.LogError("CardTierPool: no cards loaded in any tier — check the CSV.");
                return default;
            }
        }

        return bucket[UnityEngine.Random.Range(0, bucket.Count)];
    }
}
