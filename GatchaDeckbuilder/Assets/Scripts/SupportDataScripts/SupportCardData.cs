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
    }
}