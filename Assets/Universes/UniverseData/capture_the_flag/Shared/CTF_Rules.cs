using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    public enum CTF_Team : byte { Red, Blue }
    public enum CTF_FlagState : byte { Home, Carried, Dropped }
    /// <summary>What a mid-lane power-up gives. Stored in the map scene, so only ever append.</summary>
    public enum CTF_PowerUpKind : byte { Health, Speed }
    /// <summary>How someone died, for the kill feed icon. Only ever append.</summary>
    public enum CTF_KillCause : byte { Other, Gun, Melee, Fall }

    /// <summary>Numbers and pure rules both sides agree on.</summary>
    public static class CTF_Rules {
        public const int CapturesToWin = 3;
        public const float PickupRange = 2.6f;
        public const float CaptureRange = 4f;
        public const float DropReturnSeconds = 20f;
        public const float CarrierSpeedMultiplier = 0.85f;
        /// <summary>How close a character must be to take a power-up.</summary>
        public const float PowerUpRange = 3f;
        /// <summary>Power-up availability travels as one bit each in a <see cref="uint"/>.</summary>
        public const int MaxPowerUps = 32;
        /// <summary>Height difference that still counts as "the same floor" for reach checks.</summary>
        public const float ReachHeight = 3f;

        public static CTF_Team Other(CTF_Team team) => team == CTF_Team.Red ? CTF_Team.Blue : CTF_Team.Red;

        public static string Name(CTF_Team team) => team == CTF_Team.Red ? "RED" : "BLUE";
        public static string Hex(CTF_Team team) => team == CTF_Team.Red ? "#FF5A4E" : "#4EA6FF";
        public static Color Color(CTF_Team team) => team == CTF_Team.Red ? new Color(1f, 0.32f, 0.27f) : new Color(0.28f, 0.6f, 1f);

        /// <summary>Joiners go to the smaller team; ties go to whichever team is losing, then red.</summary>
        public static CTF_Team PickTeam(int red, int blue, int redScore, int blueScore) {
            if (red != blue) return red < blue ? CTF_Team.Red : CTF_Team.Blue;
            if (redScore != blueScore) return redScore < blueScore ? CTF_Team.Red : CTF_Team.Blue;
            return CTF_Team.Red;
        }

        /// <summary>Within reach on the ground plane, ignoring small height differences (stairs, jumping).</summary>
        public static bool InReach(Vector3 a, Vector3 b, float range) {
            if (Mathf.Abs(a.y - b.y) > ReachHeight) return false;
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude <= range * range;
        }

        /// <summary>Who won when the clock runs out, or null for a draw.</summary>
        public static CTF_Team? Winner(int redScore, int blueScore) =>
            redScore == blueScore ? null : redScore > blueScore ? CTF_Team.Red : CTF_Team.Blue;

        /// <summary>Whether power-up <paramref name="index"/> is ready in an availability mask.</summary>
        public static bool IsReady(uint mask, int index) => index >= 0 && index < MaxPowerUps && (mask & (1u << index)) != 0;

        /// <summary>The mask with power-up <paramref name="index"/> marked ready or taken.</summary>
        public static uint WithReady(uint mask, int index, bool ready) {
            if (index < 0 || index >= MaxPowerUps) throw new ArgumentOutOfRangeException(nameof(index), index, $"Power-ups are limited to {MaxPowerUps}.");
            return ready ? mask | (1u << index) : mask & ~(1u << index);
        }

        /// <summary>Walk/sprint multiplier: carrying a flag slows you, a speed boost stacks on top.</summary>
        public static float SpeedMultiplier(bool carrying, bool boosted, float boost) =>
            (carrying ? CarrierSpeedMultiplier : 1f) * (boosted ? boost : 1f);

        /// <summary>
        /// Whether state <paramref name="sequence"/> is newer than <paramref name="last"/>. Counts wrap
        /// around; a jump far back (a restarted server) also counts as newer, so a client never gets stuck.
        /// </summary>
        public static bool IsNewer(uint sequence, uint last) {
            int ahead = unchecked((int)(sequence - last));
            return ahead > 0 || ahead < -SequenceRestartGap;
        }

        /// <summary>
        /// Maps health to the value the shared low-health heartbeat takes (it beats at
        /// <paramref name="kitThreshold"/> and below, 1 = off). With two-hit kills the only living values
        /// are full and one hit left, so "one hit left" (<paramref name="lowAt"/> and below) is mapped into
        /// the beating range, at <paramref name="signalAtLowAt"/> and falling toward 0.
        /// </summary>
        public static float LowHealthSignal(float health01, float lowAt, float signalAtLowAt) {
            if (health01 <= 0f || health01 > lowAt || lowAt <= 0f) return 1f;
            return signalAtLowAt * (health01 / lowAt);
        }

        /// <summary>A state this far behind the last one comes from a restarted server, not a late packet.</summary>
        public const int SequenceRestartGap = 1000;

        /// <summary>How much a health power-up actually restores (never above max; 0 means "don't take it").</summary>
        public static long HealAmount(long health, long maxHealth, long heal) =>
            health <= 0 ? 0 : Math.Max(0L, Math.Min(heal, maxHealth - health));
    }
}
