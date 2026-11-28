using System;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>Closet door numbers (world units, seconds, points). Serialized on the server runner.</summary>
    [Serializable]
    public sealed class HS_DoorTuning {
        [Tooltip("How far from the shut leaf (horizontally) a player can reach a door, inside or outside.")]
        [Min(0.5f)] public float reach = HS_Doors.DefaultReach;
        [Tooltip("Height difference allowed between a player's feet and the door's floor, so nobody works a door through a ceiling.")]
        [Min(0.5f)] public float verticalReach = HS_Doors.DefaultVerticalReach;
        [Tooltip("A door will not shut on anyone standing closer than this to the shut leaf (about a robot's radius).")]
        [Min(0f)] public float doorwayClearance = 1.1f;
        [Tooltip("Seconds between two door uses by the same player, so nobody can strobe a door.")]
        [Min(0f)] public float useCooldown = 0.6f;
        [Tooltip("A seeker opened an empty closet a hider had shut: that hider's bluff worked.")]
        [Min(0)] public int bluffPoints = 12;
        [Tooltip("Bluffs one hider can be paid for in a round.")]
        [Min(1)] public int bluffsPerRound = 3;

        public void Validate() {
            reach = Mathf.Max(0.5f, reach);
            verticalReach = Mathf.Max(0.5f, verticalReach);
            doorwayClearance = Mathf.Max(0f, doorwayClearance);
            useCooldown = Mathf.Max(0f, useCooldown);
            bluffsPerRound = Mathf.Max(1, bluffsPerRound);
        }
    }

    /// <summary>
    /// Who may work a hiding spot's door, and when. Hiders open and shut doors while they hide and while
    /// the seekers hunt: shutting yourself in hides you, shutting an empty closet is a bluff. Seekers (and
    /// found hiders) can only fling doors open, and only once they are released. Between rounds anyone
    /// may play with them. Pure, so the server and the HUD's prompt agree and it can be tested.
    /// </summary>
    public static class HS_Doors {
        /// <summary>Default reach (world units): from inside a closet or a step outside it. The HUD's prompt offers doors within this.</summary>
        public const float DefaultReach = 4.2f;
        public const float DefaultVerticalReach = 3f;

        /// <summary>Why <paramref name="role"/> may not set a door to <paramref name="open"/> now, or null when they may.</summary>
        public static string Refusal(HS_Phase phase, HS_Role role, bool open) {
            switch (phase) {
                case HS_Phase.Hiding:
                    return role == HS_Role.Hider ? null : "Wait for the countdown.";
                case HS_Phase.Seeking:
                    if (role == HS_Role.Hider) return null;
                    if (role is HS_Role.Seeker or HS_Role.Caught) return open ? null : "Seekers can only open doors.";
                    return "Wait for the next round.";
                default:
                    return null;
            }
        }

        /// <summary>Whether <paramref name="feet"/> can reach the door whose shut leaf runs from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static bool InReach(Vector3 feet, Vector3 from, Vector3 to, float reach, float verticalReach) =>
            Mathf.Abs(feet.y - from.y) <= verticalReach && FlatDistanceToLeaf(feet, from, to) <= reach;

        /// <summary>Horizontal distance from <paramref name="point"/> to the shut leaf (a segment on the floor).</summary>
        public static float FlatDistanceToLeaf(Vector3 point, Vector3 from, Vector3 to) {
            Vector2 p = new(point.x, point.z), a = new(from.x, from.z), b = new(to.x, to.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// Whether someone standing at <paramref name="feet"/> is in the doorway, so the door must not shut
        /// on them: close to the leaf line and on the same floor.
        /// </summary>
        public static bool InDoorway(Vector3 feet, Vector3 from, Vector3 to, float clearance, float verticalReach) =>
            Mathf.Abs(feet.y - from.y) <= verticalReach && FlatDistanceToLeaf(feet, from, to) < clearance;

        /// <summary>
        /// Whether opening a door pays its closer a bluff: a seeker opened it during the hunt, nobody was
        /// inside, the closer is still hiding, and they have bluffs left this round.
        /// </summary>
        public static bool PaysBluff(bool openedBySeeker, bool anyoneInside, bool closerStillHiding, int bluffsPaid, int bluffsPerRound) =>
            openedBySeeker && !anyoneInside && closerStillHiding && bluffsPaid < bluffsPerRound;

        /// <summary>"CoatCloset" -> "Coat Closet": the builder's object names, readable.</summary>
        public static string Readable(string name) {
            if (string.IsNullOrEmpty(name)) return "";
            var text = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++) {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) text.Append(' ');
                text.Append(name[i]);
            }
            return text.ToString();
        }

        /// <summary>The door states as a bit mask (bit i set = door i shut), for the round state.</summary>
        public static ulong ClosedMask(HS_HideDoor[] doors) {
            ulong mask = 0;
            if (doors == null) return mask;
            for (int i = 0; i < doors.Length && i < 64; i++)
                if (doors[i] != null && !doors[i].IsOpen) mask |= 1UL << i;
            return mask;
        }

        public static bool IsClosed(ulong mask, int index) => index is >= 0 and < 64 && (mask & (1UL << index)) != 0;
    }
}
