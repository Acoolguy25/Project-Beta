using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// How much room the server allows around the course (serialized on the server runner). Every value
    /// is a margin around the generator's own piece volumes (<see cref="OB_PieceVolumes"/>), so the
    /// envelope follows the course whatever shape it takes.
    /// </summary>
    [Serializable]
    public struct OB_EnvelopeRules {
        [Tooltip("Room around every piece, sideways and up and down, for a player in the air or standing at its edge (the character's middle is judged, about half its height off the feet).")]
        [Min(1f)] public float margin;
        [Tooltip("Extra room around springs, boost pads, cannons, mushrooms and moving platforms, whose flights reach further than a jump.")]
        [Min(0f)] public float launchMargin;
        [Tooltip("Extra room above those launchers for the climb of their arc.")]
        [Min(0f)] public float launchLift;
        [Tooltip("Radius of the lobby around the first spawn, which the generator's pieces do not cover.")]
        [Min(1f)] public float lobbyRadius;

        public static OB_EnvelopeRules Default => new() { margin = 10f, launchMargin = 22f, launchLift = 24f, lobbyRadius = 24f };
    }

    /// <summary>
    /// The space a racer may be in, stage by stage: the union of every piece of the stages they have reached
    /// (and the next checkpoint pad, which they jump to), each inflated by <see cref="OB_EnvelopeRules"/>,
    /// minus the guard volumes (<see cref="OB_PieceKind.OutOfBounds"/>) the generator puts where a wall or
    /// ceiling is solid on its far side. The server sends back anyone whose middle is outside it, so a
    /// gravity wall's other side, the top of a ceiling, or a shortcut past stages cannot be used.
    /// </summary>
    public sealed class OB_StageEnvelope {
        readonly struct Zone {
            public readonly OB_Volume volume;
            public readonly int stage;
            /// <summary>Only counts for the stage before it (a checkpoint pad or the finish, jumped to from the stage before).</summary>
            public readonly bool entry;

            public Zone(OB_Volume volume, int stage, bool entry) {
                this.volume = volume;
                this.stage = stage;
                this.entry = entry;
            }
        }

        readonly List<Zone> zones = new();
        readonly List<OB_Volume> guards = new();
        readonly OB_Volume lobby;
        readonly int stageCount;

        public int ZoneCount => zones.Count;
        public int GuardCount => guards.Count;
        public IReadOnlyList<OB_Volume> Guards => guards;

        public OB_StageEnvelope(OB_CourseLayout layout, OB_EnvelopeRules rules) {
            stageCount = layout.StageCount;
            lobby = new OB_Volume(layout.stages[0].spawn, Vector3.one * (rules.lobbyRadius * 2f), 0f);
            foreach (OB_Piece piece in layout.pieces) {
                if (piece.kind == OB_PieceKind.OutOfBounds) {
                    guards.Add(new OB_Volume(piece.position, piece.size, piece.yaw));
                    continue;
                }
                if (!TryGetSpace(piece, out OB_Volume volume, out bool launcher)) continue;
                volume = Inflate(volume, launcher ? rules.margin + rules.launchMargin : rules.margin, launcher ? rules.launchLift : 0f);
                bool entry = piece.kind is OB_PieceKind.CheckpointPad or OB_PieceKind.Finish;
                zones.Add(new Zone(volume, piece.stage, entry));
            }
        }

        /// <summary>The space a piece takes, including the trigger fields (gravity, wind, flip pads) and pickups.</summary>
        static bool TryGetSpace(in OB_Piece piece, out OB_Volume volume, out bool launcher) {
            launcher = piece.kind is OB_PieceKind.Spring or OB_PieceKind.Boost or OB_PieceKind.Cannon or OB_PieceKind.Mushroom
                or OB_PieceKind.Mover or OB_PieceKind.Ring or OB_PieceKind.Swing;
            if (OB_PieceVolumes.TryGet(piece, out volume)) return true;
            switch (piece.kind) {
                case OB_PieceKind.GravityZone: case OB_PieceKind.Wind: case OB_PieceKind.GravityFlip: case OB_PieceKind.Ring:
                    volume = new OB_Volume(piece.position, piece.size, piece.yaw);
                    return true;
                case OB_PieceKind.RisingLava:
                    // The surface starts at the top of the box and climbs vector.y.
                    volume = new OB_Volume(piece.position + Vector3.up * (piece.vector.y * 0.5f), piece.size + Vector3.up * piece.vector.y, piece.yaw);
                    return true;
                case OB_PieceKind.Coin: case OB_PieceKind.Star:
                    volume = new OB_Volume(piece.position, Vector3.one, piece.yaw);
                    return true;
                default:
                    return false;
            }
        }

        static OB_Volume Inflate(in OB_Volume v, float margin, float lift) =>
            new(v.centre + Vector3.up * (lift * 0.5f), v.size + new Vector3(2f * margin, 2f * margin + lift, 2f * margin), v.yaw);

        /// <summary>Whether a racer whose furthest checkpoint is <paramref name="reached"/> may have their middle at <paramref name="centre"/>.</summary>
        public bool Allows(int reached, Vector3 centre) => !IsGuarded(centre) && IsInside(reached, centre);

        /// <summary>Whether <paramref name="centre"/> is inside a guard volume (the far side of a wall or ceiling).</summary>
        public bool IsGuarded(Vector3 centre) {
            foreach (OB_Volume guard in guards)
                if (guard.SqrDistance(centre) <= 0f) return true;
            return false;
        }

        /// <summary>Whether <paramref name="centre"/> is within the inflated space of the stages reached (or the next checkpoint pad), ignoring guards.</summary>
        public bool IsInside(int reached, Vector3 centre) {
            int own = Mathf.Clamp(reached, 0, stageCount - 1);
            if (reached <= 0 && lobby.SqrDistance(centre) <= 0f) return true;
            foreach (Zone zone in zones) {
                bool counts = zone.entry ? zone.stage <= own + 1 : zone.stage <= own;
                if (counts && zone.volume.SqrDistance(centre) <= 0f) return true;
            }
            return false;
        }
    }
}
