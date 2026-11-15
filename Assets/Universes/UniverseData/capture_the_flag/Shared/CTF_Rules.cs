using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    public enum CTF_Team : byte { Red, Blue }
    public enum CTF_FlagState : byte { Home, Carried, Dropped }

    /// <summary>Numbers and pure rules both sides agree on.</summary>
    public static class CTF_Rules {
        public const int CapturesToWin = 3;
        public const float PickupRange = 2.6f;
        public const float CaptureRange = 4f;
        public const float DropReturnSeconds = 20f;
        public const float CarrierSpeedMultiplier = 0.85f;

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
            if (Mathf.Abs(a.y - b.y) > 3f) return false;
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude <= range * range;
        }

        /// <summary>Who won when the clock runs out, or null for a draw.</summary>
        public static CTF_Team? Winner(int redScore, int blueScore) =>
            redScore == blueScore ? null : redScore > blueScore ? CTF_Team.Red : CTF_Team.Blue;
    }
}
