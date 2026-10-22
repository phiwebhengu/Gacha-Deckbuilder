using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies Support card effects when a round is resolved. Plain C# (no networking), so it is easy to read and test.
/// The server calls <see cref="SupportEffectResolver.Resolve"/> once per round with both players' played cards.
///
/// ======================= HOW A ROUND IS CALCULATED =======================
/// 1. Totals come from the played ACTION cards: Attack cards add to attack, Defense cards add to defense.
/// 2. "Doubling" supports are worked out (Dawn Winery, Tubby, Stormterror's Lair). Each played support gets a
///    multiplier (x1 normally, x2 when doubled). Every number below is scaled by that multiplier.
///    The multiplier is also handed to the gacha side (see SideOutcome.PlayedSupports), so doubled effects such as
///    Liyue Harbor Wharf give double the cards.
/// 3. Each player's own modifiers, in this order:
///      a) flat bonuses/penalties  (Favonius Cathedral, Central Laboratory Ruins, Grand Narukami Shrine, Mamere)
///      b) conversion               (Parametric Transformer: defense -> attack)
///      c) multipliers on attack    (Treasure-Seeking Seelie)
///      d) multipliers on defense   (Liben)
/// 4. Opponent-targeted attack reduction (Wangshu Inn) is applied to the other player's finished attack total.
/// 5. Damage dealt = attack - opponent's defense (never below 0), then:
///      Ellin adds flat damage, Seirai Island doubles big hits, and Memento Lens makes the holder immune.
/// 6. After the round: Paimon / Vanarana / Katheryne give tokens, Kid Kujirai may be discarded.
///
/// ======================= INTERPRETATIONS (change here if you meant otherwise) =======================
/// - A support's effect happens every round it is PLAYED (a "Forever" card is simply one that comes back to your
///   hand afterwards; "Immediate" cards are used up).
/// - "Total cards used that round" (Grand Narukami Shrine) = all cards YOU played that round, actions + supports.
/// - "Own N cards" / "have defense cards" use the cards you PLAYED this round (the server doesn't track hands).
/// - "Win the round" (Katheryne) = you dealt more damage than you took.
/// - A doubled effect: flat numbers are multiplied by 2; "double X" effects become x4.
/// - Companion / Location / Item comes from the "Category" column of SupportDeck.csv (SupportCardData.Category).
///   If the CSV has no such column, a best-guess table by card ID is used instead (KindOfById).
/// - Dawn Winery doubles ALL Companion supports played alongside it; Tubby doubles ALL Location supports.
/// - Stormterror's Lair doubles the NEXT support played with it, THE SAME ROUND ONLY (nothing carries over to later
///   rounds). "Next" = the first other support in the played order; two Lairs double two different supports.
/// - Ellin's +damage applies even if the attack was fully blocked; Memento Lens still cancels it.
///
/// Supports that are NOT match-result effects (pulls, draws, pity, luck, discarding from the collection) are not
/// handled here: see <see cref="AffectsMatchResult"/>. They are applied by GachaManager.ApplyRoundEnd, which gets the
/// played supports (and their multipliers) from <see cref="SideOutcome.PlayedSupports"/>.
/// </summary>

/// <summary>
/// Per-player support state that has to survive from one round to the next.
/// Nothing needs to survive any more (Stormterror's Lair now only works in the round it is played), the class is
/// kept so the Resolve() signature and EndTurnManager don't have to change.
/// </summary>
public class SupportMatchState
{
}

/// <summary>One support card a player played this round, as the gacha side needs to see it.</summary>
public class PlayedSupportInfo
{
    public int SupportId;
    public int NetId;
    /// <summary>1 normally, 2 when the effect was doubled (Dawn Winery / Tubby / Stormterror's Lair), 4 when doubled twice.</summary>
    public int Mult = 1;
    /// <summary>True for "Immediate" cards: they are used up and leave the hand after the round.</summary>
    public bool Spent;
}

/// <summary>Everything the match needs to know about one player after support effects are applied.</summary>
public class SideOutcome
{
    /// <summary>Final attack total (after support effects). This is the number the attack count-up shows.</summary>
    public int Attack;

    /// <summary>Final defense total (after support effects).</summary>
    public int Defense;

    /// <summary>Damage THIS player deals to the other player this round.</summary>
    public int DamageDealt;

    /// <summary>Tokens this player earns from support effects this round.</summary>
    public int TokenReward;

    /// <summary>Played cards (network ids, see <see cref="CardNetId"/>) that are removed from the hand after the round.</summary>
    public readonly List<int> DiscardedNetIds = new List<int>();

