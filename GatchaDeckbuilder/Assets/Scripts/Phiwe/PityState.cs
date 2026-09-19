[System.Serializable]
public class PityState
{
    public int PullsSinceLegendary;
    public bool GuaranteedFeaturedNext;

    public void RegisterNonLegendaryPull() => PullsSinceLegendary++;
    public void RegisterLegendaryPull() => PullsSinceLegendary = 0;
}