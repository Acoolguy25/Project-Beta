using System;
using UnityEngine;

namespace RyanAssets.Retention {
    /// <summary>
    /// The daily play streak: the first finished match of each UTC day pays a bonus that grows with
    /// consecutive days up to <see cref="maxBonusDays"/>; missing a day starts the count again at 1.
    /// The streak itself keeps counting past the cap ("Day 12 streak"), only the bonus stops growing.
    /// </summary>
    [Serializable]
    public sealed class StreakRules {
        [Tooltip("Game XP for day 1 of a streak.")]
        [Min(0)] public int firstDayXp = 25;
        [Tooltip("Extra game XP for every further consecutive day, until maxBonusDays.")]
        [Min(0)] public int xpPerExtraDay = 15;
        [Tooltip("Account gold for day 1 of a streak.")]
        [Min(0)] public int firstDayGold = 10;
        [Tooltip("Extra account gold for every further consecutive day, until maxBonusDays.")]
        [Min(0)] public int goldPerExtraDay = 5;
        [Tooltip("The bonus stops growing on this day of a streak.")]
        [Range(1, 365)] public int maxBonusDays = 7;
        [Tooltip("Reward line on the match summary; {0} is the streak day.")]
        public string lineFormat = "Daily streak (day {0})";

        public int BonusXp(int days) => firstDayXp + xpPerExtraDay * (BonusDay(days) - 1);
        public int BonusGold(int days) => firstDayGold + goldPerExtraDay * (BonusDay(days) - 1);

        int BonusDay(int days) => Math.Clamp(days, 1, Math.Max(1, maxBonusDays));

        const string DefaultLineFormat = "Daily streak (day {0})";

        /// <summary>The summary line for a streak of <paramref name="days"/>; a broken <see cref="lineFormat"/> falls back to the default.</summary>
        public string Line(int days) {
            try {
                return string.Format(string.IsNullOrEmpty(lineFormat) ? DefaultLineFormat : lineFormat, days);
            } catch (FormatException) {
                return string.Format(DefaultLineFormat, days);
            }
        }

        public void Validate() {
            firstDayXp = Math.Max(0, firstDayXp);
            xpPerExtraDay = Math.Max(0, xpPerExtraDay);
            firstDayGold = Math.Max(0, firstDayGold);
            goldPerExtraDay = Math.Max(0, goldPerExtraDay);
            maxBonusDays = Math.Clamp(maxBonusDays, 1, 365);
        }

        public StreakRules Clone() => (StreakRules)MemberwiseClone();
    }

    /// <summary>What <see cref="RetentionRules.ClaimStreak"/> did.</summary>
    public readonly struct StreakClaim {
        /// <summary>False when today's bonus was already paid.</summary>
        public readonly bool Paid;
        /// <summary>The streak length after the claim (1 = a new streak).</summary>
        public readonly int Days;
        public readonly int Xp;
        public readonly int Gold;
        /// <summary>True when a streak of 2+ days had lapsed and this claim started over.</summary>
        public readonly bool Restarted;

        public StreakClaim(bool paid, int days, int xp, int gold, bool restarted) {
            Paid = paid;
            Days = days;
            Xp = xp;
            Gold = gold;
            Restarted = restarted;
        }
    }
}