    /// <summary>Human-readable log of the effects that fired (for Debug.Log / future on-screen popups).</summary>
    public readonly List<string> Notes = new List<string>();

    /// <summary>Every support this player played, in play order, with its multiplier. The gacha side reads this.</summary>
    public readonly List<PlayedSupportInfo> PlayedSupports = new List<PlayedSupportInfo>();

    /// <summary>How many cards (actions + supports) this player played this round.</summary>
    public int PlayedCardCount;

    /// <summary>How many of the played cards leave the hand for good (spent Immediate supports + discarded cards).</summary>
    public int RemovedFromHandCount;
}

public static class SupportEffectResolver
{
    // ===================== Support card Ids (SupportDeck.csv) =====================
    public const int JadeChamber = 1;
    public const int SeiraiIsland = 2;
    public const int Liben = 3;
    public const int LiyueHarborWharf = 4;
    public const int WangshuInn = 5;
    public const int GoldenHouse = 6;
    public const int GrandNarukamiShrine = 7;
    public const int Paimon = 8;
    public const int Ellin = 9;
    public const int ParametricTransformer = 10;
    public const int Hanachirusato = 11;
    public const int StadiumOfTheSacredFlame = 12;
    public const int FavoniusCathedral = 13;
    public const int KnightsOfFavoniusLibrary = 14;
    public const int DawnWinery = 15;
    public const int StormterrorsLair = 16;
    public const int Vanarana = 17;
    public const int WeepingWillowOfTheLake = 18;
    public const int FrostmoonEnclave = 19;
    public const int Katheryne = 20;
    public const int Tubby = 21;
    public const int Xudong = 22;
    public const int KidKujirai = 23;
    public const int Setaria = 24;
    public const int Seymour = 25;
    public const int TreasureSeekingSeelie = 26;
    public const int MementoLens = 27;
    public const int Atea = 28;
    public const int CentralLaboratoryRuins = 29;
    public const int Mamere = 30;

    // Dawn Winery doubles Companions, Tubby doubles Locations. The kind comes from the CSV "Category" column.
    private enum SupportKind { Location, Companion, Item, Other }

    private static bool warnedMissingCategory;

    private static SupportKind KindOf(SupportCardData data)
    {
        string category = data != null && data.Category != null ? data.Category.Trim() : "";

        if (category.Length > 0)
        {
            if (category.IndexOf("compan", System.StringComparison.OrdinalIgnoreCase) >= 0) return SupportKind.Companion;
            if (category.IndexOf("locat", System.StringComparison.OrdinalIgnoreCase) >= 0) return SupportKind.Location;
            if (category.IndexOf("item", System.StringComparison.OrdinalIgnoreCase) >= 0) return SupportKind.Item;
            return SupportKind.Other; // some other category (e.g. Event): neither Dawn Winery nor Tubby doubles it
        }

        if (!warnedMissingCategory)
        {
            warnedMissingCategory = true;
            Debug.LogWarning("[SupportEffectResolver] Support cards have no Category (add a 'Category' column to SupportDeck.csv). Guessing Companion/Location/Item by card ID.");
        }
        return data != null ? KindOfById(data.Id) : SupportKind.Other;
    }

    // Fallback used only when the CSV has no Category column (best guess from the Genshin TCG card types).
    private static SupportKind KindOfById(int supportId)
    {
        switch (supportId)
        {
            case Hanachirusato:
            case Liben:
            case Paimon:
            case Ellin:
            case Katheryne:
            case Tubby:
            case Xudong:
            case KidKujirai:
            case Setaria:
            case Seymour:
            case Atea:
            case Mamere:
                return SupportKind.Companion;

            case ParametricTransformer:
            case TreasureSeekingSeelie:
            case MementoLens:
                return SupportKind.Item;

            default:
                return SupportKind.Location;
        }
    }

    /// <summary>True for the supports this resolver actually applies. The rest are pull/economy effects.</summary>
    public static bool AffectsMatchResult(int supportId)
    {
        switch (supportId)
        {
            case SeiraiIsland:
            case Liben:
            case WangshuInn:
            case GrandNarukamiShrine:
            case Paimon:
            case Ellin:
            case ParametricTransformer:
            case FavoniusCathedral:
            case DawnWinery:
            case StormterrorsLair:
            case Vanarana:
            case Katheryne:
            case Tubby:
            case KidKujirai:
            case TreasureSeekingSeelie:
            case MementoLens:
            case CentralLaboratoryRuins:
            case Mamere:
                return true;

            default:
                return false;
        }
    }

    // ===================== internal working data =====================

