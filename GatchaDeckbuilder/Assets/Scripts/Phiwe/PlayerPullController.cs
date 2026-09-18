using UnityEngine;

public class PlayerPullController : MonoBehaviour
{
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private PullConfig actionConfig;
    [SerializeField] private PullConfig supportConfig;

    private readonly PityState actionPity = new PityState();
    private readonly PityState supportPity = new PityState();
    private System.Random rng;

    private int featuredActionCardId;
    private int featuredSupportCardId;

    private void Awake()
    {
        rng = new System.Random();
        if (deckManager == null) deckManager = FindFirstObjectByType<DeckManager>();
    }

    public void SetSeed(int seed) => rng = new System.Random(seed);

    public void SetFeaturedCards(int actionCardId, int supportCardId)
    {
        featuredActionCardId = actionCardId;
        featuredSupportCardId = supportCardId;
    }

    public PullResult PullAction() =>
        GachaEngine.PullAction(actionConfig, actionPity, rng, deckManager.loadedActionCards, featuredActionCardId);

    public PullResult PullSupport() =>
        GachaEngine.PullSupport(supportConfig, supportPity, rng, deckManager.loadedSupportCards, featuredSupportCardId);
}