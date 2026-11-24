using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>The fixed playing field every board is generated inside, in board units centred on 0.</summary>
    public static class DI_Arena {
        public const float HalfWidth = 100f;
        public const float HalfHeight = 74f;
        /// <summary>Keeps whole base visuals inside the border, not just their centres.</summary>
        public const float EdgeClearance = 5f;
        /// <summary>Most bases a board may hold: the backdrop shader's territory array (DI_MAX_TERRITORY).</summary>
        public const int MaxBases = 64;
        /// <summary>Most NPC teams a board may hold: one per NPC colour.</summary>
        public const int MaxNpcTeams = 4;

        public static float InnerHalfWidth => HalfWidth - EdgeClearance;
        public static float InnerHalfHeight => HalfHeight - EdgeClearance;

        public static bool Contains(Vector2 point) =>
            Mathf.Abs(point.x) <= InnerHalfWidth + 0.001f && Mathf.Abs(point.y) <= InnerHalfHeight + 0.001f;
    }

    /// <summary>How a board's bases are laid out and wired.</summary>
    public enum DI_BoardShape {
        /// <summary>Bases scattered over the whole field, each linked to its nearest neighbours.</summary>
        Scatter = 0,
        /// <summary>Clusters of bases joined only by a few long bridges: the bridges are choke points.</summary>
        Islands = 1,
        /// <summary>A rich core inside two rings, joined by spokes.</summary>
        Rings = 2,
        /// <summary>Long parallel lanes with crossings every few bases.</summary>
        Lanes = 3,
        /// <summary>A jittered hex grid: every base has up to six roads.</summary>
        Honeycomb = 4,
    }

    /// <summary>A base's special role on the board (a base has at most one).</summary>
    [Flags]
    public enum DI_BaseKind {
        Plain = 0,
        Turret = 1,
        Super = 2,
        Speed = 4,
        Factory = 8,
        Fortress = 16,
    }

    /// <summary>
    /// One board a match can be played on: its shape, size, special bases, NPC teams and match length.
    /// The built-in boards are <see cref="DI_BoardLayouts.All"/>; <see cref="DI_BoardGenerator"/> turns a
    /// layout and a seed into a concrete board, so the same board name still plays differently every match.
    /// </summary>
    [Serializable]
    public sealed class DI_BoardLayout {
        [Tooltip("Stable id sent to clients and used in votes; never rename a shipped id.")]
        public string id;
        public string title;
        [Tooltip("One line for the board vote and the HUD.")]
        public string tagline;
        [Tooltip("Theme icon name (Tools/UIIcons/icons.json).")]
        public string icon;
        public DI_BoardShape shape;
        [Range(8, DI_Arena.MaxBases)] public int baseCount = 32;
        [Range(1, DI_Arena.MaxNpcTeams)] public int npcTeams = 4;
        [Tooltip("Match length on this board, before the runner's time limit caps it.")]
        [Min(30)] public int matchSeconds = 480;
        [Tooltip("Closest two base centres may be (board units).")]
        [Min(8f)] public float minSpacing = 12f;

        [Header("Roads (Scatter, Islands, Honeycomb)")]
        [Tooltip("Nearest bases each base gets a road to (within its region).")]
        [Range(1, 8)] public int linksPerBase = 6;
        [Tooltip("Longest local road.")]
        [Min(12f)] public float maxLinkLength = 65f;

        [Header("Shape")]
        [Tooltip("Islands: island count (5 or more puts one in the middle). Lanes: lane count.")]
        [Range(1, 8)] public int regions = 1;
        [Tooltip("Islands: island radius. Honeycomb: distance between neighbouring cells.")]
        [Min(8f)] public float regionSize = 20f;
        [Tooltip("Islands: outer islands further apart than this get no bridge of their own.")]
        [Min(0f)] public float maxBridgeLength = 110f;
        [Tooltip("Rings: bases in the core. Lanes: a crossing every this many bases.")]
        [Min(1)] public int shapeDetail = 3;

        [Header("Special bases")]
        [Range(0, 2)] public int turrets = 2;
        [Range(0, 2)] public int superBases = 2;
        [Range(0, 6)] public int speedBases = 4;
        [Range(0, DI_BoardGenerator.MaxObjectives)] public int factories = 2;
        [Range(0, DI_BoardGenerator.MaxObjectives)] public int fortresses = 2;
        [Tooltip("Super bases sit this far above and below the centre (share of the half height); 0 puts them in the middle.")]
        [Range(0f, 1f)] public float superSpread = 0.47f;

        public int SpecialCount => turrets + superBases + speedBases + factories + fortresses;

        /// <summary>The vote line: tagline plus the numbers that matter ("32 bases, 8 min, 4 bot teams").</summary>
        public string Describe() =>
            $"{tagline} {baseCount} bases, {Mathf.CeilToInt(matchSeconds / 60f)} min, {npcTeams} bot team{(npcTeams == 1 ? "" : "s")}.";

        public override string ToString() => id;
    }

    /// <summary>The built-in boards, in rotation order.</summary>
    public static class DI_BoardLayouts {
        public const string OpenFieldId = "open_field";

        static readonly DI_BoardLayout[] all = {
            new() {
                id = OpenFieldId, title = "Open Field", icon = "explore", shape = DI_BoardShape.Scatter,
                tagline = "The classic: bases scattered everywhere.",
                baseCount = 32, npcTeams = 4, matchSeconds = 480,
            },
            new() {
                id = "archipelago", title = "Archipelago", icon = "mountains", shape = DI_BoardShape.Islands,
                tagline = "Islands joined by long bridges. Hold the crossings.",
                baseCount = 32, npcTeams = 4, matchSeconds = 480, minSpacing = 11f,
                linksPerBase = 3, maxLinkLength = 32f, regions = 5, regionSize = 21f, maxBridgeLength = 110f,
                superSpread = 0f,
            },
            new() {
                id = "halo", title = "Halo", icon = "hub", shape = DI_BoardShape.Rings,
                tagline = "Two rings around a rich core. Everyone wants the middle.",
                baseCount = 34, npcTeams = 4, matchSeconds = 480, shapeDetail = 4, superSpread = 0f,
            },
            new() {
                id = "three_lanes", title = "Three Lanes", icon = "trending_up", shape = DI_BoardShape.Lanes,
                tagline = "Three long lanes with crossings. Push, flank, break through.",
                baseCount = 33, npcTeams = 4, matchSeconds = 480, regions = 3, shapeDetail = 3, speedBases = 3,
            },
            new() {
                id = "honeycomb", title = "Honeycomb", icon = "castle", shape = DI_BoardShape.Honeycomb,
                tagline = "A tight hex grid where every base has six roads.",
                baseCount = 36, npcTeams = 4, matchSeconds = 480, regionSize = 25f,
                linksPerBase = 6, maxLinkLength = 31f,
            },
            new() {
                id = "skirmish", title = "Skirmish", icon = "crossed_swords", shape = DI_BoardShape.Scatter,
                tagline = "A small board and two bot teams. Fast and brutal.",
                baseCount = 22, npcTeams = 2, matchSeconds = 300, minSpacing = 14f, maxLinkLength = 70f,
                superBases = 1, speedBases = 2, factories = 1, fortresses = 1,
            },
            new() {
                id = "grand_frontier", title = "Grand Frontier", icon = "flag_objective", shape = DI_BoardShape.Scatter,
                tagline = "A huge board with extra factories and fortresses.",
                baseCount = 46, npcTeams = 4, matchSeconds = 720, minSpacing = 11f, linksPerBase = 5, maxLinkLength = 50f,
                factories = 4, fortresses = 4,
            },
        };

        public static IReadOnlyList<DI_BoardLayout> All => all;

        /// <summary>The layout with <paramref name="id"/>, or null.</summary>
        public static DI_BoardLayout Find(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (DI_BoardLayout layout in all)
                if (layout.id == id) return layout;
            return null;
        }

        public static int IndexOf(string id) {
            for (int i = 0; i < all.Length; i++)
                if (all[i].id == id) return i;
            return -1;
        }

        /// <summary>
        /// Up to <paramref name="count"/> distinct board indexes for a vote, never the one just played
        /// (unless nothing else exists), in a random order from <paramref name="random"/>.
        /// </summary>
        public static int[] VoteOptions(int count, int lastPlayed, System.Random random) {
            var pool = new List<int>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (i != lastPlayed) pool.Add(i);
            if (pool.Count == 0 && all.Length > 0) pool.Add(Mathf.Clamp(lastPlayed, 0, all.Length - 1));
            for (int i = pool.Count - 1; i > 0; i--) {
                int j = random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            int take = Mathf.Clamp(count, 0, pool.Count);
            return pool.GetRange(0, take).ToArray();
        }
    }

    /// <summary>A generated board: base positions, roads, special roles and where teams may start.</summary>
    public sealed class DI_BoardPlan {
        public Vector2[] positions;
        /// <summary>Roads as (lower index, higher index) pairs, each once.</summary>
        public Vector2Int[] links;
        public DI_BaseKind[] kinds;
        /// <summary>False for bases no team may start on (cores, the middle island).</summary>
        public bool[] spawnable;

        public int Count => positions.Length;
    }

    /// <summary>
    /// Builds a <see cref="DI_BoardPlan"/> from a <see cref="DI_BoardLayout"/> and a seed. Pure and
    /// deterministic (a seed always gives the same board), so the rules can be tested without a server.
    /// Every board is guaranteed to be connected, inside the arena and at the layout's spacing (save
    /// for the rare top-up base placed at a slightly smaller spacing when a shape cannot fit them all).
    /// </summary>
    public static class DI_BoardGenerator {
        /// <summary>Factory and fortress objectives per board.</summary>
        public const int MaxObjectives = 4;
        /// <summary>A road may not pass closer than this to another base's centre.</summary>
        public const float RoadClearance = 4.5f;
        const int PlacementAttempts = 4000;
        // Top-up bases relax the spacing by this factor at each try, so a crowded board still gets every base.
        const float SpacingRelaxation = 0.9f;
        const int SpacingRelaxSteps = 4;

        // Special-base objectives as shares of the inner half-extents. Factories take one diagonal's
        // corners first, fortresses the other, then the edge midpoints, so no two specials share a point.
        const float TurretSpread = 0.35f;
        const float SpeedEdgeInset = 20f;
        const float ObjectiveCornerX = 0.62f;
        const float ObjectiveCornerY = 0.57f;
        const float ObjectiveEdge = 0.8f;
        static readonly Vector2[] FactoryObjectives = {
            Objective(-ObjectiveCornerX, ObjectiveCornerY), Objective(ObjectiveCornerX, -ObjectiveCornerY),
            Objective(-ObjectiveEdge, 0f), Objective(ObjectiveEdge, 0f),
        };
        static readonly Vector2[] FortressObjectives = {
            Objective(-ObjectiveCornerX, -ObjectiveCornerY), Objective(ObjectiveCornerX, ObjectiveCornerY),
            Objective(0f, ObjectiveEdge), Objective(0f, -ObjectiveEdge),
        };

        // Rings: radii as shares of the inner half-extents, the core's radius in board units, and how
        // much each ring base may wander off its slot.
        const float InnerRingRadius = 0.48f;
        const float OuterRingRadius = 0.9f;
        const float InnerRingShare = 0.4f;
        const float CoreRadius = 9f;
        const float RingAngleJitter = 0.15f;
        const float RingRadiusJitter = 2f;
        // Lanes: the outer lanes' distance from the middle (share of the inner half height) and jitter.
        const float LaneSpread = 0.7f;
        const float LaneJitterX = 3f;
        const float LaneJitterY = 5f;
        // Honeycomb jitter (board units).
        const float CellJitter = 1.5f;
        const float HexRowFactor = 0.8660254f;
        // A honeycomb road reaches the six neighbours but not the next ring out.
        const float HexLinkReach = 1.2f;

        static Vector2 Objective(float x, float y) => new(x * DI_Arena.HalfWidth, y * DI_Arena.HalfHeight);

        public static DI_BoardPlan Generate(DI_BoardLayout layout, int seed) {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var random = new System.Random(seed);
            var board = new Builder(layout, random);
            switch (layout.shape) {
                case DI_BoardShape.Islands: board.BuildIslands(); break;
                case DI_BoardShape.Rings: board.BuildRings(); break;
                case DI_BoardShape.Lanes: board.BuildLanes(); break;
                case DI_BoardShape.Honeycomb: board.BuildHoneycomb(); break;
                default: board.BuildScatter(); break;
            }
            board.TopUp();
            board.Connect();
            return board.Finish();
        }

        sealed class Builder {
            readonly DI_BoardLayout layout;
            readonly System.Random random;
            readonly List<Vector2> points = new();
            readonly List<int> regions = new();
            readonly List<bool> spawnable = new();
            readonly List<Vector2Int> links = new();
            readonly HashSet<long> linkKeys = new();

            public Builder(DI_BoardLayout layout, System.Random random) {
                this.layout = layout;
                this.random = random;
            }

            float Next(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            int Target => Mathf.Clamp(layout.baseCount, 1, DI_Arena.MaxBases);

            // ---------------------------------------------------------------- shapes

            public void BuildScatter() {
                Scatter(Target, 0, Vector2.zero, DI_Arena.InnerHalfWidth, DI_Arena.InnerHalfHeight, layout.minSpacing);
                LinkSpanningTree();
                LinkNearest(layout.linksPerBase, layout.maxLinkLength);
            }

            public void BuildIslands() {
                int islands = Mathf.Max(1, layout.regions);
                bool middle = islands >= 5;
                int outer = middle ? islands - 1 : islands;
                float radius = layout.regionSize;
                var centres = new List<Vector2>();
                if (middle) centres.Add(Vector2.zero);
                float reachX = DI_Arena.InnerHalfWidth - radius, reachY = DI_Arena.InnerHalfHeight - radius;
                for (int i = 0; i < outer; i++) {
                    // Spread round the rectangle, starting on the diagonals: four islands sit in the corners.
                    float angle = Mathf.PI * 0.25f + Mathf.PI * 2f * i / outer;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    direction /= Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
                    centres.Add(new Vector2(direction.x * reachX, direction.y * reachY));
                }

                int share = Target / centres.Count, extra = Target % centres.Count;
                for (int island = 0; island < centres.Count; island++) {
                    int count = share + (island < extra ? 1 : 0);
                    // The middle island is the prize, not a start.
                    Disc(count, island, centres[island], radius, layout.minSpacing, !(middle && island == 0));
                }

                LinkNearest(layout.linksPerBase, layout.maxLinkLength, sameRegion: true);
                for (int a = 0; a < centres.Count; a++)
                    for (int b = a + 1; b < centres.Count; b++) {
                        bool hub = middle && a == 0;
                        if (hub || Vector2.Distance(centres[a], centres[b]) <= layout.maxBridgeLength)
                            Bridge(a, b);
                    }
            }

            public void BuildRings() {
                int core = Mathf.Clamp(layout.shapeDetail, 1, Target);
                int inner = Mathf.RoundToInt((Target - core) * InnerRingShare);
                int outer = Target - core - inner;
                for (int i = 0; i < core; i++) {
                    float angle = Mathf.PI * 2f * i / core + Mathf.PI * 0.25f;
                    Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (core == 1 ? 0f : CoreRadius), 0, false);
                }
                Ring(inner, 1, InnerRingRadius);
                Ring(outer, 2, OuterRingRadius);

                LinkNearest(2, layout.maxLinkLength, sameRegion: true, region: 0);
                Chain(1, true);
                Chain(2, true);
                Spokes(0, 1, 1, true);
                Spokes(1, 2, 2, true);
            }

            public void BuildLanes() {
                int lanes = Mathf.Max(1, layout.regions);
                int share = Target / lanes, extra = Target % lanes;
                for (int lane = 0; lane < lanes; lane++) {
                    int count = share + (lane < extra ? 1 : 0);
                    float y = lanes == 1 ? 0f : Mathf.Lerp(-LaneSpread, LaneSpread, (float)lane / (lanes - 1)) * DI_Arena.InnerHalfHeight;
                    for (int i = 0; i < count; i++) {
                        float x = count == 1 ? 0f : Mathf.Lerp(-DI_Arena.InnerHalfWidth, DI_Arena.InnerHalfWidth, (float)i / (count - 1));
                        Vector2 candidate = Clamp(new Vector2(x + Next(-LaneJitterX, LaneJitterX), y + Next(-LaneJitterY, LaneJitterY)));
                        if (Clear(candidate, layout.minSpacing))
                            Add(candidate, lane, true);
                    }
                }
                for (int lane = 0; lane < lanes; lane++)
                    Chain(lane, false);
                for (int lane = 0; lane + 1 < lanes; lane++)
                    Spokes(lane, lane + 1, Mathf.Max(1, layout.shapeDetail), false);
            }

            public void BuildHoneycomb() {
                float cell = Mathf.Max(layout.minSpacing, layout.regionSize);
                float row = cell * HexRowFactor;
                int rows = Mathf.FloorToInt(2f * DI_Arena.InnerHalfHeight / row) + 1;
                float top = (rows - 1) * row * 0.5f;
                var cells = new List<Vector2>();
                int evenColumns = Mathf.FloorToInt(2f * DI_Arena.InnerHalfWidth / cell) + 1;
                float left = -(evenColumns - 1) * cell * 0.5f;
                for (int r = 0; r < rows; r++) {
                    // Odd rows sit half a cell over, as in a hex grid.
                    float start = left + (r % 2 == 0 ? 0f : cell * 0.5f);
                    for (float x = start; x <= DI_Arena.InnerHalfWidth + 0.001f; x += cell)
                        cells.Add(new Vector2(x, top - r * row));
                }
                // Drop random cells until the board has its size; the gaps become detours.
                while (cells.Count > Target)
                    cells.RemoveAt(random.Next(cells.Count));
                foreach (Vector2 centre in cells) {
                    Vector2 candidate = Clamp(centre + new Vector2(Next(-CellJitter, CellJitter), Next(-CellJitter, CellJitter)));
                    if (Clear(candidate, layout.minSpacing))
                        Add(candidate, 0, true);
                }
                LinkNearest(layout.linksPerBase, Mathf.Min(layout.maxLinkLength, cell * HexLinkReach));
            }

            // ---------------------------------------------------------------- placement

            void Add(Vector2 point, int region, bool canSpawn) {
                points.Add(point);
                regions.Add(region);
                spawnable.Add(canSpawn);
            }

            bool Clear(Vector2 candidate, float spacing) {
                float squared = spacing * spacing;
                foreach (Vector2 point in points)
                    if ((point - candidate).sqrMagnitude < squared)
                        return false;
                return true;
            }

            static Vector2 Clamp(Vector2 point) => new(
                Mathf.Clamp(point.x, -DI_Arena.InnerHalfWidth, DI_Arena.InnerHalfWidth),
                Mathf.Clamp(point.y, -DI_Arena.InnerHalfHeight, DI_Arena.InnerHalfHeight));

            void Scatter(int count, int region, Vector2 centre, float halfWidth, float halfHeight, float spacing, bool canSpawn = true) {
                int placed = 0;
                for (int attempt = 0; placed < count && attempt < PlacementAttempts; attempt++) {
                    Vector2 candidate = Clamp(centre + new Vector2(Next(-halfWidth, halfWidth), Next(-halfHeight, halfHeight)));
                    if (!Clear(candidate, spacing))
                        continue;
                    Add(candidate, region, canSpawn);
                    placed++;
                }
            }

            void Disc(int count, int region, Vector2 centre, float radius, float spacing, bool canSpawn) {
                int placed = 0;
                for (int attempt = 0; placed < count && attempt < PlacementAttempts; attempt++) {
                    float angle = Next(0f, Mathf.PI * 2f);
                    // sqrt keeps the density even across the disc.
                    float distance = radius * Mathf.Sqrt((float)random.NextDouble());
                    Vector2 candidate = Clamp(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
                    if (!Clear(candidate, spacing))
                        continue;
                    Add(candidate, region, canSpawn);
                    placed++;
                }
            }

            void Ring(int count, int region, float share) {
                float startAngle = Next(0f, Mathf.PI * 2f);
                for (int i = 0; i < count; i++) {
                    float angle = startAngle + Mathf.PI * 2f * (i + Next(-RingAngleJitter, RingAngleJitter)) / count;
                    float jitter = Next(-RingRadiusJitter, RingRadiusJitter);
                    var candidate = new Vector2(
                        Mathf.Cos(angle) * (share * DI_Arena.InnerHalfWidth + jitter),
                        Mathf.Sin(angle) * (share * DI_Arena.InnerHalfHeight + jitter));
                    candidate = Clamp(candidate);
                    if (Clear(candidate, layout.minSpacing))
                        Add(candidate, region, true);
                }
            }

            /// <summary>Places any bases a shape could not fit anywhere on the field, relaxing the spacing a little if it must.</summary>
            public void TopUp() {
                float spacing = layout.minSpacing;
                for (int step = 0; step <= SpacingRelaxSteps && points.Count < Target; step++) {
                    Scatter(Target - points.Count, -1, Vector2.zero, DI_Arena.InnerHalfWidth, DI_Arena.InnerHalfHeight, spacing);
                    spacing *= SpacingRelaxation;
                }
            }

            // ---------------------------------------------------------------- roads

            bool AddLink(int a, int b) {
                if (a == b) return false;
                int low = Mathf.Min(a, b), high = Mathf.Max(a, b);
                if (!linkKeys.Add(((long)low << 32) | (uint)high)) return false;
                links.Add(new Vector2Int(low, high));
                return true;
            }

            /// <summary>True when the road from <paramref name="a"/> to <paramref name="b"/> would run through another base.</summary>
            bool Blocked(int a, int b) => PassesThroughBase(points, a, b);

            /// <summary>Prim's minimum spanning tree: connected without long roads crossing the board.</summary>
            void LinkSpanningTree() {
                if (points.Count == 0) return;
                var connected = new HashSet<int> { 0 };
                while (connected.Count < points.Count) {
                    int source = -1, target = -1;
                    float shortest = float.MaxValue;
                    foreach (int i in connected)
                        for (int j = 0; j < points.Count; j++) {
                            if (connected.Contains(j)) continue;
                            float distance = (points[i] - points[j]).sqrMagnitude;
                            if (distance < shortest) {
                                shortest = distance;
                                source = i;
                                target = j;
                            }
                        }
                    if (target < 0) break;
                    AddLink(source, target);
                    connected.Add(target);
                }
            }

            /// <summary>Each base's nearest neighbours within reach, skipping roads that would run through a base.</summary>
            void LinkNearest(int perBase, float maxLength, bool sameRegion = false, int region = int.MinValue) {
                var neighbours = new List<int>();
                for (int i = 0; i < points.Count; i++) {
                    if (region != int.MinValue && regions[i] != region) continue;
                    neighbours.Clear();
                    for (int j = 0; j < points.Count; j++)
                        if (i != j && (!sameRegion || regions[j] == regions[i]))
                            neighbours.Add(j);
                    Vector2 from = points[i];
                    neighbours.Sort((a, b) => (from - points[a]).sqrMagnitude.CompareTo((from - points[b]).sqrMagnitude));
                    for (int n = 0; n < Mathf.Min(perBase, neighbours.Count); n++) {
                        int j = neighbours[n];
                        if (Vector2.Distance(from, points[j]) <= maxLength && !Blocked(i, j))
                            AddLink(i, j);
                    }
                }
            }

            List<int> Members(int region) {
                var members = new List<int>();
                for (int i = 0; i < points.Count; i++)
                    if (regions[i] == region) members.Add(i);
                return members;
            }

            /// <summary>Links a region's bases one after another: by angle round the centre (a ring) or left to right (a lane).</summary>
            void Chain(int region, bool loop) {
                List<int> members = Members(region);
                SortMembers(members, loop);
                for (int i = 0; i + 1 < members.Count; i++)
                    AddLink(members[i], members[i + 1]);
                if (loop && members.Count > 2)
                    AddLink(members[members.Count - 1], members[0]);
            }

            /// <summary>
            /// Every <paramref name="stride"/>-th base of <paramref name="from"/> (in ring or lane order) gets
            /// a road to its nearest clear base in <paramref name="to"/>.
            /// </summary>
            void Spokes(int from, int to, int stride, bool byAngle) {
                List<int> sources = Members(from), targets = Members(to);
                if (targets.Count == 0) return;
                SortMembers(sources, byAngle);
                for (int i = 0; i < sources.Count; i += Mathf.Max(1, stride)) {
                    int source = sources[i];
                    int best = NearestClear(source, targets);
                    if (best >= 0) AddLink(source, best);
                }
            }

            void SortMembers(List<int> members, bool byAngle) {
                if (byAngle)
                    members.Sort((a, b) => Mathf.Atan2(points[a].y, points[a].x).CompareTo(Mathf.Atan2(points[b].y, points[b].x)));
                else
                    members.Sort((a, b) => points[a].x.CompareTo(points[b].x));
            }

            int NearestClear(int source, List<int> candidates) {
                int best = -1;
                float shortest = float.MaxValue;
                foreach (int candidate in candidates) {
                    float distance = (points[source] - points[candidate]).sqrMagnitude;
                    if (distance < shortest && !Blocked(source, candidate)) {
                        shortest = distance;
                        best = candidate;
                    }
                }
                return best;
            }

            /// <summary>The shortest clear road between two regions.</summary>
            void Bridge(int regionA, int regionB) {
                List<int> a = Members(regionA), b = Members(regionB);
                int bestA = -1, bestB = -1;
                float shortest = float.MaxValue;
                foreach (int i in a)
                    foreach (int j in b) {
                        float distance = (points[i] - points[j]).sqrMagnitude;
                        if (distance < shortest && !Blocked(i, j)) {
                            shortest = distance;
                            bestA = i;
                            bestB = j;
                        }
                    }
                if (bestA >= 0) AddLink(bestA, bestB);
            }

            /// <summary>
            /// Joins any separate pieces with the shortest road between them (a clear road when one
            /// exists), so every base can reach every other whatever the shape produced.
            /// </summary>
            public void Connect() {
                int count = points.Count;
                var parent = new int[count];
                for (int i = 0; i < count; i++) parent[i] = i;
                int Find(int x) {
                    while (parent[x] != x) x = parent[x] = parent[parent[x]];
                    return x;
                }
                int pieces = count;
                foreach (Vector2Int link in links) {
                    int a = Find(link.x), b = Find(link.y);
                    if (a == b) continue;
                    parent[a] = b;
                    pieces--;
                }
                while (pieces > 1) {
                    int bestA = -1, bestB = -1;
                    float shortest = float.MaxValue;
                    bool bestClear = false;
                    for (int i = 0; i < count; i++)
                        for (int j = i + 1; j < count; j++) {
                            if (Find(i) == Find(j)) continue;
                            bool clear = !Blocked(i, j);
                            float distance = (points[i] - points[j]).sqrMagnitude;
                            // A clear road always beats one through a base; then the shorter one wins.
                            if (clear && !bestClear || clear == bestClear && distance < shortest) {
                                shortest = distance;
                                bestA = i;
                                bestB = j;
                                bestClear = clear;
                            }
                        }
                    if (bestA < 0) break;
                    AddLink(bestA, bestB);
                    parent[Find(bestA)] = Find(bestB);
                    pieces--;
                }
            }

            // ---------------------------------------------------------------- specials

            public DI_BoardPlan Finish() {
                var kinds = new DI_BaseKind[points.Count];
                float turretX = TurretSpread * DI_Arena.HalfWidth;
                for (int t = 0; t < layout.turrets; t++)
                    Assign(kinds, DI_BaseKind.Turret, new Vector2(layout.turrets == 1 ? 0f : (t == 0 ? -turretX : turretX), 0f));
                float superY = layout.superSpread * DI_Arena.HalfHeight;
                for (int s = 0; s < layout.superBases; s++)
                    Assign(kinds, DI_BaseKind.Super, new Vector2(0f, layout.superBases == 1 ? 0f : (s == 0 ? -superY : superY)));
                float speedReach = DI_Arena.HalfWidth - SpeedEdgeInset;
                for (int s = 0; s < layout.speedBases; s++)
                    Assign(kinds, DI_BaseKind.Speed, new Vector2(
                        layout.speedBases > 1 ? Mathf.Lerp(-speedReach, speedReach, (float)s / (layout.speedBases - 1)) : 0f, 0f));
                for (int f = 0; f < Mathf.Min(layout.factories, FactoryObjectives.Length); f++)
                    Assign(kinds, DI_BaseKind.Factory, FactoryObjectives[f]);
                for (int f = 0; f < Mathf.Min(layout.fortresses, FortressObjectives.Length); f++)
                    Assign(kinds, DI_BaseKind.Fortress, FortressObjectives[f]);

                return new DI_BoardPlan {
                    positions = points.ToArray(),
                    links = links.ToArray(),
                    kinds = kinds,
                    spawnable = spawnable.ToArray(),
                };
            }

            /// <summary>Gives <paramref name="kind"/> to the plain base nearest <paramref name="objective"/>.</summary>
            void Assign(DI_BaseKind[] kinds, DI_BaseKind kind, Vector2 objective) {
                int best = -1;
                float shortest = float.MaxValue;
                for (int i = 0; i < points.Count; i++) {
                    float distance = (points[i] - objective).sqrMagnitude;
                    if (kinds[i] == DI_BaseKind.Plain && distance < shortest) {
                        shortest = distance;
                        best = i;
                    }
                }
                if (best >= 0) kinds[best] = kind;
            }
        }

        /// <summary>True when the road from base <paramref name="a"/> to base <paramref name="b"/> passes within <see cref="RoadClearance"/> of another base.</summary>
        public static bool PassesThroughBase(IReadOnlyList<Vector2> positions, int a, int b) {
            Vector2 start = positions[a], end = positions[b];
            Vector2 direction = end - start;
            float squared = RoadClearance * RoadClearance;
            for (int i = 0; i < positions.Count; i++) {
                if (i == a || i == b) continue;
                float along = Mathf.Clamp01(Vector2.Dot(positions[i] - start, direction) / Mathf.Max(0.001f, direction.sqrMagnitude));
                if ((positions[i] - (start + direction * along)).sqrMagnitude < squared)
                    return true;
            }
            return false;
        }
    }
}
