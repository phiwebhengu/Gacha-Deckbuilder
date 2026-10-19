using UnityEngine;

/// <summary>
/// Running totals for the LOCAL player's match, shown on the Outcome page.
/// Plain static data: it survives scene changes and is cleared when a new match starts (GachaManager spawns).
/// Everything is recorded on the client that owns the stats, so each player sees their own numbers.
/// </summary>
public static class MatchStats
{
    // Match summary
    public static int Rounds;
    public static int DamageDealt;
    public static int DamageTaken;

    // Pull summary
    public static int TokensSpent;
    public static int ActionPulls;
    public static int SupportPulls;
    public static int ActionPity;      // times the pity guarantee kicked in on an Action pull
    public static int SupportPity;     // times the pity guarantee kicked in on a Support pull
    public static int Won5050;
    public static int Lost5050;

    private static int lastTokens = -1;

    public static void Reset(int startingTokens)
    {
        Rounds = DamageDealt = DamageTaken = 0;
        TokensSpent = ActionPulls = SupportPulls = 0;
        ActionPity = SupportPity = Won5050 = Lost5050 = 0;
        lastTokens = startingTokens;
    }

    /// <summary>One resolved round. Damage is from this player's point of view.</summary>
    public static void RecordRound(int damageDealtToOpponent, int damageTakenByPlayer)
    {
        Rounds++;
        DamageDealt += Mathf.Max(0, damageDealtToOpponent);
        DamageTaken += Mathf.Max(0, damageTakenByPlayer);
    }

    /// <summary>Called every time the token balance changes. Only drops count as spending.</summary>
    public static void RecordTokens(int newTokens)
    {
        if (lastTokens >= 0 && newTokens < lastTokens) TokensSpent += lastTokens - newTokens;
        lastTokens = newTokens;
    }

    /// <param name="playerRequested">true for a pull the player asked for, false for a bonus card from a Support effect.
    /// Bonus cards still count towards pity and 50/50, but not towards the pull count.</param>
    public static void RecordPull(PullResult r, bool playerRequested)
    {
        if (r == null) return;
        bool isAction = r.Deck == DeckType.Action;

        if (playerRequested)
        {
            if (isAction) ActionPulls++; else SupportPulls++;
        }

        if (r.PityTriggered)
        {
            if (isAction) ActionPity++; else SupportPity++;
        }

        if (r.Was5050Roll)
        {
            if (r.Won5050) Won5050++; else Lost5050++;
        }
    }
}