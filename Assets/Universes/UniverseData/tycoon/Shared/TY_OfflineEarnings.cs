using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// "While you were away": a returning player is paid a share of their income for the time they were
    /// gone, up to a cap, so leaving and coming back feels rewarding. Two level unlocks raise the cap and
    /// the share (Night Shift, Overtime). Serialized on the server runner; the maths is shared and tested.
    /// </summary>
    [Serializable]
    public sealed class TY_OfflineEarnings {
        [Tooltip("Share (0..1) of the player's income paid for each second away.")]
        [Range(0f, 1f)] public float share = 0.25f;
        [Tooltip("Most hours away that are paid.")]
        [Min(0f)] public float capHours = 2f;
        [Tooltip("Shorter absences (a reconnect, a server hop) pay nothing.")]
        [Min(0f)] public float minAwayMinutes = 3f;

        [Header("Level unlocks")]
        [Tooltip("Cap in hours with the Night Shift unlock.")]
        [Min(0f)] public float nightShiftCapHours = 4f;
        [Tooltip("Share with the Overtime unlock.")]
        [Range(0f, 1f)] public float overtimeShare = 0.5f;
        [Tooltip("Cap in hours with the Overtime unlock (the larger of this and Night Shift's applies).")]
        [Min(0f)] public float overtimeCapHours = 8f;

        const double SecondsPerHour = 3600d;

        public void Validate() {
            share = Mathf.Clamp01(share);
            overtimeShare = Mathf.Clamp01(overtimeShare);
            capHours = Mathf.Max(0f, capHours);
            nightShiftCapHours = Mathf.Max(capHours, nightShiftCapHours);
            overtimeCapHours = Mathf.Max(capHours, overtimeCapHours);
            minAwayMinutes = Mathf.Max(0f, minAwayMinutes);
        }

        /// <summary>Most seconds paid for one absence.</summary>
        public double CapSeconds(bool nightShift, bool overtime) {
            float hours = capHours;
            if (nightShift) hours = Mathf.Max(hours, nightShiftCapHours);
            if (overtime) hours = Mathf.Max(hours, overtimeCapHours);
            return hours * SecondsPerHour;
        }

        public float Share(bool overtime) => overtime ? Mathf.Max(share, overtimeShare) : share;

        /// <summary>Seconds of <paramref name="awaySeconds"/> that are paid: 0 below the minimum, then up to the cap.</summary>
        public double PaidSeconds(double awaySeconds, bool nightShift, bool overtime) =>
            double.IsNaN(awaySeconds) || awaySeconds < minAwayMinutes * 60d ? 0d : Math.Min(awaySeconds, CapSeconds(nightShift, overtime));

        /// <summary>Cash a player earning <paramref name="incomePerSecond"/> is paid for <paramref name="awaySeconds"/> away.</summary>
        public long Payout(double incomePerSecond, double awaySeconds, bool nightShift, bool overtime) {
            if (double.IsNaN(incomePerSecond) || incomePerSecond <= 0d) return 0;
            double cash = incomePerSecond * Share(overtime) * PaidSeconds(awaySeconds, nightShift, overtime);
            return (long)Math.Min(long.MaxValue / 4d, Math.Floor(cash));
        }

        /// <summary>
        /// The welcome-back card's second line: the share paid and, for a long absence, the cap and how to raise
        /// it. The card's first line already says how long the player was away, so this never repeats it.
        /// </summary>
        public string Describe(double awaySeconds, bool nightShift, bool overtime) {
            string text = $"Your factory ran at {Share(overtime) * 100f:0}% while you were away.";
            double paid = PaidSeconds(awaySeconds, nightShift, overtime);
            if (paid <= 0d || paid >= awaySeconds) return text;
            text += $" Pay stops after {Duration(paid)}";
            if (overtime) return text + ".";
            return text + (nightShift
                ? $": the Overtime level unlock raises it to {Num(overtimeCapHours)} hours."
                : $": the Night Shift level unlock raises it to {Num(nightShiftCapHours)} hours.");
        }

        static string Num(float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Seconds between the last save and now (0 for a first visit or a clock that went back).</summary>
        public static double AwaySeconds(long lastSeenUnixSeconds, long nowUnixSeconds) =>
            lastSeenUnixSeconds <= 0 ? 0d : Math.Max(0L, nowUnixSeconds - lastSeenUnixSeconds);

        /// <summary>"2h 05m", "14m", "45s".</summary>
        public static string Duration(double seconds) {
            long s = (long)Math.Max(0d, seconds);
            if (s >= 3600) return $"{s / 3600}h {s % 3600 / 60:00}m";
            return s >= 60 ? $"{s / 60}m" : $"{s}s";
        }
    }
}
