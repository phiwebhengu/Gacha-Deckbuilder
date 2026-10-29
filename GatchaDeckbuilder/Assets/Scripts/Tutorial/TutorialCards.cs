/// <summary>
/// The cards the tutorial hands out, by their ID in ActionDeck.csv / SupportDeck.csv.
/// If the CSVs change, these are the only numbers to update.
/// </summary>
public static class TutorialCards
{
    // Action cards
    public const int Zhongli = 2;                // Legendary Character, Geo, 10 Defense
    public const int RaidenShogun = 3;           // Legendary Character, Electro, 10 Attack (the featured card)
    public const int Dvalin = 7;                 // Rare Monster, Anemo, 7 Attack
    public const int Azhdaha = 8;                // Rare Monster, Geo, 8 Defense
    public const int MountainKing = 10;          // Rare Monster, Dendro, 7 Defense
    public const int AnemoSamachurl = 14;        // Common Monster, Anemo, 2 Defense
    public const int GeoRifthound = 15;          // Common Monster, Geo, 3 Attack
    public const int WingedDendroshroom = 17;    // Common Monster, Dendro, 4 Attack
    public const int TaintedHydroPhantasm = 18;  // Common Monster, Hydro, 5 Defense
    public const int PyroWhopperflower = 19;     // Common Monster, Pyro, 4 Attack
    public const int FavoniusSword = 24;         // Rare Weapon, Sword, 7 Attack

    // Support cards
    public const int JadeChamber = 1;            // Legendary, Forever (the featured Support card)
    public const int WangshuInn = 5;             // Rare, Forever: -1 opponent attack
    public const int Paimon = 8;                 // Rare, Immediate: the guide's portrait
    public const int TreasureSeekingSeelie = 26; // Common, Immediate: double your attack

    public static int Action(int id) => CardNetId.Encode(id, false);
    public static int Support(int id) => CardNetId.Encode(id, true);
}
