using System;
using System.Collections.Generic;
using System.Text;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>
    /// The roster board's rules (the HUD's <c>CTF_RosterBoard</c> draws them): everyone in the match,
    /// bots and players alike, by team, best first, with flags, eliminations and deaths.
    /// </summary>
    public static class CTF_Board {
        /// <summary>Columns of a row, as TMP horizontal positions (share of the column's width).</summary>
        public const string FlagsAt = "58%", KillsAt = "72%", DeathsAt = "86%";

        /// <summary>Best first: captures, then eliminations, then fewest deaths, then name.</summary>
        public static int Compare(in CTF_BotView a, in CTF_BotView b) {
            if (a.captures != b.captures) return b.captures.CompareTo(a.captures);
            if (a.kills != b.kills) return b.kills.CompareTo(a.kills);
            if (a.deaths != b.deaths) return a.deaths.CompareTo(b.deaths);
            return string.CompareOrdinal(a.name ?? "", b.name ?? "");
        }

        /// <summary>Fills <paramref name="into"/> with <paramref name="team"/>'s rows from both lists, sorted.</summary>
        public static void Rows(CTF_BotView[] bots, CTF_BotView[] players, CTF_Team team, List<CTF_BotView> into) {
            into.Clear();
            if (players != null) foreach (var p in players) if (p.team == team) into.Add(p);
            if (bots != null) foreach (var b in bots) if (b.team == team) into.Add(b);
            into.Sort((x, y) => Compare(x, y));
        }

        /// <summary>
        /// One team's rows as rich text: a header line, then "name  F  K  D" per row, your own row
        /// highlighted and a down (dead) one dimmed. <paramref name="maxRows"/> caps the list ("+2 more").
        /// </summary>
        public static string Lines(List<CTF_BotView> rows, int myId, int maxRows, string highlightHex, StringBuilder sb) {
            sb.Clear();
            sb.Append("<size=80%><color=#9AA3B2>Name<pos=").Append(FlagsAt).Append(">Flags<pos=").Append(KillsAt).Append(">Elims<pos=")
              .Append(DeathsAt).Append(">Deaths</color></size>");
            int shown = Math.Min(rows.Count, Math.Max(1, maxRows));
            for (int i = 0; i < shown; i++) {
                var r = rows[i];
                bool me = r.actorId == myId;
                sb.Append('\n');
                if (me) sb.Append("<b><color=").Append(highlightHex).Append('>');
                else if (!r.alive) sb.Append("<color=#8A93A3>");
                sb.Append(Clip(me ? "You" : r.name, 16)).Append("<pos=").Append(FlagsAt).Append('>').Append(r.captures)
                  .Append("<pos=").Append(KillsAt).Append('>').Append(r.kills)
                  .Append("<pos=").Append(DeathsAt).Append('>').Append(r.deaths);
                if (me) sb.Append("</color></b>");
                else if (!r.alive) sb.Append("</color>");
            }
            if (rows.Count > shown) sb.Append("\n<size=80%><color=#9AA3B2>+").Append(rows.Count - shown).Append(" more</color></size>");
            return sb.ToString();
        }

        /// <summary><paramref name="name"/> cut to <paramref name="max"/> characters with an ellipsis; tags stripped of their angle brackets.</summary>
        public static string Clip(string name, int max) {
            if (string.IsNullOrEmpty(name)) return "?";
            name = name.Replace("<", "").Replace(">", "");
            return name.Length <= max ? name : name.Substring(0, Math.Max(1, max - 1)) + "…";
        }
    }
}