    private class SupportEntry
    {
        public SupportCardData Data;
        public int NetId;
        public int Mult = 1; // 1 normally, 2 when this support's effect is doubled
    }

    private class Side
    {
        public readonly List<ActionCardData> Actions = new List<ActionCardData>();
        public readonly List<SupportEntry> Supports = new List<SupportEntry>(); // in the order they were played
        public readonly SideOutcome Outcome = new SideOutcome();

        public int Attack;
        public int Defense;
        public int DefenseCardCount;
        public int MonsterCount;
        public int WeaponArtefactCount;

        public int ActionCount { get { return Actions.Count; } }
        public int SupportCount { get { return Supports.Count; } }
        public int TotalCards { get { return Actions.Count + Supports.Count; } }
    }

    // ===================== public entry point =====================

    /// <summary>
    /// Works out one round for both players.
    /// cardsA / cardsB are the played card ids as sent over the network (Support ids are offset, see CardNetId).
    /// </summary>
    public static void Resolve(
        int[] cardsA, int[] cardsB, DeckManager deck,
        SupportMatchState stateA, SupportMatchState stateB,
        out SideOutcome outcomeA, out SideOutcome outcomeB)
    {
        Side a = BuildSide(cardsA, deck);
        Side b = BuildSide(cardsB, deck);

        // 1b. elemental reactions between the played Action cards (see ElementalReactions)
        ApplyReactions(a);
        ApplyReactions(b);

        // 2. who gets doubled
        BuildMultipliers(a);
        BuildMultipliers(b);

        // 3. each player's own modifiers
        ApplyOwnModifiers(a, b);
        ApplyOwnModifiers(b, a);

        // 4. attack reduction aimed at the other player
        ApplyAttackReduction(a, b);
        ApplyAttackReduction(b, a);

        // 5. damage
        int damageByA = ComputeDamage(a, b);
        int damageByB = ComputeDamage(b, a);

        // 6. after the round
        ApplyAfterRound(a, b, damageByA > damageByB);
        ApplyAfterRound(b, a, damageByB > damageByA);

        FillGachaInfo(a);
        FillGachaInfo(b);

        a.Outcome.Attack = a.Attack;
        a.Outcome.Defense = a.Defense;
        a.Outcome.DamageDealt = damageByA;

        b.Outcome.Attack = b.Attack;
        b.Outcome.Defense = b.Defense;
        b.Outcome.DamageDealt = damageByB;

        outcomeA = a.Outcome;
        outcomeB = b.Outcome;
    }

    // ===================== step 1: read the played cards =====================

    private static Side BuildSide(int[] netIds, DeckManager deck)
    {
        var side = new Side();
        if (netIds == null || deck == null) return side;

        foreach (int netId in netIds)
        {
            int id = CardNetId.Decode(netId);

            if (CardNetId.IsSupport(netId))
            {
                SupportCardData support = deck.loadedSupportCards != null ? deck.loadedSupportCards.Find(c => c.Id == id) : null;
                if (support == null)
                {
                    Debug.LogWarning($"[SupportEffectResolver] Support card ID {id} not found in the Support list!");
                    continue;
                }
                side.Supports.Add(new SupportEntry { Data = support, NetId = netId });
                continue;
            }

            ActionCardData action = deck.loadedActionCards != null ? deck.loadedActionCards.Find(c => c.Id == id) : null;
            if (action == null)
            {
                Debug.LogWarning($"[SupportEffectResolver] Action card ID {id} not found in the Action list!");
                continue;
            }

            side.Actions.Add(action);

            if (Is(action.Role, "Attack"))
            {
                side.Attack += action.Value;
            }
            else if (Is(action.Role, "Defense"))
            {
                side.Defense += action.Value;
                side.DefenseCardCount++;
            }

            if (Is(action.Category, "Monster")) side.MonsterCount++;
            if (Is(action.Category, "Weapon") || Is(action.Category, "Artefact")) side.WeaponArtefactCount++;
        }

        return side;
    }

