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

    // Set by the server when a Support effect throws this card away the moment it is pulled
    // (Knights of Favonius Library: "discard this pull and pull again"). The card is shown, then removed.
    public bool AutoDiscard;
}
public enum DeckType
{
    Action,
    Support
}