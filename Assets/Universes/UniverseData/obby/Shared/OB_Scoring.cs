using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Medal targets for a course, as seconds per stage so they scale with the course length.
    /// A finish earns the best medal whose target its scored time (after the coin bonus) beats.
    /// Defaults are measured: a no-skip run of stages 1-10 (Basic and Easy tiers) took 76 s, 7.6 s a
    /// stage, and clean single stages in the Medium to Extreme tiers took 3-12 s (7.8 s a stage over
    /// 16 clean stages). Gold at 9 s leaves a clean run a small margin; the same runs with their
    /// knockbacks and retries counted averaged 13.4 s, between silver (13 s) and bronze (18 s).
    /// </summary>
    [Serializable]
    public struct OB_MedalTimes {
        [Tooltip("Scored seconds per stage for a gold medal.")]
        [Min(1f)] public float goldPerStage;
        [Tooltip("Scored seconds per stage for a silver medal.")]
        [Min(1f)] public float silverPerStage;
        [Tooltip("Scored seconds per stage for a bronze medal.")]
        [Min(1f)] public float bronzePerStage;

        public static OB_MedalTimes Default => new() { goldPerStage = 9f, silverPerStage = 13f, bronzePerStage = 18f };

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
            if (a.finished) return a.scoredTime < b.scoredTime;
            // On the same checkpoint, whoever got there first is ahead; racers still on the start are level.
            return a.stage != b.stage ? a.stage > b.stage : a.reachedAt < b.reachedAt;
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

        /// <summary>Fills <paramref name="into"/> with the ids of every racer strictly ahead of <paramref name="clientId"/>.</summary>
        public static void AheadOf(OB_Standing[] racers, int clientId, HashSet<int> into) {
            into.Clear();
            int index = IndexOf(racers, clientId);
            if (index < 0) return;
            foreach (var racer in racers)
                if (IsAhead(racer, racers[index])) into.Add(racer.clientId);
        }

        /// <summary>
        /// Who changed places with you between two standings updates. <paramref name="gained"/> counts racers
        /// that were ahead and are not any more (and are still racing: someone leaving is no pass);
        /// <paramref name="lost"/> counts racers ahead now that were not before.
        /// </summary>
        public static void Overtakes(HashSet<int> aheadBefore, HashSet<int> aheadNow, OB_Standing[] racers, out int gained, out int lost) {
            gained = lost = 0;
            foreach (int id in aheadBefore)
                if (!aheadNow.Contains(id) && IndexOf(racers, id) >= 0) gained++;
            foreach (int id in aheadNow)
                if (!aheadBefore.Contains(id)) lost++;
        }

        /// <summary>
        /// Race time (scored, so coins count) minus the gold medal pace after <paramref name="stagesDone"/>
        /// stages: negative means ahead of gold.
        /// </summary>
        public static float GoldPaceDelta(float raceTime, int coins, float secondsPerCoin, int stagesDone, float goldPerStage) =>
            ScoredTime(raceTime, coins, secondsPerCoin) - Mathf.Max(0, stagesDone) * goldPerStage;

        /// <summary>Number of collected coins in a per-stage coin bit mask.</summary>
        public static int CountCoins(ulong mask) {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }

        /// <summary>"1st", "2nd", "3rd", "4th", ..., "11th", "21st".</summary>
        public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        /// <summary>
        /// Writes <see cref="Clock(float, bool)"/> with tenths ("m:ss.t") into <paramref name="buffer"/> without
        /// allocating, for a clock redrawn many times a second. Returns the number of characters written.
        /// </summary>
        public static int ClockTenths(int totalTenths, char[] buffer) {
            totalTenths = Mathf.Max(0, totalTenths);
            int minutes = totalTenths / 600, rest = totalTenths % 600;
            int length = 0;
            if (minutes >= 10) length = WriteDigits(minutes, buffer);
            else buffer[length++] = (char)('0' + minutes);
            buffer[length++] = ':';
            buffer[length++] = (char)('0' + rest / 100);
            buffer[length++] = (char)('0' + rest / 10 % 10);
            buffer[length++] = '.';
            buffer[length++] = (char)('0' + rest % 10);
            return length;
        }

        static int WriteDigits(int value, char[] buffer) {
            int digits = 0;
            for (int v = value; v > 0; v /= 10) digits++;
            for (int i = digits - 1; i >= 0; i--, value /= 10) buffer[i] = (char)('0' + value % 10);
            return digits;
        }

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
