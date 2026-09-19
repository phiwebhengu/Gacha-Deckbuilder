using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GachaSystem
{
    [CreateAssetMenu(fileName = "SupportDeckDatabase", menuName = "Gacha/Support Deck Database")]
    public class SupportDeckDatabase : ScriptableObject
    {
        [Header("CSV Import Settings")]
        [Tooltip("Drag your SupportDeck.csv file here")]
        [SerializeField] private TextAsset csvFile;

        [Header("Loaded Deck Data")]
        [SerializeField] private List<SupportCardData> supportCards = new List<SupportCardData>();

        public List<SupportCardData> SupportCards => supportCards;

        private void OnValidate()
        {
            if (csvFile != null)
            {
                ParseCSV();
            }
        }

        [ContextMenu("Reload CSV Data")]
        public void ParseCSV()
        {
            if (csvFile == null)
            {
                Debug.LogWarning("[SupportDeckDatabase] No CSV file assigned!");
                return;
            }

            supportCards.Clear();

            string linePattern = @"\r\n|\n\r|\n|\r";
            string[] lines = Regex.Split(csvFile.text, linePattern);

            if (lines.Length <= 1) return;

            // Skip header row
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                string[] fields = ParseCsvLine(lines[i]);

                if (fields.Length < 6)
                {
                    Debug.LogWarning($"[SupportDeckDatabase] Line {i + 1} has insufficient fields. Skipping.");
                    continue;
                }

                SupportCardData card = new SupportCardData();

                // ID
                int.TryParse(fields[0].Trim(), out card.id);

                // Name
                card.cardName = fields[1].Trim();

                // Tier / Rarity
                string rarityStr = fields[2].Trim();
                card.tier = rarityStr.ToLower() switch
                {
                    "legendary" => Rarity.Legendary,
                    "rare" => Rarity.Rare,
                    _ => Rarity.Common
                };

                // Effect
                card.effectDescription = fields[3].Trim();

                // Effect Type
                card.effectType = fields[4].Trim();

                // Copies
                int.TryParse(fields[5].Trim(), out card.copies);

                supportCards.Add(card);
            }

            Debug.Log($"[SupportDeckDatabase] Successfully loaded {supportCards.Count} support card entries!");
        }

        private string[] ParseCsvLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            string currentField = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField);
                    currentField = "";
                }
                else
                {
                    currentField += c;
                }
            }

            result.Add(currentField);
            return result.ToArray();
        }

        public SupportCardData GetRandomCardByRarity(Rarity targetRarity, System.Random rng)
        {
            List<SupportCardData> matching = supportCards.FindAll(c => c.tier == targetRarity);
            if (matching.Count == 0) return null;

            int index = rng.Next(0, matching.Count);
            return matching[index];
        }
    }
}