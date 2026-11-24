using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>How much room scenery and dressing leave around the course.</summary>
    [Serializable]
    public struct OB_ClearanceSettings {
        [Tooltip("Space kept free above every course piece (m): the character's height, a jump and the chase camera's lift.")]
        [Min(0f)] public float headroom;
        [Tooltip("Free space beside and under every piece (m).")]
        [Min(0f)] public float margin;
        [Tooltip("Box used for pieces that carry no size of their own (springs, boost pads, coins).")]
        public Vector3 defaultPieceSize;
        [Tooltip("Radius of the free column along the route between consecutive surfaces (m).")]
        [Min(0f)] public float routeRadius;
        [Tooltip("Spacing of the route columns (m).")]
        [Min(0.25f)] public float routeStep;
        [Tooltip("Extra room around gravity fields that turn 'down' (m): the player and the camera turn onto walls and ceilings there.")]
        [Min(0f)] public float turnedGravityMargin;
        [Tooltip("Piece kinds that also get the wide margin (new gravity or camera-turning mechanics). Gravity fields with a direction always do.")]
        public OB_PieceKind[] wideKinds;
        [Tooltip("Free box around the lobby spawn (m).")]
        public Vector3 lobbySize;
        [Tooltip("Spatial hash cell (m). Bigger cells mean fewer lists but more boxes per query.")]
        [Min(4f)] public float cellSize;

        public static OB_ClearanceSettings Default => new() {
            headroom = 12f, margin = 1.2f, defaultPieceSize = new Vector3(3f, 2f, 3f),
            routeRadius = 3f, routeStep = 1.5f, turnedGravityMargin = 18f, wideKinds = Array.Empty<OB_PieceKind>(),
            lobbySize = new Vector3(26f, 8f, 26f), cellSize = 32f
        };
    }

    /// <summary>
    /// The space scenery must leave free: a box around every course piece (with headroom above it for the
    /// player and camera), a column along the route between consecutive surfaces, and a wide margin
    /// around gravity fields that turn the player onto walls and ceilings. Built once per course and
    /// queried by the scenery and dressing planners through a spatial hash.
    /// <para>
    /// Unknown piece kinds (added by later mechanics) are kept clear by their box, or by
    /// <see cref="OB_ClearanceSettings.defaultPieceSize"/> when they have none, and rotating or unknown
    /// pieces use the circle their box sweeps.
    /// </para>
    /// </summary>
    public sealed class OB_CourseClearance {
        struct Zone {
            public Bounds bounds;
            /// <summary>The piece's box with the ordinary margin and headroom; for wide zones, smaller than <see cref="bounds"/>.</summary>
            public Bounds core;
            /// <summary>Lowest point of what the zone protects (the piece itself, not its margin); -infinity for wide zones.</summary>
            public float rest;
            public int piece;
            public bool route;
        }

        /// <summary>Zones resting this close above a hanging candidate's top still count as resting on it.</summary>
        const float RestTolerance = 0.05f;

        readonly List<Zone> zones = new();
        readonly Dictionary<long, List<int>> cells = new();
        readonly Stack<List<int>> spareLists = new();
        readonly List<Vector3> routePoints = new();
        readonly List<OB_Tier> routeTiers = new();
        OB_ClearanceSettings settings;

        /// <summary>Everything the course occupies, headroom included.</summary>
        public Bounds CourseBounds { get; private set; }
        /// <summary>Top centres of the surfaces players move between, in route order (lobby first).</summary>
        public IReadOnlyList<Vector3> RoutePoints => routePoints;
        public int ZoneCount => zones.Count;
        public OB_ClearanceSettings Settings => settings;

        /// <summary>Tier of the route point nearest to <paramref name="position"/> across the ground (Basic when there is no route).</summary>
        public OB_Tier NearestTier(Vector3 position) {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < routePoints.Count; i++) {
                float dx = routePoints[i].x - position.x, dz = routePoints[i].z - position.z;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = i;
                }
            }
            return TierAt(best);
        }

        /// <summary>The tier of the stretch of course around route point <paramref name="index"/>.</summary>
        public OB_Tier TierAt(int index) => routeTiers.Count == 0 ? OB_Tier.Basic : routeTiers[Mathf.Clamp(index, 0, routeTiers.Count - 1)];

        /// <summary>Rebuilds the free space for <paramref name="layout"/> (null: only the lobby).</summary>
        public void Build(OB_CourseLayout layout, Vector3 lobbySpawn, in OB_ClearanceSettings clearance) {
            settings = clearance;
            zones.Clear();
            routePoints.Clear();
            routeTiers.Clear();
            foreach (var list in cells.Values) {
                list.Clear();
                spareLists.Push(list);
            }
            cells.Clear();

            var lobby = new Bounds(lobbySpawn + Vector3.up * (settings.lobbySize.y * 0.5f), settings.lobbySize);
            AddZone(Grow(lobby), lobby.min.y, -1, false);
            AddRoutePoint(lobbySpawn, OB_Tier.Basic);

            if (layout != null) {
                for (int i = 0; i < layout.pieces.Count; i++) {
                    OB_Piece piece = layout.pieces[i];
                    Bounds box = Sweep(piece, PieceBounds(piece, settings.defaultPieceSize));
                    if (IsWide(piece)) AddZone(Widen(box, settings.turnedGravityMargin), float.NegativeInfinity, i, false, Grow(box));
                    else AddZone(Grow(box), box.min.y, i, false);
                    if (IsRouteSurface(piece.kind)) {
                        int stage = Mathf.Clamp(piece.stage, 0, layout.StageCount - 1);
                        AddRoutePoint(new Vector3(box.center.x, box.max.y, box.center.z), layout.stages[stage].tier);
                    }
                }
                AddRoutePoint(layout.finishTop, layout.StageCount > 0 ? layout.stages[layout.StageCount - 1].tier : OB_Tier.Basic);
            }
            AddRouteColumns();

            var all = zones[0].bounds;
            foreach (var zone in zones) all.Encapsulate(zone.bounds);
            CourseBounds = all;
        }

        /// <summary>
        /// True when <paramref name="bounds"/> overlaps space the course needs, other than the zones of
        /// pieces <paramref name="ignoreA"/> and <paramref name="ignoreB"/> (the anchors it hangs from).
        /// <paramref name="ignoreRoute"/> lets things the route passes through on purpose (arches) through.
        /// Something hanging under a surface passes <paramref name="hangsFrom"/> (its top): pieces and
        /// route columns resting at or above that height only reach it with their under-margin, so they
        /// do not count (rock under a lava pool may touch the stepping stones standing in it).
        /// Something that dissolves as the camera reaches it (arches and their banners) passes
        /// <paramref name="dissolvesNearCamera"/>: around a gravity turn it only keeps the ordinary
        /// margin, since a camera turned onto a wall or ceiling sees through it rather than into it.
        /// </summary>
        public bool Blocks(Bounds bounds, int ignoreA = -1, int ignoreB = -1, bool ignoreRoute = false, float hangsFrom = float.PositiveInfinity,
            bool dissolvesNearCamera = false) {
            CellRange(bounds.min, bounds.max, out int x0, out int z0, out int x1, out int z1);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++) {
                    if (!cells.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (int index in list) {
                        Zone zone = zones[index];
                        if (zone.route ? ignoreRoute : zone.piece >= 0 && (zone.piece == ignoreA || zone.piece == ignoreB)) continue;
                        if (zone.rest >= hangsFrom - RestTolerance) continue;
                        if ((dissolvesNearCamera ? zone.core : zone.bounds).Intersects(bounds)) return true;
                    }
                }
            return false;
        }

        /// <summary>Distance from <paramref name="point"/> to the nearest free-space zone, or <paramref name="searchRadius"/> if none is closer.</summary>
        public float Clearance(Vector3 point, float searchRadius) {
            float best = searchRadius * searchRadius;
            Vector3 reach = new(searchRadius, 0f, searchRadius);
            CellRange(point - reach, point + reach, out int x0, out int z0, out int x1, out int z1);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++) {
                    if (!cells.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (int index in list) best = Mathf.Min(best, zones[index].bounds.SqrDistance(point));
                }
            return Mathf.Sqrt(best);
        }

        /// <summary>The world box a piece occupies (no margins). Rotating and unknown kinds use the circle their box sweeps.</summary>
        public static Bounds PieceBounds(in OB_Piece piece, Vector3 defaultSize) {
            Vector3 size = piece.size.sqrMagnitude > 0.0001f ? piece.size : defaultSize;
            if (piece.kind == OB_PieceKind.Pendulum) return PendulumBounds(piece, size);
            Vector3 centre = piece.position;
            // Turntables and mushrooms have their origin on top (the disc, the cap); a mushroom's stalk hangs size.y below it.
            if (piece.kind is OB_PieceKind.Turntable or OB_PieceKind.Mushroom) centre -= Vector3.up * (size.y * 0.5f);
            else if (!HasBoxOrigin(piece.kind)) centre += Vector3.up * (size.y * 0.5f);   // pads and pickups sit on their origin
            if (!IsStaticBox(piece.kind)) {
                float radius = new Vector2(size.x, size.z).magnitude * 0.5f;
                return new Bounds(centre, new Vector3(radius * 2f, size.y, radius * 2f));
            }
            float yaw = piece.yaw * Mathf.Deg2Rad, cos = Mathf.Abs(Mathf.Cos(yaw)), sin = Mathf.Abs(Mathf.Sin(yaw));
            return new Bounds(centre, new Vector3(cos * size.x + sin * size.z, size.y, sin * size.x + cos * size.z));
        }

        /// <summary>A pendulum's whole swing: from its pivot down the arm, out to either side by the amplitude.</summary>
        static Bounds PendulumBounds(in OB_Piece piece, Vector3 size) {
            float arm = size.y, head = Mathf.Max(size.x, size.z);
            float amplitude = Mathf.Clamp(Mathf.Abs(piece.vector.x), 0f, 90f) * Mathf.Deg2Rad;
            float reach = arm * Mathf.Sin(amplitude) + head;   // square, so the swing axis' yaw does not matter
            return new Bounds(piece.position + Vector3.down * (arm * 0.5f), new Vector3(reach * 2f, arm + head, reach * 2f));
        }

        /// <summary>Box pieces that never move or turn (their box is exactly what they occupy; a log only spins about its own axis).</summary>
        public static bool IsStaticBox(OB_PieceKind kind) => kind is OB_PieceKind.Platform or OB_PieceKind.CheckpointPad
            or OB_PieceKind.Finish or OB_PieceKind.Lava or OB_PieceKind.GravityZone or OB_PieceKind.Conveyor
            or OB_PieceKind.Crumble or OB_PieceKind.Beat or OB_PieceKind.WallJumpWall or OB_PieceKind.Mover
            or OB_PieceKind.Beam or OB_PieceKind.OnOffBlock or OB_PieceKind.Laser or OB_PieceKind.RollingLog
            or OB_PieceKind.Crusher or OB_PieceKind.GravityFlip or OB_PieceKind.RisingLava or OB_PieceKind.Ice
            or OB_PieceKind.Wind;

        /// <summary>Pieces whose origin is their box centre (the generator's Box helper); others sit on their origin.</summary>
        static bool HasBoxOrigin(OB_PieceKind kind) => kind is not (OB_PieceKind.Spring or OB_PieceKind.Boost or OB_PieceKind.Coin
            or OB_PieceKind.Star or OB_PieceKind.Cannon);

        /// <summary>Pieces players stand on or pass through on their way, in route order (not hazards, pickups or triggers).</summary>
        static bool IsRouteSurface(OB_PieceKind kind) => kind is not (OB_PieceKind.Coin or OB_PieceKind.Star or OB_PieceKind.GravityZone
            or OB_PieceKind.Lava or OB_PieceKind.RisingLava or OB_PieceKind.WallJumpWall or OB_PieceKind.SpinnerBar
            or OB_PieceKind.SpinnerCross or OB_PieceKind.Pendulum or OB_PieceKind.Crusher or OB_PieceKind.Laser
            or OB_PieceKind.OnOffSwitch or OB_PieceKind.GravityFlip or OB_PieceKind.Wind);

        bool IsWide(in OB_Piece piece) {
            // Pieces that turn gravity turn the player (and the chase camera) onto walls and ceilings.
            if (piece.kind is OB_PieceKind.GravityZone or OB_PieceKind.GravityFlip && piece.vector != Vector3.zero) return true;
            if (settings.wideKinds != null)
                foreach (var kind in settings.wideKinds)
                    if (kind == piece.kind) return true;
            return false;
        }

        /// <summary>Movers occupy their whole travel, crushers their whole drop.</summary>
        static Bounds Sweep(in OB_Piece piece, Bounds box) {
            Vector3 travel = piece.kind switch {
                OB_PieceKind.Mover => piece.vector,
                OB_PieceKind.Crusher => Vector3.down * Mathf.Abs(piece.vector.y),
                _ => Vector3.zero
            };
            if (travel != Vector3.zero) {
                var end = box;
                end.center += travel;
                box.Encapsulate(end);
            }
            return box;
        }

        Bounds Grow(Bounds box) {
            Vector3 min = box.min - new Vector3(settings.margin, settings.margin, settings.margin);
            Vector3 max = box.max + new Vector3(settings.margin, settings.headroom, settings.margin);
            box.SetMinMax(min, max);
            return box;
        }

        Bounds Widen(Bounds box, float amount) {
            box.Expand(amount * 2f);
            box.max += Vector3.up * settings.headroom;
            return box;
        }

        void AddRoutePoint(Vector3 point, OB_Tier tier) {
            routePoints.Add(point);
            routeTiers.Add(tier);
        }

        /// <summary>A free column (headroom tall) every <see cref="OB_ClearanceSettings.routeStep"/> between consecutive surfaces.</summary>
        void AddRouteColumns() {
            float r = settings.routeRadius;
            const float below = 0.5f;   // the column starts just under the line, so it never reaches the rock under a surface
            var size = new Vector3(r * 2f, settings.headroom + below, r * 2f);
            for (int i = 1; i < routePoints.Count; i++) {
                Vector3 a = routePoints[i - 1], b = routePoints[i];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / settings.routeStep));
                for (int s = 0; s <= steps; s++) {
                    Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                    AddZone(new Bounds(p + Vector3.up * (size.y * 0.5f - below), size), p.y - below, -1, true);
                }
            }
        }

        /// <summary>Marks space taken by dressing (towers, arches), so scenery placed afterwards keeps off it.</summary>
        public void Reserve(Bounds bounds) => AddZone(bounds, float.NegativeInfinity, -1, false);

        void AddZone(Bounds bounds, float rest, int piece, bool route) => AddZone(bounds, rest, piece, route, bounds);

        void AddZone(Bounds bounds, float rest, int piece, bool route, Bounds core) {
            int index = zones.Count;
            zones.Add(new Zone { bounds = bounds, core = core, rest = rest, piece = piece, route = route });
            CellRange(bounds.min, bounds.max, out int x0, out int z0, out int x1, out int z1);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++) {
                    long key = Key(x, z);
                    if (!cells.TryGetValue(key, out var list)) {
                        list = spareLists.Count > 0 ? spareLists.Pop() : new List<int>();
                        cells.Add(key, list);
                    }
                    list.Add(index);
                }
        }

        void CellRange(Vector3 min, Vector3 max, out int x0, out int z0, out int x1, out int z1) {
            float inv = 1f / settings.cellSize;
            x0 = Mathf.FloorToInt(min.x * inv);
            z0 = Mathf.FloorToInt(min.z * inv);
            x1 = Mathf.FloorToInt(max.x * inv);
            z1 = Mathf.FloorToInt(max.z * inv);
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