    /// <summary>Adds +1 attack / +1 defense for every elemental reaction among this player's played Action cards.</summary>
    private static void ApplyReactions(Side side)
    {
        var elements = new HashSet<string>();
        var seen = new List<string>();

        foreach (ActionCardData card in side.Actions)
        {
            string e = CardRules.ElementOf(card.Id);
            if (e != null)
            {
                elements.Add(e);
                seen.Add(card.Name + "=" + e);
            }
            else if (Is(card.Category, "Character") || Is(card.Category, "Monster"))
            {
                // Most likely the card ID in ActionDeck.csv doesn't match the table in CardRules.ElementById.
                Debug.LogWarning($"[Reaction] No element known for {card.Category} '{card.Name}' (ID {card.Id}). Add it to CardRules.ElementById.");
            }
        }

        List<ElementalReactions.Reaction> reactions = ElementalReactions.Find(elements);

        // Always logged (host Console) so you can see what was checked, even when nothing reacted.
        Debug.Log($"[Reaction] Elements played: {(seen.Count == 0 ? "none" : string.Join(", ", seen))} -> {reactions.Count} reaction(s)");

        foreach (ElementalReactions.Reaction r in reactions)
        {
            side.Attack += r.Attack;
            side.Defense += r.Defense;
            string gain = r.Attack > 0 ? "+" + r.Attack + " attack" : "+" + r.Defense + " defense";
            side.Outcome.Notes.Add("Reaction " + r.Name + ": " + gain);
        }
    }

    private static bool Is(string value, string expected)
    {
        return value != null && string.Equals(value.Trim(), expected, System.StringComparison.OrdinalIgnoreCase);
    }

    // ===================== step 2: doubling =====================

    private static void BuildMultipliers(Side side)
    {
        bool hasDawnWinery = Has(side, DawnWinery);
        bool hasTubby = Has(side, Tubby);

        // Each Stormterror's Lair doubles one OTHER support played this round (the next one in the played order).
        int lairsToSpend = 0;
        foreach (SupportEntry s in side.Supports)
        {
            if (s.Data.Id == StormterrorsLair) lairsToSpend++;
        }

        foreach (SupportEntry s in side.Supports)
        {
            int id = s.Data.Id;

            if (id != StormterrorsLair && lairsToSpend > 0)
            {
                s.Mult *= 2;
                lairsToSpend--;
                Note(side, s, "doubled by Stormterror's Lair");
            }

            // Dawn Winery doubles Companions played with it, Tubby doubles Locations played with it.
            SupportKind kind = KindOf(s.Data);

            if (hasDawnWinery && id != DawnWinery && kind == SupportKind.Companion)
            {
                s.Mult *= 2;
                Note(side, s, "doubled by Dawn Winery");
            }
            if (hasTubby && id != Tubby && kind == SupportKind.Location)
            {
                s.Mult *= 2;
                Note(side, s, "doubled by Tubby");
            }
        }
    }

    // ===================== step 3: a player's own modifiers =====================

    private static void ApplyOwnModifiers(Side me, Side opp)
    {
        // a) flat bonuses / penalties
        foreach (SupportEntry s in me.Supports)
        {
            int m = s.Mult;

            switch (s.Data.Id)
            {
                case FavoniusCathedral: // +2 defense if you have defense cards
                    if (me.DefenseCardCount > 0)
                    {
                        me.Defense += 2 * m;
                        Note(me, s, $"+{2 * m} defense");
                    }
                    break;

                case CentralLaboratoryRuins: // 3+ Monster cards: +1 attack
                    if (me.MonsterCount >= 3)
                    {
                        me.Attack += 1 * m;
                        Note(me, s, $"+{1 * m} attack ({me.MonsterCount} Monster cards)");
                    }
                    break;

                case GrandNarukamiShrine: // odd number of cards used: +1 attack, even: -1 attack
                    {
                        int delta = (me.TotalCards % 2 == 1 ? 1 : -1) * m;
                        me.Attack += delta;
                        Note(me, s, $"{(delta > 0 ? "+" : "")}{delta} attack ({me.TotalCards} cards used)");
                        break;
                    }

                case Mamere: // played more Weapon/Artefact cards than the opponent: +2 attack
                    if (me.WeaponArtefactCount > opp.WeaponArtefactCount)
                    {
                        me.Attack += 2 * m;
                        Note(me, s, $"+{2 * m} attack ({me.WeaponArtefactCount} vs {opp.WeaponArtefactCount} Weapon/Artefact)");
                    }
                    break;
            }
        }
        me.Attack = Mathf.Max(0, me.Attack);

        // b) conversion: all defense points become attack
        foreach (SupportEntry s in me.Supports)
        {
            if (s.Data.Id != ParametricTransformer) continue;

            int converted = me.Defense * s.Mult;
            me.Attack += converted;
            me.Defense = 0;
            Note(me, s, $"converted defense into +{converted} attack");
        }

        // c) attack multipliers
        foreach (SupportEntry s in me.Supports)
        {
            if (s.Data.Id != TreasureSeekingSeelie) continue;

            int factor = 2 * s.Mult; // "double" (x2), or x4 when this effect itself is doubled
            me.Attack *= factor;
            Note(me, s, $"attack x{factor}");
        }
        me.Attack = Mathf.Max(0, me.Attack);

        // d) defense multipliers: Liben doubles defense when it is more than 10
        foreach (SupportEntry s in me.Supports)
        {
            if (s.Data.Id != Liben) continue;

            if (me.Defense > 10)
            {
                int factor = 2 * s.Mult;
                me.Defense *= factor;
                Note(me, s, $"defense x{factor}");
            }
        }
    }

