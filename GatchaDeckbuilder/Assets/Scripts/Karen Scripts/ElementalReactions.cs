using System.Collections.Generic;

/// <summary>
/// Elemental reactions between the Action cards a player plays in the SAME round.
///
///   +1 ATTACK:  Super Conduct (Cryo + Electro)   Overload (Electro + Pyro)   Burn (Pyro + Dendro)
///               Swirl: Anemo + any of Pyro / Cryo / Electro / Hydro  (each of those four gives +1)
///   +1 DEFENSE: Melt (Cryo + Pyro)   Freeze (Cryo + Hydro)   Vaporise (Pyro + Hydro)
///               Crystallize: Geo + any of Pyro / Cryo / Electro / Hydro  (each of those four gives +1)
///
/// HOW IT COUNTS (change here if you meant otherwise):
///  - It looks at which elements are present among the cards played (Characters, Monsters and Artefacts).
///    Each reaction can happen only ONCE per round, no matter how many cards of that element were played.
///  - Anemo and Geo react with each of the four elements separately: Anemo + Pyro + Cryo = two Swirls = +2 attack.
///  - Reactions are added to the attack / defense totals BEFORE Support effects, so doublers and conversions
///    (Parametric Transformer, Treasure-Seeking Seelie, Liben...) apply to them too.
///  - Each player's reactions only depend on their own cards.
/// </summary>
public static class ElementalReactions
{
    public struct Reaction
    {
        public string Name;
        public int Attack;
        public int Defense;
    }

    private static readonly string[] FourElements = { "Pyro", "Cryo", "Electro", "Hydro" };

    public static List<Reaction> Find(ICollection<string> elements)
    {
        var found = new List<Reaction>();
        if (elements == null || elements.Count == 0) return found;

        bool Has(string e) => elements.Contains(e);

        // Attack reactions (red lines)
        if (Has("Cryo") && Has("Electro")) found.Add(new Reaction { Name = "Super Conduct", Attack = 1 });
        if (Has("Electro") && Has("Pyro")) found.Add(new Reaction { Name = "Overload", Attack = 1 });
        if (Has("Pyro") && Has("Dendro")) found.Add(new Reaction { Name = "Burn", Attack = 1 });

        // Defense reactions (blue lines)
        if (Has("Cryo") && Has("Pyro")) found.Add(new Reaction { Name = "Melt", Defense = 1 });
        if (Has("Cryo") && Has("Hydro")) found.Add(new Reaction { Name = "Freeze", Defense = 1 });
        if (Has("Pyro") && Has("Hydro")) found.Add(new Reaction { Name = "Vaporise", Defense = 1 });

        // Anemo (attack) and Geo (defense) with each of the four elements
        foreach (string e in FourElements)
        {
            if (Has("Anemo") && Has(e)) found.Add(new Reaction { Name = "Swirl (" + e + ")", Attack = 1 });
            if (Has("Geo") && Has(e)) found.Add(new Reaction { Name = "Crystallize (" + e + ")", Defense = 1 });
        }

        return found;
    }
}