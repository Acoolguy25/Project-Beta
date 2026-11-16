using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Medal targets for a course, as seconds per stage so they scale with the course length.
    /// A finish earns the best medal whose target its scored time (after the coin bonus) beats.
    /// Defaults come from the generated courses: about 44 m between checkpoints, run at 10-15 m/s, so
    /// a clean run with obstacle waits takes about 7-9 s a stage (gold at 10 s), and a run with a few
    /// falls (5-10 s each) takes about twice that.
    /// </summary>
    [Serializable]
    public struct OB_MedalTimes {
        [Tooltip("Scored seconds per stage for a gold medal.")]
        [Min(1f)] public float goldPerStage;
        [Tooltip("Scored seconds per stage for a silver medal.")]
        [Min(1f)] public float silverPerStage;
        [Tooltip("Scored seconds per stage for a bronze medal.")]
        [Min(1f)] public float bronzePerStage;

        public static OB_MedalTimes Default => new() { goldPerStage = 10f, silverPerStage = 15f, bronzePerStage = 22f };

        /// <summary>The scored time to beat for <paramref name="medal"/> on a course of <paramref name="stageCount"/> stages.</summary>
        public float Target(OB_Medal medal, int stageCount) => Mathf.Max(1, stageCount) * medal switch {
            OB_Medal.Gold => goldPerStage,
            OB_Medal.Silver => silverPerStage,
            OB_Medal.Bronze => bronzePerStage,
            _ => float.PositiveInfinity
        };

        /// <summary>The medal a finish with <paramref name="scoredTime"/> earns.</summary>
        public OB_Medal Award(float scoredTime, int stageCount) {
            for (OB_Medal medal = OB_Medal.Gold; medal > OB_Medal.None; medal--)
                if (scoredTime <= Target(medal, stageCount)) return medal;
            return OB_Medal.None;
        }
    }

    /// <summary>Race scoring rules shared by the server (which decides) and clients (which present).</summary>
    public static class OB_Scoring {
        /// <summary>Finish time with <paramref name="secondsPerCoin"/> taken off for every coin, never below zero.</summary>
        public static float ScoredTime(float finishTime, int coins, float secondsPerCoin) =>
            Mathf.Max(0f, finishTime - Mathf.Max(0, coins) * Mathf.Max(0f, secondsPerCoin));

        /// <summary>
        /// Whether <paramref name="a"/> is strictly ahead of <paramref name="b"/>: finishers beat
        /// everyone still racing (faster scored time first), otherwise the further checkpoint wins.
        /// Racers on the same checkpoint are level.
        /// </summary>
        public static bool IsAhead(in OB_Standing a, in OB_Standing b) {
            if (a.finished != b.finished) return a.finished;
            return a.finished ? a.scoredTime < b.scoredTime : a.stage > b.stage;
        }

        /// <summary>Ordering for result tables: leaders first, level racers by name so the order is stable.</summary>
        public static int Compare(OB_Standing a, OB_Standing b) {
            if (IsAhead(a, b)) return -1;
            if (IsAhead(b, a)) return 1;
            return string.CompareOrdinal(a.name, b.name);
        }

        /// <summary>1-based place of the racer with <paramref name="clientId"/>: one more than the racers strictly ahead. 0 if absent.</summary>
        public static int PlaceOf(OB_Standing[] racers, int clientId) {
            if (racers == null) return 0;
            int index = IndexOf(racers, clientId);
            if (index < 0) return 0;
            int ahead = 0;
            foreach (var other in racers)
                if (IsAhead(other, racers[index])) ahead++;
            return ahead + 1;
        }

        public static int IndexOf(OB_Standing[] racers, int clientId) {
            if (racers == null) return -1;
            for (int i = 0; i < racers.Length; i++)
                if (racers[i].clientId == clientId) return i;
            return -1;
        }

        /// <summary>Number of collected coins in a per-stage coin bit mask.</summary>
        public static int CountCoins(ulong mask) {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }

        /// <summary>"1st", "2nd", "3rd", "4th", ..., "11th", "21st".</summary>
        public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        /// <summary>A race clock: "m:ss", or "m:ss.t" with <paramref name="tenths"/>.</summary>
        public static string Clock(float seconds, bool tenths = false) {
            seconds = Mathf.Max(0f, seconds);
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            if (tenths && rest >= 59.95f) { minutes++; rest = 0f; }
            return tenths ? $"{minutes}:{rest:00.0}" : $"{minutes}:{(int)rest:00}";
        }
    }
}
