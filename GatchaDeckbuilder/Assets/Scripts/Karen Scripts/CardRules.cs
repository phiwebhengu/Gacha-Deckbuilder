using System;
using System.Collections.Generic;

/// <summary>
/// "Which Action cards may be played together" rules.
///
///  - A WEAPON card can only be played together with a CHARACTER card.
///  - An ARTEFACT card can only be played together with another card of the SAME ELEMENT
///    (a Character or Monster of that element; two Artefacts do not unlock each other).
///
/// Support cards are ignored by these rules.
/// </summary>
public static class CardRules
{
    // Set to true if a Weapon should also have to match its Character's weapon type (Bow with Bow, Sword with Sword...).
    // The rule as described only asks for "a character", so this is off.
    public const bool WeaponMustMatchCharacterType = false;

    // Element of every Action card by its ID in ActionDeck.csv. Weapons have no element.
    // IDs 8 (Azhdaha) and 10 (Mountain King) are my best guess: change them here if your design says otherwise.
    private static readonly Dictionary<int, string> ElementById = new Dictionary<int, string>
    {
        // Characters
        { 1, "Anemo" }, { 2, "Geo" }, { 3, "Electro" }, { 4, "Dendro" }, { 5, "Hydro" }, { 6, "Pyro" },
        // Monsters
        { 7, "Anemo" },   // Dvalin
        { 8, "Geo" },     // Azhdaha            (guess)
        { 9, "Electro" }, // Thunder Manifestation
        { 10, "Dendro" }, // Gluttonous Yumkasaur Mountain King (guess)
        { 11, "Hydro" },  // Hydro Tupla
        { 12, "Pyro" },   // Goldflame Qucusaur Tyrant
        { 13, "Cryo" },   // Cryo Hypostasis
        { 14, "Anemo" },  // Anemo Samachurl
        { 15, "Geo" },    // Geo Rifthound
        { 16, "Electro" },// Electro Abyss Mage
        { 17, "Dendro" }, // Winged Dendroshroom
        { 18, "Hydro" },  // Tainted Hydro Phantasm
        { 19, "Pyro" },   // Pyro Whopperflower
        { 20, "Cryo" },   // Large Cryo Slime
        // Artefacts
        { 26, "Anemo" },  // Viridescent Venerer
        { 27, "Geo" },    // Archaic Petra
        { 28, "Electro" },// Thundering Fury
        { 29, "Dendro" }, // Deepwood Memories
        { 30, "Hydro" },  // Heart of Depth
        { 31, "Pyro" },   // Crimson Witch of Flames
        { 32, "Cryo" },   // Blizzard Strayer
    };

    public static string ElementOf(int actionCardId)
        => ElementById.TryGetValue(actionCardId, out string e) ? e : null;

    private static bool Is(string category, string expected)
        => category != null && string.Equals(category.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <param name="netCardIds">The selected cards (Support cards are skipped).</param>
    /// <param name="actionCards">The loaded Action cards (DeckManager.loadedActionCards).</param>
    /// <returns>true if the selection may be played. Otherwise 'error' says why, in words for the player.</returns>
    public static bool IsPlayAllowed(IEnumerable<int> netCardIds, List<ActionCardData> actionCards, out string error)
    {
        error = null;
        if (netCardIds == null || actionCards == null) return true; // nothing to check against

        var selected = new List<ActionCardData>();
        foreach (int netId in netCardIds)
        {
            if (CardNetId.IsSupport(netId)) continue;
            ActionCardData card = actionCards.Find(c => c.Id == CardNetId.Decode(netId));
            if (card != null) selected.Add(card);
        }

        foreach (ActionCardData card in selected)
        {
            if (Is(card.Category, "Weapon"))
            {
                bool hasCharacter = selected.Exists(o => Is(o.Category, "Character") &&
                    (!WeaponMustMatchCharacterType ||
                     string.Equals((o.WeaponType ?? "").Trim(), (card.WeaponType ?? "").Trim(), StringComparison.OrdinalIgnoreCase)));

                if (!hasCharacter)
                {
                    error = WeaponMustMatchCharacterType
                        ? card.Name + " needs a " + card.WeaponType + " Character played with it."
                        : card.Name + " needs a Character card played with it.";
                    return false;
                }
            }
            else if (Is(card.Category, "Artefact"))
            {
                string element = ElementOf(card.Id);
                bool hasMatch = element == null || selected.Exists(o => o != card &&
                    (Is(o.Category, "Character") || Is(o.Category, "Monster")) &&
                    ElementOf(o.Id) == element);

                if (!hasMatch)
                {
                    error = card.Name + " needs a " + element + " Character or Monster played with it.";
                    return false;
                }
            }
        }

        return true;
    }
}