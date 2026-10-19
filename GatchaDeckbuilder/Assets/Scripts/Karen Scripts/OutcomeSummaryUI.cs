using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// Fills the "#" placeholders on the Outcome page with the real match and pull numbers.
///
/// SETUP: add this script to the Outcome panel (the object that gets switched on when the match ends, for example
/// the same panel EndTurnManager uses as Game Over Panel). No fields to drag in: it searches every TMP text under
/// the panel for lines such as "Total Rounds: #" and replaces whatever follows the colon with the number.
/// It refreshes each time the panel is switched on.
/// </summary>
public class OutcomeSummaryUI : MonoBehaviour
{
    private void OnEnable() => Refresh();

    public void Refresh()
    {
        (string label, int value)[] rows =
        {
            ("Total Rounds",  MatchStats.Rounds),
            ("Damage dealt",  MatchStats.DamageDealt),
            ("Damage taken",  MatchStats.DamageTaken),
            ("Tokens spent",  MatchStats.TokensSpent),
            ("Action pulls",  MatchStats.ActionPulls),
            ("Action pity",   MatchStats.ActionPity),
            ("Support pulls", MatchStats.SupportPulls),
            ("Support pity",  MatchStats.SupportPity),
            ("50/50 won",     MatchStats.Won5050),
            ("50/50 lost",    MatchStats.Lost5050),
        };

        foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
        {
            string s = t.text;
            if (string.IsNullOrEmpty(s)) continue;

            string updated = s;
            foreach (var row in rows)
            {
                // "Label: <anything up to the next space / line end>"  ->  "Label: value"
                string pattern = "(" + Regex.Escape(row.label) + @"\s*:\s*)\S*";
                updated = Regex.Replace(updated, pattern, m => m.Groups[1].Value + row.value, RegexOptions.IgnoreCase);
            }

            if (updated != s) t.text = updated;
        }
    }
}