    // ===================== step 4: attack reduction aimed at the opponent =====================

    private static void ApplyAttackReduction(Side me, Side opp)
    {
        foreach (SupportEntry s in me.Supports)
        {
            if (s.Data.Id != WangshuInn) continue;

            int amount = 1 * s.Mult;
            opp.Attack = Mathf.Max(0, opp.Attack - amount);
            Note(me, s, $"opponent attack -{amount}");
        }
    }

    // ===================== step 5: damage =====================

    private static int ComputeDamage(Side me, Side opp)
    {
        // Memento Lens: the opponent is immune to all attacks this round.
        foreach (SupportEntry s in opp.Supports)
        {
            if (s.Data.Id == MementoLens)
            {
                Note(opp, s, "immune to all attacks this round");
                return 0;
            }
        }

        int damage = Mathf.Max(0, me.Attack - opp.Defense);

        // Ellin: +3 damage if you played fewer action cards than support cards (once per round).
        foreach (SupportEntry s in me.Supports)
        {
            if (s.Data.Id != Ellin) continue;

            if (me.ActionCount < me.SupportCount)
            {
                damage += 3 * s.Mult;
                Note(me, s, $"+{3 * s.Mult} damage ({me.ActionCount} action vs {me.SupportCount} support cards)");
            }
            break; // only once per round, even with two copies
        }

        // Seirai Island: double damage when your damage is more than 10.
        foreach (SupportEntry s in me.Supports)
        {
            if (s.Data.Id != SeiraiIsland) continue;

            if (damage > 10)
            {
                int factor = 2 * s.Mult;
                damage *= factor;
                Note(me, s, $"damage x{factor}");
            }
        }

        return damage;
    }

    // ===================== step 6: after the round =====================

    private static void ApplyAfterRound(Side me, Side opp, bool wonRound)
    {
        foreach (SupportEntry s in me.Supports)
        {
            int m = s.Mult;

            switch (s.Data.Id)
            {
                case Paimon: // get 2 tokens
                    me.Outcome.TokenReward += 2 * m;
                    Note(me, s, $"+{2 * m} tokens");
                    break;

                case Vanarana: // reclaim a token that was used to draw
                    me.Outcome.TokenReward += 1 * m;
                    Note(me, s, $"+{1 * m} token reclaimed");
                    break;

                case Katheryne: // get a token if you win the round
                    if (wonRound)
                    {
                        me.Outcome.TokenReward += 1 * m;
                        Note(me, s, $"+{1 * m} token (won the round)");
                    }
                    break;

                case KidKujirai: // disappears if you played fewer support cards than the opponent
                    if (me.SupportCount < opp.SupportCount)
                    {
                        me.Outcome.DiscardedNetIds.Add(s.NetId);
                        Note(me, s, $"discarded ({me.SupportCount} vs {opp.SupportCount} support cards)");
                    }
                    break;

                default:
                    if (!AffectsMatchResult(s.Data.Id))
                    {
                        Note(me, s, "pull/economy effect (applied by the gacha side at the end of the round)");
                    }
                    break;
            }
        }
    }

    // ===================== hand-over to the gacha side =====================

    private static void FillGachaInfo(Side side)
    {
        SideOutcome outcome = side.Outcome;
        outcome.PlayedCardCount = side.TotalCards;

        int removed = outcome.DiscardedNetIds.Count; // e.g. Kid Kujirai

        foreach (SupportEntry s in side.Supports)
        {
            bool spent = !Is(s.Data.EffectType, "Forever"); // Immediate supports are used up

            outcome.PlayedSupports.Add(new PlayedSupportInfo
            {
                SupportId = s.Data.Id,
                NetId = s.NetId,
                Mult = s.Mult,
                Spent = spent
            });

            if (spent) removed++;
        }

        outcome.RemovedFromHandCount = removed;
    }

    // ===================== helpers =====================

    private static bool Has(Side side, int supportId)
    {
        foreach (SupportEntry s in side.Supports)
        {
            if (s.Data.Id == supportId) return true;
        }
        return false;
    }

    private static void Note(Side side, SupportEntry s, string text)
    {
        string tag = s.Mult > 1 ? $" [effect x{s.Mult}]" : "";
        side.Outcome.Notes.Add($"{s.Data.Name}: {text}{tag}");
    }
}