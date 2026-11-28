using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>A box turned about the vertical axis: what a course piece occupies, including how it moves.</summary>
    public readonly struct OB_Volume {
        public readonly Vector3 centre, size;
        public readonly float yaw;

        public OB_Volume(Vector3 centre, Vector3 size, float yaw) {
            this.centre = centre;
            this.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            this.yaw = yaw;
        }

        /// <summary>
        /// Whether the two volumes come closer than <paramref name="margin"/> (a negative margin lets them
        /// touch or overlap by that much). Separating-axis test in the horizontal plane plus the heights.
        /// </summary>
        public bool Overlaps(in OB_Volume other, float margin = 0f) {
            float heights = (size.y + other.size.y) * 0.5f + margin;
            if (Mathf.Abs(centre.y - other.centre.y) >= heights) return false;
            Vector2 delta = new(other.centre.x - centre.x, other.centre.z - centre.z);
            Vector2 a1 = Axis(yaw), a2 = Perpendicular(a1), b1 = Axis(other.yaw), b2 = Perpendicular(b1);
            Vector2 ha = new(size.x * 0.5f, size.z * 0.5f), hb = new(other.size.x * 0.5f, other.size.z * 0.5f);
            return Overlap(a1, delta, ha, a2, a1, hb, b2, b1, margin) && Overlap(a2, delta, ha, a2, a1, hb, b2, b1, margin)
                && Overlap(b1, delta, ha, a2, a1, hb, b2, b1, margin) && Overlap(b2, delta, ha, a2, a1, hb, b2, b1, margin);
        }

        /// <summary>Squared distance from <paramref name="point"/> to the volume (0 inside it).</summary>
        public float SqrDistance(Vector3 point) {
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * (point - centre);
            Vector3 half = size * 0.5f;
            var outside = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - half.x), Mathf.Max(0f, Mathf.Abs(local.y) - half.y),
                Mathf.Max(0f, Mathf.Abs(local.z) - half.z));
            return outside.sqrMagnitude;
        }

        /// <summary>The volume's local +Z (forward) in the horizontal plane, as (x, z).</summary>
        static Vector2 Axis(float degrees) {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>The volume's local +X (right) for a forward of <paramref name="forward"/>.</summary>
        static Vector2 Perpendicular(Vector2 forward) => new(forward.y, -forward.x);

        static bool Overlap(Vector2 axis, Vector2 delta, Vector2 ha, Vector2 aRight, Vector2 aForward, Vector2 hb, Vector2 bRight, Vector2 bForward, float margin) {
            float ra = ha.x * Mathf.Abs(Vector2.Dot(aRight, axis)) + ha.y * Mathf.Abs(Vector2.Dot(aForward, axis));
            float rb = hb.x * Mathf.Abs(Vector2.Dot(bRight, axis)) + hb.y * Mathf.Abs(Vector2.Dot(bForward, axis));
            return Mathf.Abs(Vector2.Dot(delta, axis)) < ra + rb + margin;
        }
    }

    /// <summary>
    /// The space course pieces take up, for the generator's pro routes, the tests and the environment's
    /// dressing (keep scenery out of these volumes and nothing blocks a jump or a swing). Moving pieces
    /// cover their whole travel; triggers and pickups (fields, flip pads, coins, stars, rising lava) take
    /// no space.
    /// </summary>
    public static class OB_PieceVolumes {
        /// <summary>Footprint of a spring, boost pad or cannon.</summary>
        static readonly Vector3 PadVolume = new(2.6f, 1.2f, 2.6f);
        static readonly Vector3 CannonVolume = new(3f, 3f, 4f);
        static readonly Vector3 SwitchPadVolume = new(2.2f, 0.5f, 2.2f);
        static readonly Vector3 SwitchBlockVolume = new(2.2f, 2.2f, 2.2f);
        /// <summary>Height of turntable posts above the disc (matches the turntable prefab).</summary>
        public const float TurntablePostHeight = 3f;

        /// <summary>The volume <paramref name="piece"/> occupies; false for pieces that take no space.</summary>
        public static bool TryGet(in OB_Piece piece, out OB_Volume volume) {
            Quaternion toLocal = Quaternion.Inverse(Quaternion.Euler(0f, piece.yaw, 0f));
            switch (piece.kind) {
                case OB_PieceKind.Platform: case OB_PieceKind.CheckpointPad: case OB_PieceKind.Finish: case OB_PieceKind.Lava:
                case OB_PieceKind.Conveyor: case OB_PieceKind.Crumble: case OB_PieceKind.Beat: case OB_PieceKind.WallJumpWall:
                case OB_PieceKind.Beam: case OB_PieceKind.OnOffBlock: case OB_PieceKind.Laser:
                case OB_PieceKind.RollingLog: case OB_PieceKind.Ice: case OB_PieceKind.Ghost: case OB_PieceKind.Honey: case OB_PieceKind.Plank:
                    volume = new OB_Volume(piece.position, piece.size, piece.yaw);
                    return true;
                case OB_PieceKind.Mover: {
                    Vector3 travel = toLocal * piece.vector;
                    var abs = new Vector3(Mathf.Abs(travel.x), Mathf.Abs(travel.y), Mathf.Abs(travel.z));
                    volume = new OB_Volume(piece.position + piece.vector * 0.5f, piece.size + abs, piece.yaw);
                    return true;
                }
                case OB_PieceKind.SpinnerBar: case OB_PieceKind.SpinnerCross:
                    volume = new OB_Volume(piece.position, new Vector3(piece.size.x, 1f, piece.size.x), piece.yaw);
                    return true;
                case OB_PieceKind.Spring: case OB_PieceKind.Boost:
                    volume = new OB_Volume(piece.position + Vector3.up * (PadVolume.y * 0.5f), PadVolume, piece.yaw);
                    return true;
                case OB_PieceKind.Cannon:
                    volume = new OB_Volume(piece.position + Vector3.up * (CannonVolume.y * 0.5f), CannonVolume, piece.yaw);
                    return true;
                case OB_PieceKind.Mushroom:
                    volume = new OB_Volume(piece.position - Vector3.up * 0.5f, new Vector3(piece.size.x, 1f, piece.size.x), piece.yaw);
                    return true;
                case OB_PieceKind.OnOffSwitch:
                    volume = piece.group == 0
                        ? new OB_Volume(piece.position + Vector3.up * (SwitchPadVolume.y * 0.5f), SwitchPadVolume, piece.yaw)
                        : new OB_Volume(piece.position, SwitchBlockVolume, piece.yaw);
                    return true;
                case OB_PieceKind.PowerBlock:
                    volume = new OB_Volume(piece.position, piece.size, piece.yaw);
                    return true;
                case OB_PieceKind.Pendulum: {
                    // Pivot at the top; the head sweeps sideways through the whole swing.
                    float length = piece.size.y, reach = length * Mathf.Sin(Mathf.Min(90f, piece.vector.x) * Mathf.Deg2Rad);
                    float height = length + piece.size.x * 0.5f;
                    volume = new OB_Volume(piece.position + Vector3.down * (height * 0.5f),
                        new Vector3(2f * reach + piece.size.x, height, piece.size.z), piece.yaw);
                    return true;
                }
                case OB_PieceKind.Crusher:
                    volume = new OB_Volume(piece.position + Vector3.down * (piece.vector.y * 0.5f),
                        piece.size + Vector3.up * piece.vector.y, piece.yaw);
                    return true;
                case OB_PieceKind.Swing: {
                    // Pivot above, platform hanging a rope's length below; it sweeps forward and back through the swing.
                    float rope = piece.size.y, reach = rope * Mathf.Sin(Mathf.Min(90f, piece.vector.x) * Mathf.Deg2Rad);
                    float height = rope + piece.vector.y;
                    volume = new OB_Volume(piece.position + Vector3.down * (height * 0.5f),
                        new Vector3(piece.size.x, height, 2f * reach + piece.size.z), piece.yaw);
                    return true;
                }
                case OB_PieceKind.Turntable:
                    volume = new OB_Volume(piece.position + Vector3.up * ((TurntablePostHeight - piece.size.y) * 0.5f),
                        new Vector3(piece.size.x, TurntablePostHeight + piece.size.y, piece.size.z), piece.yaw);
                    return true;
                default:
                    volume = default;
                    return false;
            }
        }
    }
}
