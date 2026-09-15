using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public class ActionCardData
{
    public int Id;
    public string Name;
    public string Category;    // Character / Monster / Weapon / Artefact
    public string Tier;        // Common / Rare / Legendary
    public string WeaponType;  // Polearm / Claymore / Sword / "" (blank if not a Character or Weapon)
    public int Value;          // Attack or Defense number
    public string Role;        // Attack / Defense
    public int Copies;
}

[Serializable]
public class SupportCardData
{
    public int Id;
    public string Name;
    public string Tier;         // Common / Rare / Legendary
    public string Effect;
    public string EffectType;  // Immediate / Forever
    public int Copies;
}

public static class CardLoader
{
    // ---------- Public entry points ----------

    public static List<ActionCardData> LoadActionDeck(TextAsset csvFile)
    {
        var rows = ParseCsv(csvFile.text);
        var cards = new List<ActionCardData>(rows.Count);

        // rows[0] is the header row — skip it
        for (int i = 1; i < rows.Count; i++)
        {
            var f = rows[i];
            if (f.Count < 8) continue;

            cards.Add(new ActionCardData
            {
                Id = int.Parse(f[0]),
                Name = f[1],
                Category = f[2],
                Tier = f[3],
                WeaponType = f[4],
                Value = int.Parse(f[5]),
                Role = f[6],
                Copies = int.Parse(f[7])
            });
        }
        return cards;
    }

    public static List<SupportCardData> LoadSupportDeck(TextAsset csvFile)
    {
        var rows = ParseCsv(csvFile.text);
        var cards = new List<SupportCardData>(rows.Count);

        for (int i = 1; i < rows.Count; i++)
        {
            var f = rows[i];
            if (f.Count < 6) continue;

            cards.Add(new SupportCardData
            {
                Id = int.Parse(f[0]),
                Name = f[1],
                Tier = f[2],
                Effect = f[3],
                EffectType = f[4],
                Copies = int.Parse(f[5])
            });
        }
        return cards;
    }

    // ---------- CSV parsing ----------
    // Minimal RFC-4180 parser: handles quoted fields, commas inside quotes,
    // escaped "" quotes, and \r\n or \n line endings. Good enough for the
    // ActionDeck.csv / SupportDeck.csv files exported alongside this script.

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < len && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    i++;
                    break;
                case '\r':
                    i++; // ignore, \n handles the line break
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                    i++;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }

        // last field/row if the file doesn't end with a newline
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
        }

        return rows;
    }
}

