using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Sizes of the course dressing. Authoring builds the meshes from the same values, so what the
    /// planner keeps clear is what gets drawn.
    /// </summary>
    [Serializable]
    public struct OB_DressingSettings {
        [Tooltip("Piece kinds that are dressed: rock underneath, trim, ropes. Checkpoint and finish pads also get arches, lamps and towers.")]
        public OB_PieceKind[] dressedKinds;
        [Tooltip("Pieces thicker than this are walls, not floors (m).")]
        [Min(0.1f)] public float maxThickness;
        [Tooltip("Narrowest floor that gets rock underneath (m).")]
        [Min(0f)] public float minFootprint;

        [Header("Rock under platforms")]
        [Tooltip("Rock depth per metre of footprint (square root of the floor area).")]
        [Min(0f)] public float keelDepthFactor;
        [Tooltip("Shallowest and deepest rock (m).")]
        public Vector2 keelDepth;
        [Tooltip("Shorter depths (fractions) tried when the full rock would reach into the course.")]
        public float[] keelFallbacks;
        [Tooltip("Height of the trim band along a platform's bottom edge, and how far it sticks out (m).")]
        [Min(0f)] public float trimHeight, trimOverhang;

        [Header("Towers under pads")]
        [Range(0f, 1f)] public float pillarChance;
        [Tooltip("Tower length below a pad (m); it fades into the haze.")]
        public Vector2 pillarLength;
        [Min(0.1f)] public float pillarWidth;
        [Tooltip("Height of the finial at a tower's foot (m).")]
        [Min(0f)] public float pillarCapHeight;

        [Header("Checkpoint arches")]
        [Tooltip("Clear width between the posts over a 6 m pad (m); wider pads scale the arch. Wide enough that the posts frame the way on from beside it.")]
        [Min(0f)] public float archInnerWidth;
        [Tooltip("Clear width of the narrower arch tried where the next or previous piece needs the room (m): the posts just clear the pad. 0 never narrows.")]
        [Min(0f)] public float archNarrowInnerWidth;
        [Tooltip("Clear height under the lintel (m): the 5.6 m robot plus a jump. High enough that from the chase camera on the pad the lintel stays above the view, clear of the HUD across its top.")]
        [Min(0f)] public float archHeight;
        [Min(0f)] public float archDepth, archPost;
        [Tooltip("How far the posts reach below the pad's top (m).")]
        [Min(0f)] public float archFoot;
        [Min(0f)] public float bannerWidth, bannerLength;
        [Tooltip("Height of the banners' rods above the pad (m): they hang beside the player, below the HUD across the top of the view.")]
        [Min(0f)] public float bannerHeight;

        [Header("Lamps")]
        [Min(0f)] public float lampHeight;
        [Tooltip("Distance of a lamp post out from the pad's side (m).")]
        [Min(0f)] public float lampOffset;
        [Min(0f)] public float lampFoot, lampWidth;

        [Header("Ropes and chains between platforms")]
        [Tooltip("Shortest and longest gap a rope spans (m).")]
        public Vector2 ropeGap;
        [Tooltip("Largest height difference a rope spans (m).")]
        [Min(0f)] public float ropeMaxRise;
        [Tooltip("Sag at the middle: a fixed part plus a share of the span (m, m/m).")]
        [Min(0f)] public float ropeSag, ropeSagPerMetre;
        [Tooltip("Ropes hang at this fraction of the narrower platform's half width from the centre line.")]
        [Range(0f, 1f)] public float ropeInset;
        [Min(0.01f)] public float ropeThickness;
        [Min(1)] public int ropeSegments;
        [Tooltip("Spacing of the pennants (or icicles, lanterns, shards) along a rope, and how far they hang (m).")]
        [Min(0.1f)] public float pennantSpacing;
        [Min(0f)] public float pennantLength;

        public static OB_DressingSettings Default => new() {
            dressedKinds = new[] { OB_PieceKind.Platform, OB_PieceKind.CheckpointPad, OB_PieceKind.Finish, OB_PieceKind.Conveyor, OB_PieceKind.Lava, OB_PieceKind.Beam, OB_PieceKind.Ice },
            maxThickness = 1.6f, minFootprint = 1.2f,
            keelDepthFactor = 0.9f, keelDepth = new Vector2(2f, 16f), keelFallbacks = new[] { 0.6f, 0.35f },
            trimHeight = 0.35f, trimOverhang = 0.2f,
            pillarChance = 0.6f, pillarLength = new Vector2(45f, 95f), pillarWidth = 2.4f, pillarCapHeight = 5f,
            archInnerWidth = 12f, archNarrowInnerWidth = 7.4f, archHeight = 11f, archDepth = 1.2f, archPost = 0.65f, archFoot = 2.5f,
            bannerWidth = 1.2f, bannerLength = 3.6f, bannerHeight = 6.2f,
            lampHeight = 7.5f, lampOffset = 0.8f, lampFoot = 2.5f, lampWidth = 1.4f,
            ropeGap = new Vector2(1.5f, 14f), ropeMaxRise = 5f, ropeSag = 0.6f, ropeSagPerMetre = 0.12f, ropeInset = 0.7f,
            ropeThickness = 0.18f, ropeSegments = 6, pennantSpacing = 1.6f, pennantLength = 1.1f
        };
    }

    /// <summary>
    /// Plans the dressing that makes the course look built rather than floating boxes: rock (or ice,
    /// basalt, crystal) under every floor with a trim band along its bottom edge, towers dropping from
    /// checkpoint pads into the haze, an arch with banners over each pad's exit (or entry) and lamps
    /// beside it, and ropes or chains (with pennants, icicles or lanterns) slung under the gaps between
    /// floors. Everything is in the style of the stage's tier and seeded by the course, and nothing is
    /// placed where it would reach into the space the course needs (<see cref="OB_CourseClearance"/>);
    /// only arches stand over the route on purpose, tall enough to run and jump through.
    /// </summary>
    public sealed class OB_DressingPlanner {
        const uint Stream = 0xD2E55u;
        /// <summary>Ropes attach this far above a platform's bottom face (m).</summary>
        const float RopeAttachLift = 0.15f;
        /// <summary>Banners hang this far in front of the arch posts (m).</summary>
        const float BannerStandOff = 0.06f;

        readonly List<OB_Placement> placements = new();
        readonly List<Bounds> reserved = new();
        readonly Vector3[] ropePoints = new Vector3[64];
        readonly List<Bounds> archParts = new();

        public IReadOnlyList<OB_Placement> Placements => placements;

        public void Plan(OB_CourseLayout layout, OB_CourseClearance clearance, in OB_DressingSettings s) {
            placements.Clear();
            reserved.Clear();
            if (layout == null) return;
            var random = new OB_EnvRandom(layout.seed, Stream);
            int previous = -1;
            for (int i = 0; i < layout.pieces.Count; i++) {
                OB_Piece piece = layout.pieces[i];
                if (!IsDressedFloor(piece, s)) continue;
                OB_Tier tier = TierOf(layout, piece.stage);
                PlaceKeel(i, piece, tier, clearance, s, ref random);
                if (piece.kind is OB_PieceKind.CheckpointPad or OB_PieceKind.Finish) PlacePadDressing(i, piece, tier, clearance, s, ref random);
                if (previous >= 0) PlaceRopes(previous, layout.pieces[previous], i, piece, tier, clearance, s);
                previous = i;
            }
            // Scenery placed after the dressing keeps off it too.
            foreach (var bounds in reserved) clearance.Reserve(bounds);
        }

        static OB_Tier TierOf(OB_CourseLayout layout, int stage) => layout.stages[Mathf.Clamp(stage, 0, layout.StageCount - 1)].tier;

        public static bool IsDressedFloor(in OB_Piece piece, in OB_DressingSettings s) {
            if (piece.size.y > s.maxThickness || Mathf.Min(piece.size.x, piece.size.z) < s.minFootprint) return false;
            if (s.dressedKinds == null) return false;
            foreach (var kind in s.dressedKinds)
                if (kind == piece.kind) return true;
            return false;
        }

        // ---------------------------------------------------------------- rock and trim
        void PlaceKeel(int index, in OB_Piece piece, OB_Tier tier, OB_CourseClearance clearance, in OB_DressingSettings s, ref OB_EnvRandom random) {
            var rotation = Quaternion.Euler(0f, piece.yaw, 0f);
            Vector3 bottom = piece.position - Vector3.up * (piece.size.y * 0.5f);
            float depth = Mathf.Clamp(Mathf.Sqrt(piece.size.x * piece.size.z) * s.keelDepthFactor, s.keelDepth.x, s.keelDepth.y) * random.Range(0.8f, 1.2f);
            int variant = random.Next(int.MaxValue);
            for (int attempt = -1; attempt < (s.keelFallbacks?.Length ?? 0); attempt++) {
                float d = attempt < 0 ? depth : Mathf.Max(s.keelDepth.x * 0.5f, depth * s.keelFallbacks[attempt]);
                Bounds bounds = RotatedBox(bottom - Vector3.up * (d * 0.5f), new Vector3(piece.size.x, d, piece.size.z), piece.yaw);
                if (clearance.Blocks(bounds, index, -1, false, bottom.y)) continue;
                Add(OB_DressingProp.Keel, variant, tier, Matrix4x4.TRS(bottom, rotation, new Vector3(piece.size.x, d, piece.size.z)), bounds);
                break;
            }
            var trimSize = new Vector3(piece.size.x + s.trimOverhang * 2f, s.trimHeight, piece.size.z + s.trimOverhang * 2f);
            Bounds trim = RotatedBox(bottom, trimSize, piece.yaw);
            if (!clearance.Blocks(trim, index, -1, false, bottom.y + s.trimHeight * 0.5f))
                Add(OB_DressingProp.Trim, 0, tier, Matrix4x4.TRS(bottom, rotation, trimSize), trim, false);
        }

        // ---------------------------------------------------------------- pads: arch, banners, lamps, tower
        void PlacePadDressing(int index, OB_Piece pad, OB_Tier tier, OB_CourseClearance clearance, in OB_DressingSettings s, ref OB_EnvRandom random) {
            var rotation = Quaternion.Euler(0f, pad.yaw, 0f);
            float top = pad.size.y * 0.5f, halfX = pad.size.x * 0.5f;
            Vector3 Local(float x, float y, float z) => pad.position + rotation * new Vector3(x, y, z);

            PlaceArch(index, pad, tier, clearance, s);

            // Lamps beside the middle of each side, clear of the arch posts and of the camera behind the player.
            for (int side = -1; side <= 1; side += 2) {
                Vector3 foot0 = Local(side * (halfX + s.lampOffset), top, 0f);
                Bounds lamp = new(foot0 + Vector3.up * ((s.lampHeight - s.lampFoot) * 0.5f), new Vector3(s.lampWidth, s.lampHeight + s.lampFoot, s.lampWidth));
                if (!clearance.Blocks(lamp, index)) Add(OB_DressingProp.Lamp, 0, tier, Matrix4x4.TRS(foot0, rotation, Vector3.one), lamp);
            }

            // A tower dropping from the pad into the haze, so the course reads as built up from below.
            if (!random.Chance(s.pillarChance)) return;
            Vector3 bottom = pad.position - Vector3.up * (pad.size.y * 0.5f);
            float length = random.Range(s.pillarLength);
            for (int attempt = 0; attempt < 2; attempt++, length *= 0.6f) {
                float total = length + s.pillarCapHeight;
                Bounds tower = new(bottom - Vector3.up * (total * 0.5f), new Vector3(s.pillarWidth, total, s.pillarWidth));
                if (clearance.Blocks(tower, index, -1, false, bottom.y)) continue;
                Add(OB_DressingProp.Pillar, 0, tier, Matrix4x4.TRS(bottom, rotation, new Vector3(s.pillarWidth, length, s.pillarWidth)), tower);
                Add(OB_DressingProp.PillarCap, 0, tier, Matrix4x4.TRS(bottom - Vector3.up * length, rotation, Vector3.one), tower, false);
                break;
            }
        }

        /// <summary>
        /// An arch over the pad's exit edge, framing the way on, with a banner on the pad side of each
        /// post. Where the next piece needs that room, a narrower arch (posts just clear of the pad) is
        /// tried there, then both over the entry edge, without banners; only a pad with no room at
        /// either edge goes without. The exit comes first because the chase camera rests behind the entry.
        /// </summary>
        void PlaceArch(int index, in OB_Piece pad, OB_Tier tier, OB_CourseClearance clearance, in OB_DressingSettings s) {
            float scale = pad.size.x / OB_CourseGenerator.PadSize;
            float wideSpan = s.archInnerWidth + s.archPost * 2f;
            bool canNarrow = s.archNarrowInnerWidth > 0f && s.archNarrowInnerWidth < s.archInnerWidth;
            for (int option = 0; option < 4; option++) {
                bool exit = option < 2, narrow = option % 2 == 1;
                if (narrow && !canNarrow) continue;
                // The narrow arch is the same mesh squeezed across, so its posts also come out thinner.
                float across = narrow ? (s.archNarrowInnerWidth + s.archPost * 2f) / wideSpan : 1f;
                // Arch space has +z pointing out of the pad over the edge it stands on.
                var facing = Quaternion.Euler(0f, pad.yaw + (exit ? 0f : 180f), 0f);
                Vector3 origin = pad.position + facing * new Vector3(0f, pad.size.y * 0.5f, pad.size.z * 0.5f);
                Matrix4x4 matrix = Matrix4x4.TRS(origin, facing, new Vector3(across, 1f, 1f) * scale);
                archParts.Clear();
                ArchParts(matrix, s, archParts);
                if (AnyBlocked(archParts, index, clearance)) continue;

                Bounds all = archParts[0];
                foreach (var part in archParts) {
                    all.Encapsulate(part);
                    reserved.Add(part);
                }
                Add(OB_DressingProp.Arch, 0, tier, matrix, all, false);
                // Over the entry the posts already stand near the resting camera; banners there would crowd it.
                if (!exit) return;
                float postX = (s.archInnerWidth * 0.5f + s.archPost * 0.5f) * across * scale;
                float inward = (s.archDepth * 0.5f) * scale + BannerStandOff;
                for (int side = -1; side <= 1; side += 2) {
                    Vector3 hang = origin + facing * new Vector3(side * postX, s.bannerHeight * scale, -inward);
                    Bounds banner = RotatedBox(hang - Vector3.up * (s.bannerLength * scale * 0.5f),
                        new Vector3(s.bannerWidth, s.bannerLength, s.bannerWidth) * scale, facing.eulerAngles.y);
                    // A banner reaches a little past the arch's own parts, so it is checked on its own.
                    if (clearance.Blocks(banner, index, -1, true, float.PositiveInfinity, true)) continue;
                    Add(OB_DressingProp.Banner, side < 0 ? 0 : 1, tier, Matrix4x4.TRS(hang, facing, Vector3.one * scale), banner, false);
                }
                return;
            }
        }

        static bool AnyBlocked(List<Bounds> parts, int pad, OB_CourseClearance clearance) {
            foreach (var part in parts)
                if (clearance.Blocks(part, pad, -1, true, float.PositiveInfinity, true)) return true;
            return false;
        }

        /// <summary>
        /// The world boxes an arch placed with <paramref name="matrix"/> fills: its two posts and its
        /// lintel in short pieces, so a turned arch is not judged by the much bigger box around it.
        /// </summary>
        public static void ArchParts(Matrix4x4 matrix, in OB_DressingSettings s, List<Bounds> parts) {
            Vector3 scale = matrix.lossyScale;
            float yaw = matrix.rotation.eulerAngles.y;
            void Part(Vector3 centre, Vector3 size) =>
                parts.Add(RotatedBox(matrix.MultiplyPoint3x4(centre), Vector3.Scale(size, scale), yaw));

            float postX = s.archInnerWidth * 0.5f + s.archPost * 0.5f, postHeight = s.archHeight + s.archPost + s.archFoot;
            for (int side = -1; side <= 1; side += 2)
                Part(new Vector3(side * postX, postHeight * 0.5f - s.archFoot, 0f), new Vector3(s.archPost, postHeight, s.archDepth));
            float span = s.archInnerWidth + s.archPost * 2f;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(span * scale.x / Mathf.Max(0.1f, s.archDepth * scale.z)));
            for (int i = 0; i < pieces; i++)
                Part(new Vector3(-span * 0.5f + span * (i + 0.5f) / pieces, s.archHeight + s.archPost * 0.5f, 0f), new Vector3(span / pieces, s.archPost, s.archDepth));
        }

        // ---------------------------------------------------------------- ropes
        /// <summary>Two ropes (or chains) slung under the gap between consecutive floors, with pennants hanging from them.</summary>
        void PlaceRopes(int indexA, in OB_Piece a, int indexB, in OB_Piece b, OB_Tier tier, OB_CourseClearance clearance, in OB_DressingSettings s) {
            Vector3 flat = b.position - a.position;
            flat.y = 0f;
            float centres = flat.magnitude;
            if (centres < 0.01f) return;
            Vector3 along = flat / centres;
            float reachA = Support(a, along), reachB = Support(b, -along);
            float gap = centres - reachA - reachB;
            float bottomA = a.position.y - a.size.y * 0.5f, bottomB = b.position.y - b.size.y * 0.5f;
            if (gap < s.ropeGap.x || gap > s.ropeGap.y || Mathf.Abs(bottomA - bottomB) > s.ropeMaxRise) return;

            Vector3 side = Vector3.Cross(Vector3.up, along);
            float offset = Mathf.Min(HalfWidth(a, side), HalfWidth(b, side)) * s.ropeInset;
            int segments = Mathf.Clamp(s.ropeSegments, 1, ropePoints.Length - 1);
            float sag = s.ropeSag + s.ropeSagPerMetre * gap;
            for (int lane = -1; lane <= 1; lane += 2) {
                Vector3 start = a.position + along * (reachA * 0.9f) + side * (lane * offset);
                Vector3 end = b.position - along * (reachB * 0.9f) + side * (lane * offset);
                start.y = bottomA + RopeAttachLift;
                end.y = bottomB + RopeAttachLift;
                for (int k = 0; k <= segments; k++) {
                    float t = k / (float)segments;
                    ropePoints[k] = Vector3.Lerp(start, end, t) - Vector3.up * (sag * 4f * t * (1f - t));
                }
                float hangsFrom = Mathf.Max(start.y, end.y);
                if (!RopeIsClear(segments, indexA, indexB, hangsFrom, clearance, s)) continue;
                for (int k = 0; k < segments; k++) {
                    Vector3 p = ropePoints[k], q = ropePoints[k + 1];
                    Vector3 span = q - p;
                    Add(OB_DressingProp.Rope, 0, tier, Matrix4x4.TRS(p, Quaternion.LookRotation(span), new Vector3(1f, 1f, span.magnitude)), SegmentBounds(p, q, s.ropeThickness), false);
                }
                PlacePennants(segments, indexA, indexB, hangsFrom, tier, along, clearance, s);
            }
        }

        bool RopeIsClear(int segments, int indexA, int indexB, float hangsFrom, OB_CourseClearance clearance, in OB_DressingSettings s) {
            for (int k = 0; k < segments; k++)
                if (clearance.Blocks(SegmentBounds(ropePoints[k], ropePoints[k + 1], s.ropeThickness), indexA, indexB, false, hangsFrom)) return false;
            return true;
        }

        void PlacePennants(int segments, int indexA, int indexB, float hangsFrom, OB_Tier tier, Vector3 along, OB_CourseClearance clearance, in OB_DressingSettings s) {
            float travelled = s.pennantSpacing * 0.5f;
            int count = 0;
            var facing = Quaternion.LookRotation(Vector3.Cross(Vector3.up, along));
            for (int k = 0; k < segments; k++) {
                Vector3 p = ropePoints[k], q = ropePoints[k + 1];
                float length = Vector3.Distance(p, q);
                for (; travelled < length; travelled += s.pennantSpacing) {
                    Vector3 at = Vector3.Lerp(p, q, travelled / length);
                    Bounds bounds = new(at - Vector3.up * (s.pennantLength * 0.5f), new Vector3(s.pennantLength, s.pennantLength, s.pennantLength));
                    if (!clearance.Blocks(bounds, indexA, indexB, false, hangsFrom))
                        Add(OB_DressingProp.Pennant, count, tier, Matrix4x4.TRS(at, facing, Vector3.one), bounds, false);
                    count++;
                }
                travelled -= length;
            }
        }

        // ---------------------------------------------------------------- helpers
        void Add(OB_DressingProp prop, int variant, OB_Tier tier, Matrix4x4 matrix, Bounds bounds, bool reserve = true) {
            placements.Add(new OB_Placement { kind = (byte)prop, variant = variant, tier = tier, matrix = matrix, bounds = bounds });
            if (reserve) reserved.Add(bounds);
        }

        /// <summary>How far a piece's box reaches from its centre along the horizontal unit vector <paramref name="direction"/>.</summary>
        static float Support(in OB_Piece piece, Vector3 direction) {
            float yaw = piece.yaw * Mathf.Deg2Rad;
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            return Mathf.Abs(Vector3.Dot(direction, right)) * piece.size.x * 0.5f + Mathf.Abs(Vector3.Dot(direction, forward)) * piece.size.z * 0.5f;
        }

        static float HalfWidth(in OB_Piece piece, Vector3 side) => Support(piece, side);

        static Bounds SegmentBounds(Vector3 p, Vector3 q, float thickness) {
            var bounds = new Bounds(p, Vector3.one * thickness);
            bounds.Encapsulate(new Bounds(q, Vector3.one * thickness));
            return bounds;
        }

        /// <summary>The world box around a box of <paramref name="size"/> centred on <paramref name="centre"/> and turned by <paramref name="yaw"/> degrees.</summary>
        public static Bounds RotatedBox(Vector3 centre, Vector3 size, float yaw) {
            float r = yaw * Mathf.Deg2Rad, cos = Mathf.Abs(Mathf.Cos(r)), sin = Mathf.Abs(Mathf.Sin(r));
            return new Bounds(centre, new Vector3(cos * size.x + sin * size.z, size.y, sin * size.x + cos * size.z));
        }
    }
}
