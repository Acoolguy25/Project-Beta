using System;
using System.Collections.Generic;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>One standing goal: what it counts, how far along and the next milestone.</summary>
    public readonly struct CTF_Goal {
        public readonly string label;
        public readonly int value, target;
        public CTF_Goal(string label, int value, int target) {
            this.label = label;
            this.value = value;
            this.target = target;
        }
        public float Progress => target > 0 ? Math.Clamp(value / (float)target, 0f, 1f) : 1f;
        public override string ToString() => $"{label} {value}/{target}";
    }

    /// <summary>
    /// Standing goals that never run out: milestone ladders over what the save already counts (wins,
    /// MVPs, best streak, captures) and mastery of each gun. They show on the career card beside the
    /// challenges panel (J) for everyone, and at the cap the summary card names the nearest one.
    /// </summary>
    public static class CTF_Goals {
        public static readonly int[] WinSteps = { 5, 10, 25, 50, 100, 250, 500, 1000 };
        public static readonly int[] MvpSteps = { 3, 10, 25, 50, 100, 250, 500 };
        public static readonly int[] StreakSteps = { 5, 8, 12, 16, 20, 25, 30 };
        public static readonly int[] CaptureSteps = { 10, 25, 50, 100, 250, 500, 1000 };

        /// <summary>The first milestone above <paramref name="value"/>; past the ladder, the next multiple of its last step.</summary>
        public static int Next(int value, int[] steps) {
            value = Math.Max(0, value);
            foreach (int step in steps) if (step > value) return step;
            int last = steps[steps.Length - 1];
            return (value / last + 1) * last;
        }

        /// <summary>
        /// The mark before a mastered gun's name in the kill feed. A check, not a star: the HUD's Inter
        /// atlases are static and hold the check (U+2713) but no star.
        /// </summary>
        public const string MasteryMark = "\u2713";

        /// <summary>Every goal, in card order: wins, MVPs, best streak, captures, then one mastery per gun not yet mastered.</summary>
        public static void All(in CTF_CareerBroadcast career, List<CTF_Goal> into) {
            into.Clear();
            into.Add(new CTF_Goal("Wins", career.wins, Next(career.wins, WinSteps)));
            into.Add(new CTF_Goal("MVPs", career.mvps, Next(career.mvps, MvpSteps)));
            into.Add(new CTF_Goal("Best streak", career.bestStreak, Next(career.bestStreak, StreakSteps)));
            into.Add(new CTF_Goal("Captures", career.captures, Next(career.captures, CaptureSteps)));
            for (int i = 0; i < CTF_Loadouts.Weapons.Length; i++) {
                int kills = career.gunKills != null && i < career.gunKills.Length ? career.gunKills[i] : 0;
                if (kills >= CTF_Retention.MasteryKills) continue;
                into.Add(new CTF_Goal($"{CTF_Loadouts.Weapons[i].title} mastery", kills, CTF_Retention.MasteryKills));
            }
        }

        /// <summary>The goal nearest done ("Wins 34/50"), for the summary card at the cap.</summary>
        public static CTF_Goal Nearest(in CTF_CareerBroadcast career, List<CTF_Goal> scratch) {
            All(career, scratch);
            CTF_Goal best = scratch[0];
            foreach (var goal in scratch) if (goal.Progress > best.Progress) best = goal;
            return best;
        }

        /// <summary>
        /// The career card's rows as rich text, one goal a line: the label on the left, "34 / 50" in a
        /// column, done goals' count in <paramref name="doneHex"/>. Mastered guns get a gold line of their own.
        /// </summary>
        /// <summary>Where the card's numbers start (share of the row width).</summary>
        public const string CardColumn = "68%";

        public static string CardLines(in CTF_CareerBroadcast career, List<CTF_Goal> scratch, string doneHex, System.Text.StringBuilder sb) {
            All(career, scratch);
            sb.Clear();
            foreach (var goal in scratch) {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(goal.label).Append("<pos=").Append(CardColumn).Append('>').Append(goal.value).Append(" / ").Append(goal.target);
            }
            for (int i = 0; career.gunKills != null && i < career.gunKills.Length && i < CTF_Loadouts.Weapons.Length; i++) {
                if (!Mastered(career.gunKills[i])) continue;
                // "✓ Marksman Rifle" | "mastered": the word sits in the number column, as the longest gun name
                // with " mastered" after it would run into that column on the 320 px card.
                sb.Append('\n').Append("<color=").Append(doneHex).Append('>').Append(MasteryMark).Append(' ')
                    .Append(CTF_Loadouts.Weapons[i].title).Append("<pos=").Append(CardColumn).Append(">mastered</color>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// The card's header line: "74 matches · 512 eliminations". No level: the card opens beside the J panel, whose
        /// top row already reads "Level 30 · Max level" (and the career is sent at the result, before the kit pays the match).
        /// </summary>
        public static string CardHeader(in CTF_CareerBroadcast career) =>
            $"{Plural(career.matches, "match", "matches")} \u00b7 {Plural(career.kills, "elimination", "eliminations")}";

        static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

        /// <summary>Whether <paramref name="kills"/> with a gun master it.</summary>
        public static bool Mastered(int kills) => kills >= CTF_Retention.MasteryKills;
    }
}
