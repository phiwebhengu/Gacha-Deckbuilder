using UnityEngine;

public enum Rarity { Common, Rare, Legendary }

[CreateAssetMenu(fileName = "PullConfig", menuName = "Gacha/Pull Config")]
public class PullConfig : ScriptableObject
{
    [Header("Odds — must sum to 100")]
    [Range(0, 100)] public int commonWeight = 60;
    [Range(0, 100)] public int rareWeight = 30;
    [Range(0, 100)] public int legendaryWeight = 10;

    [Header("Pity")]
    [Tooltip("Non-Legendary pulls in a row before the next pull is a guaranteed Legendary")]
    public int pityThreshold = 4;

    private void OnValidate()
    {
        int total = commonWeight + rareWeight + legendaryWeight;
        if (total != 100)
            Debug.LogWarning($"[PullConfig] Weights sum to {total}, not 100 — odds will be normalized at runtime but this should be fixed.");
    }

    public Rarity RollRarity(System.Random rng)
    {
        int total = Mathf.Max(1, commonWeight + rareWeight + legendaryWeight);
        int roll = rng.Next(0, total);

        if (roll < commonWeight) return Rarity.Common;
        if (roll < commonWeight + rareWeight) return Rarity.Rare;
        return Rarity.Legendary;
    }
}