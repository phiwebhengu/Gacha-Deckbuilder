using System;
using UnityEngine;

public class PityManager : MonoBehaviour
{
    [SerializeField] private PullConfig pullConfig;
    [SerializeField] private int currentPityCount = 0;

    // Event fired whenever pity count or threshold state changes
    public event Action<int, int> OnPityUpdated; // Parameters: (currentPity, maxPityThreshold)

    public int CurrentPityCount => currentPityCount;

    private void Start()
    {
        // Broadcast initial values to UI on load
        NotifyPityChanged();
    }

    public bool ShouldForceLegendary()
    {
        if (pullConfig == null) return false;
        return currentPityCount >= pullConfig.pityThreshold;
    }

    public void RegisterPull(Rarity pulledRarity)
    {
        if (pulledRarity == Rarity.Legendary)
        {
            currentPityCount = 0;
        }
        else
        {
            currentPityCount++;
        }

        NotifyPityChanged();
    }

    public void ResetPity()
    {
        currentPityCount = 0;
        NotifyPityChanged();
    }

    private void NotifyPityChanged()
    {
        int maxPity = (pullConfig != null) ? pullConfig.pityThreshold : 0;
        OnPityUpdated?.Invoke(currentPityCount, maxPity);
    }
}