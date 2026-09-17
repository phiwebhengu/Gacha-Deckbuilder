public class PullResult
{
    public DeckType Deck;
    public Rarity Tier;
    public int CardId;
    public string CardName;

    public ActionCardData ActionData;
    public SupportCardData SupportData;

    public bool PityTriggered;
    public bool Was5050Roll;
    public bool Won5050;
}