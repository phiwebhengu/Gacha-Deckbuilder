using System;

namespace GachaSystem
{
    [Serializable]
    public class SupportCardData
    {
        public int id;
        public string cardName;
        public Rarity tier;
        public string effectDescription;
        public string effectType; // e.g., "Forever", "Immediate"
        public int copies;

        public bool IsForever => !string.IsNullOrEmpty(effectType) &&
                                 effectType.Equals("Forever", StringComparison.OrdinalIgnoreCase);
    }
}