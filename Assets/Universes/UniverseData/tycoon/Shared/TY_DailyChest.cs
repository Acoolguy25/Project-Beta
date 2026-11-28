using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// The daily chest's prize table: it pays minutes of income that grow with the play streak, perk tokens
    /// (which cash cannot buy) on set days of every week of the streak, and game XP. Serialized on the
    /// server runner; shared so the chest's sign and the HUD name the same prize the server pays.
    /// </summary>
    [Serializable]
    public sealed class TY_DailyChest {
        [Tooltip("Seconds of the owner's income (rush hours left out) the chest pays on day 1 of a streak...")]
        [Min(0f)] public float incomeSeconds = 300f;
        [Tooltip("...plus this many for every further day of the streak, until growthDays.")]
        [Min(0f)] public float incomeSecondsPerDay = 150f;
        [Tooltip("The cash prize stops growing on this day of a streak.")]
        [Range(1, 30)] public int growthDays = 7;
        [Tooltip("The chest pays at least this much cash.")]
        [Min(0)] public long minimumCash = 500;
        [Tooltip("Game XP on day 1, and the extra XP every further day (until growthDays).")]
        [Min(0)] public int xp = 40, xpPerDay = 20;
        [Tooltip("Length of the token cycle in days: the last day of every cycle pays weekTokens.")]
        [Range(1, 30)] public int week = 7;
        [Tooltip("Perk tokens on the last day of every cycle (a full week of play).")]
        [Min(0)] public int weekTokens = 2;
        [Tooltip("Day of every cycle that pays midWeekTokens (0 = none).")]
        [Range(0, 30)] public int midWeekDay = 3;
        [Min(0)] public int midWeekTokens = 1;

        public void Validate() {
            incomeSeconds = Mathf.Max(0f, incomeSeconds);
            incomeSecondsPerDay = Mathf.Max(0f, incomeSecondsPerDay);
            growthDays = Mathf.Clamp(growthDays, 1, 30);
            minimumCash = Math.Max(0, minimumCash);
            xp = Math.Max(0, xp);
            xpPerDay = Math.Max(0, xpPerDay);
            week = Mathf.Clamp(week, 1, 30);
            weekTokens = Math.Max(0, weekTokens);
            midWeekDay = Mathf.Clamp(midWeekDay, 0, week);
            midWeekTokens = Math.Max(0, midWeekTokens);
        }

        /// <summary>
        /// The streak day a player is on today: the kit's streak (<paramref name="streakDays"/>, last paid on
        /// <paramref name="lastStreakDay"/>) continues from yesterday, is today's already, or starts again at 1.
        /// </summary>
        public static int StreakDayToday(int streakDays, int lastStreakDay, int today) =>
            lastStreakDay == today ? Math.Max(1, streakDays) : lastStreakDay == today - 1 ? Math.Max(0, streakDays) + 1 : 1;

        /// <summary>Whether today's chest is waiting: the save is applied (so it never pays twice) and it was not opened today.</summary>
        public static bool Ready(bool loaded, int openedDay, int today) => loaded && openedDay != today;

        int GrowthDay(int day) => Mathf.Clamp(day, 1, growthDays);

        public float IncomeSecondsOn(int day) => incomeSeconds + incomeSecondsPerDay * (GrowthDay(day) - 1);

        /// <summary>The cash the day-<paramref name="day"/> chest pays a player earning <paramref name="incomePerSecond"/>.</summary>
        public long Cash(double incomePerSecond, int day) {
            double cash = double.IsNaN(incomePerSecond) || incomePerSecond <= 0d ? 0d : incomePerSecond * IncomeSecondsOn(day);
            return Math.Max(minimumCash, (long)Math.Min(long.MaxValue / 4d, Math.Floor(cash)));
        }

        public int Xp(int day) => xp + xpPerDay * (GrowthDay(day) - 1);

        /// <summary>Perk tokens in the day-<paramref name="day"/> chest (the streak's day, from 1).</summary>
        public int Tokens(int day) {
            int inWeek = (Math.Max(1, day) - 1) % week + 1;
            if (inWeek == week) return weekTokens;
            return inWeek == midWeekDay ? midWeekTokens : 0;
        }

        /// <summary>Minutes of income the day-<paramref name="day"/> chest pays (rounded, for reading).</summary>
        public int Minutes(int day) => Mathf.RoundToInt(IncomeSecondsOn(day) / 60f);

        /// <summary>"10 min of income, +1 perk token": the prize, for the chest's sign and the toasts.</summary>
        public string Prize(int day) => PrizeText(Minutes(day), Tokens(day));

        /// <summary>The prize text from its parts (clients get the parts in the wallet).</summary>
        public static string PrizeText(int minutes, int tokens) {
            string cash = $"{Math.Max(0, minutes)} min of best income";
            return tokens > 0 ? $"{cash}, +{tokens} perk token{(tokens == 1 ? "" : "s")}" : cash;
        }

        /// <summary>The next streak day that pays perk tokens, from <paramref name="day"/> on.</summary>
        public int NextTokenDay(int day) {
            for (int d = Math.Max(1, day); d < day + week + 1; d++) if (Tokens(d) > 0) return d;
            return -1;
        }
    }
}
