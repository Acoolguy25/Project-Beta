using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>How densely and how far out the scenery around the course is spread.</summary>
    [Serializable]
    public struct OB_SceneSettings {
        [Header("Floating rocks")]
        [Tooltip("Rocks per stage (multiplied by the tier's density).")]
        [Min(0f)] public float rocksPerStage;
        [Tooltip("Horizontal distance from the route point a rock is scattered around (m); biased towards the near end.")]
        public Vector2 rockDistance;
        [Tooltip("Height relative to that route point (m).")]
        public Vector2 rockHeight;
        [Tooltip("Rock radius (m); the meshes are built at radius 1.")]
        public Vector2 rockScale;
        [Tooltip("Room a rock leaves beyond its own radius to anything the course needs (m), so it never blocks a jump or the chase camera.")]
        [Min(0f)] public float rockClearance;

        [Header("Small islands")]
        [Min(0f)] public float islandsPerStage;
        public Vector2 islandDistance;
        public Vector2 islandHeight;
        public Vector2 islandScale;
        [Tooltip("Radius of the island meshes at scale 1 (m).")]
        [Min(0f)] public float islandRadius;
        [Min(0f)] public float islandClearance;

        [Header("Landmarks")]
        [Tooltip("Distance of a landmark from its tier's stretch of course (m).")]
        public Vector2 landmarkDistance;
        [Tooltip("Where along the tier's stretch landmarks stand (0 = its first checkpoint, 1 = its last).")]
        public Vector2 landmarkAlong;
        [Tooltip("Route length per landmark along a tier's stretch (m), so every checkpoint has one in view; each tier shows each of its kinds at least once, and repeats them on long stretches.")]
        [Min(50f)] public float landmarkSpacing;
        [Tooltip("Height of a landmark's base relative to its tier's mean course height (m): big set-pieces rise from below.")]
        public Vector2 landmarkHeight;
        [Tooltip("Room left between a landmark's footprint and the course (m).")]
        [Min(0f)] public float landmarkClearance;
        [Tooltip("How far a landmark is pushed out per try when it would be too close (m).")]
        [Min(1f)] public float landmarkPushOut;

        [Header("Clouds")]
        [Min(0)] public int cloudCount;
        public Vector2 cloudDistance;
        [Tooltip("Height relative to the route point a cloud drifts beside (m).")]
        public Vector2 cloudHeight;
        public Vector2 cloudScale;
        [Tooltip("Radius of a cloud cluster at scale 1 (m).")]
        [Min(0f)] public float cloudRadius;

        [Header("Birds")]
        [Tooltip("Radius of a flock's circle (m).")]
        public Vector2 flockRadius;
        [Tooltip("Height of a flock above its anchor (m).")]
        public Vector2 flockHeight;
        [Tooltip("Distance of a flock without a landmark from the route (m).")]
        public Vector2 flockDistance;

        [Tooltip("Placement tries before something is left out.")]
        [Min(1)] public int attempts;

        public static OB_SceneSettings Default => new() {
            rocksPerStage = 16f, rockDistance = new Vector2(18f, 240f), rockHeight = new Vector2(-70f, 30f), rockScale = new Vector2(0.8f, 6f), rockClearance = 10f,
            islandsPerStage = 1.6f, islandDistance = new Vector2(90f, 420f), islandHeight = new Vector2(-80f, 10f), islandScale = new Vector2(0.7f, 1.6f),
            islandRadius = 18f, islandClearance = 25f,
            landmarkDistance = new Vector2(190f, 320f), landmarkAlong = new Vector2(0.1f, 0.95f), landmarkSpacing = 260f, landmarkHeight = new Vector2(-90f, -40f),
            landmarkClearance = 40f, landmarkPushOut = 40f,
            cloudCount = 50, cloudDistance = new Vector2(45f, 320f), cloudHeight = new Vector2(-70f, 6f), cloudScale = new Vector2(0.6f, 1.7f), cloudRadius = 14f,
            flockRadius = new Vector2(25f, 60f), flockHeight = new Vector2(15f, 45f), flockDistance = new Vector2(60f, 140f),
            attempts = 8
        };
    }

    /// <summary>
    /// Plans the scenery around a course: floating rocks and small islands in each tier's style near
    /// their stretch of course, a few big landmarks per tier standing off to alternating sides (ahead of
    /// the players, so they are seen while climbing towards them), clouds, and the centres of bird
    /// flocks. Seeded by the course, and every piece keeps clear of what the course needs
    /// (<see cref="OB_CourseClearance"/>), so it never blocks a jump or the chase camera.
    /// </summary>
    public sealed class OB_ScenePlanner {
        const uint RockStream = 0x5C3E1u, IslandStream = 0x5C3E2u, LandmarkStream = 0x5C3E3u, CloudStream = 0x5C3E4u, FlockStream = 0x5C3E5u;
        /// <summary>Clearance searches reach this far beyond what a piece needs (m).</summary>
        const float SearchSlack = 1f;
        /// <summary>Bias of scatter distances towards the near end (1 = uniform).</summary>
        const float NearBias = 1.6f;

        readonly List<OB_Placement> placements = new();
        readonly List<int> tierFirst = new(), tierLast = new();
        OB_Tier? styleTier;

        public IReadOnlyList<OB_Placement> Placements => placements;

        /// <summary>Footprint radius (x) and height above its origin (y) of a tier's landmark; zero when it has none.</summary>
        public delegate Vector2 LandmarkSize(OB_Tier tier, int index);

        /// <summary>
        /// Plans everything around the course. <paramref name="stageCount"/> scales the counts;
        /// <paramref name="landmarkSize"/> gives each tier landmark's footprint and height (null skips landmarks).
        /// Rocks, islands and clouds take the style of the stretch of course they float beside, or all of
        /// <paramref name="style"/> when given (a course too short for its tiers to have stretches of their own).
        /// </summary>
        public void Plan(int seed, int stageCount, OB_CourseClearance clearance, in OB_SceneSettings s, LandmarkSize landmarkSize, OB_Tier? style = null) {
            placements.Clear();
            styleTier = style;
            if (clearance.RoutePoints.Count == 0) return;
            if (landmarkSize != null) PlanLandmarks(seed, clearance, s, landmarkSize);
            PlanScatter(new OB_EnvRandom(seed, IslandStream), OB_SceneryKind.Island, s.islandsPerStage * Mathf.Max(1, stageCount), clearance, s);
            PlanScatter(new OB_EnvRandom(seed, RockStream), OB_SceneryKind.Rock, s.rocksPerStage * Mathf.Max(1, stageCount), clearance, s);
            PlanClouds(new OB_EnvRandom(seed, CloudStream), clearance, s);
            PlanFlocks(new OB_EnvRandom(seed, FlockStream), clearance, s);
        }

        // ---------------------------------------------------------------- landmarks
        void PlanLandmarks(int seed, OB_CourseClearance clearance, in OB_SceneSettings s, LandmarkSize sizeOf) {
            var random = new OB_EnvRandom(seed, LandmarkStream);
            var points = clearance.RoutePoints;
            TierSpans(clearance);
            float side = random.Sign();
            for (int t = 0; t < OB_Tiers.Count; t++) {
                if (tierFirst[t] < 0) continue;
                var tier = (OB_Tier)t;
                OB_LandmarkKind[] kinds = OB_Themes.Get(tier).world.landmarks;
                if (kinds == null || kinds.Length == 0) continue;
                int first = tierFirst[t], last = tierLast[t];
                float meanY = MeanHeight(points, first, last);
                float length = PathLength(points, first, last);
                // One per spacing of route, so each checkpoint has a set-piece in the middle distance.
                int count = Mathf.Max(kinds.Length, Mathf.RoundToInt(length / s.landmarkSpacing));
                for (int n = 0; n < count; n++) {
                    int kind = n % kinds.Length;
                    Vector2 size = sizeOf(tier, kind);
                    float radius = size.x, top = size.y;
                    if (radius <= 0f) continue;
                    float along = Mathf.Lerp(s.landmarkAlong.x, s.landmarkAlong.y, (n + 0.5f) / count) * length;
                    Vector3 anchor = points[PointAlong(points, first, last, along)];
                    anchor.y = meanY;
                    // Across the course where it runs here, not across the whole stretch, which may wind.
                    Vector3 run = points[PointAlong(points, first, last, along + HeadingReach)] - points[PointAlong(points, first, last, along - HeadingReach)];
                    run.y = 0f;
                    if (run.sqrMagnitude < 1f) run = Vector3.forward;
                    Vector3 across = Vector3.Cross(Vector3.up, run.normalized);
                    float distance = random.Range(s.landmarkDistance) + radius;
                    float height = random.Range(s.landmarkHeight);
                    Vector3 position = anchor;
                    bool placed = false;
                    for (int attempt = 0; attempt < s.attempts && !placed; attempt++, distance += s.landmarkPushOut) {
                        // Every other try looks across the course, so a tier whose far side folds back on a neighbour still finds room.
                        float towards = attempt % 2 == 0 ? side : -side;
                        position = anchor + across * (towards * distance);
                        position.y = meanY + height;
                        // It must stand clear of the course and belong to its own tier's view, never beside a neighbour's stretch.
                        placed = StandsClear(clearance, position, top, radius + s.landmarkClearance) && clearance.NearestTier(position) == tier;
                    }
                    side = -side;
                    if (!placed) continue;
                    // Repeats of a kind turn a different face to the course.
                    Vector3 facing = Quaternion.Euler(0f, n < kinds.Length ? 0f : random.Range(-70f, 70f), 0f) * (anchor - position);
                    facing.y = 0f;
                    var matrix = Matrix4x4.TRS(position, Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward), Vector3.one);
                    // From its island's roots (about a radius down) to its highest point.
                    var bounds = new Bounds(position + Vector3.up * ((top - radius) * 0.5f), new Vector3(radius * 2f, top + radius, radius * 2f));
                    placements.Add(new OB_Placement { kind = (byte)OB_SceneryKind.Landmark, variant = kind, tier = tier, matrix = matrix, bounds = bounds });
                    // Rocks, islands and the next landmark keep off it.
                    clearance.Reserve(bounds);
                }
            }
        }

        /// <summary>Route length either side of a landmark's anchor used for the course's local heading (m).</summary>
        const float HeadingReach = 60f;

        /// <summary>Length of the route between two of its points, measured across the ground (m).</summary>
        static float PathLength(IReadOnlyList<Vector3> points, int from, int to) {
            float length = 0f;
            for (int i = from + 1; i <= to; i++) length += Flat(points[i] - points[i - 1]);
            return length;
        }

        /// <summary>The route point <paramref name="distance"/> metres along the route from <paramref name="from"/>, clamped to the span.</summary>
        static int PointAlong(IReadOnlyList<Vector3> points, int from, int to, float distance) {
            if (distance <= 0f) return from;
            for (int i = from + 1; i <= to; i++) {
                distance -= Flat(points[i] - points[i - 1]);
                if (distance <= 0f) return i;
            }
            return to;
        }

        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        /// <summary>True when a landmark's column, from its origin up to <paramref name="top"/>, keeps <paramref name="room"/> from everything the course needs.</summary>
        static bool StandsClear(OB_CourseClearance clearance, Vector3 origin, float top, float room) {
            const int samples = 3;   // the base, the middle and the top of the column
            for (int i = 0; i < samples; i++) {
                Vector3 point = origin + Vector3.up * (top * i / (samples - 1f));
                if (clearance.Clearance(point, room + SearchSlack) < room) return false;
            }
            return true;
        }

        /// <summary>First and last route point of each tier (-1 when a tier is not on this course).</summary>
        void TierSpans(OB_CourseClearance clearance) {
            tierFirst.Clear();
            tierLast.Clear();
            for (int t = 0; t < OB_Tiers.Count; t++) {
                tierFirst.Add(-1);
                tierLast.Add(-1);
            }
            for (int i = 0; i < clearance.RoutePoints.Count; i++) {
                int t = (int)clearance.TierAt(i);
                if (tierFirst[t] < 0) tierFirst[t] = i;
                tierLast[t] = i;
            }
        }

        /// <summary>The tier whose style scenery at <paramref name="position"/> takes.</summary>
        OB_Tier StyleAt(OB_CourseClearance clearance, Vector3 position) => styleTier ?? clearance.NearestTier(position);

        static float MeanHeight(IReadOnlyList<Vector3> points, int from, int to) {
            float sum = 0f;
            for (int i = from; i <= to; i++) sum += points[i].y;
            return sum / (to - from + 1);
        }

        // ---------------------------------------------------------------- rocks and islands
        void PlanScatter(OB_EnvRandom random, OB_SceneryKind kind, float baseCount, OB_CourseClearance clearance, in OB_SceneSettings s) {
            var points = clearance.RoutePoints;
            bool island = kind == OB_SceneryKind.Island;
            Vector2 distance = island ? s.islandDistance : s.rockDistance;
            Vector2 height = island ? s.islandHeight : s.rockHeight;
            Vector2 scale = island ? s.islandScale : s.rockScale;
            float baseRadius = island ? s.islandRadius : 1f;
            float room = island ? s.islandClearance : s.rockClearance;
            // Walk the route so every tier gets its share, scaled by the tier's density.
            float perPoint = baseCount / Mathf.Max(1, points.Count);
            float owed = 0f;
            for (int p = 0; p < points.Count; p++) {
                OB_Tier tier = styleTier ?? clearance.TierAt(p);
                owed += perPoint * OB_Themes.Get(tier).world.density;
                for (; owed >= 1f; owed -= 1f) {
                    float size = random.Range(scale);
                    // Small rocks are common, boulders rare.
                    if (!island) size = Mathf.Lerp(scale.x, scale.y, Mathf.Pow(random.Value(), 2.2f));
                    float radius = baseRadius * size;
                    for (int attempt = 0; attempt < s.attempts; attempt++) {
                        float reach = Mathf.Lerp(distance.x, distance.y, Mathf.Pow(random.Value(), NearBias)) + radius;
                        Vector3 position = points[p] + random.OnCircle() * reach;
                        position.y = points[p].y + random.Range(height);
                        if (clearance.Clearance(position, radius + room + SearchSlack) < radius + room) continue;
                        Quaternion rotation = island ? Quaternion.Euler(0f, random.Range(0f, 360f), 0f) : random.Rotation();
                        var matrix = Matrix4x4.TRS(position, rotation, Vector3.one * size);
                        placements.Add(new OB_Placement {
                            // Styled after the stretch of course it floats beside, so tiers never mix at their seams.
                            kind = (byte)kind, variant = random.Next(int.MaxValue), tier = StyleAt(clearance, position), matrix = matrix,
                            bounds = new Bounds(position, Vector3.one * (radius * 2f))
                        });
                        break;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- clouds and birds
        void PlanClouds(OB_EnvRandom random, OB_CourseClearance clearance, in OB_SceneSettings s) {
            var points = clearance.RoutePoints;
            Bounds course = clearance.CourseBounds;
            for (int i = 0; i < s.cloudCount; i++) {
                int p = random.Next(points.Count);
                float size = random.Range(s.cloudScale);
                for (int attempt = 0; attempt < s.attempts; attempt++) {
                    float reach = Mathf.Lerp(s.cloudDistance.x, s.cloudDistance.y, Mathf.Pow(random.Value(), NearBias));
                    Vector3 position = points[p] + random.OnCircle() * reach;
                    // Level with or below the stretch it drifts beside, never hanging over it from far up the course.
                    position.y = points[p].y + random.Range(s.cloudHeight);
                    // In the style of the tier it drifts beside: round cumulus, flat ash banks, or none at all.
                    OB_Tier tier = StyleAt(clearance, position);
                    OB_WorldStyle world = OB_Themes.Get(tier).world;
                    float widen = 1f + (1f - Mathf.Clamp01(world.cloudSquash)) * BankWiden;
                    float radius = s.cloudRadius * size * widen;
                    if (clearance.Clearance(position, radius + SearchSlack) < radius) continue;
                    if (!random.Chance(world.cloudCover)) break;
                    // Clouds turn their long side to the course.
                    Vector3 outward = position - course.center;
                    outward.y = 0f;
                    var rotation = Quaternion.LookRotation(outward.sqrMagnitude > 0.01f ? outward : Vector3.forward);
                    var scale = new Vector3(size * widen, size * world.cloudSquash, size * widen);
                    placements.Add(new OB_Placement {
                        kind = (byte)OB_SceneryKind.Cloud, variant = random.Next(int.MaxValue), tier = tier,
                        matrix = Matrix4x4.TRS(position, rotation, scale), bounds = new Bounds(position, Vector3.one * (radius * 2f))
                    });
                    break;
                }
            }
        }

        /// <summary>How much wider than a cumulus a fully flattened cloud bank spreads.</summary>
        const float BankWiden = 0.7f;

        /// <summary>Flock centres: circling the tier's landmarks first, then over open sky beside the route.</summary>
        void PlanFlocks(OB_EnvRandom random, OB_CourseClearance clearance, in OB_SceneSettings s) {
            int landmarkCount = placements.Count;
            var points = clearance.RoutePoints;
            TierSpans(clearance);
            for (int t = 0; t < OB_Tiers.Count; t++) {
                if (tierFirst[t] < 0) continue;
                var tier = (OB_Tier)t;
                int flocks = OB_Themes.Get(tier).world.flocks;
                int made = 0;
                for (int i = 0; i < landmarkCount && made < flocks; i++) {
                    var landmark = placements[i];
                    if (landmark.kind != (byte)OB_SceneryKind.Landmark || landmark.tier != tier) continue;
                    Vector3 centre = landmark.bounds.center + Vector3.up * (landmark.bounds.extents.y + random.Range(s.flockHeight));   // above its top
                    AddFlock(centre, random.Range(s.flockRadius), tier, ref random);
                    made++;
                }
                for (int attempt = 0; made < flocks && attempt < flocks * s.attempts; attempt++) {
                    int p = random.Next(tierLast[t] - tierFirst[t] + 1) + tierFirst[t];
                    float radius = random.Range(s.flockRadius);
                    Vector3 centre = points[p] + random.OnCircle() * (random.Range(s.flockDistance) + radius);
                    centre.y = points[p].y + random.Range(s.flockHeight);
                    if (clearance.Clearance(centre, radius + SearchSlack) < radius) continue;
                    AddFlock(centre, radius, tier, ref random);
                    made++;
                }
            }
        }

        void AddFlock(Vector3 centre, float radius, OB_Tier tier, ref OB_EnvRandom random) {
            placements.Add(new OB_Placement {
                kind = (byte)OB_SceneryKind.Flock, variant = random.Next(int.MaxValue), tier = tier,
                matrix = Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one * radius), bounds = new Bounds(centre, Vector3.one * (radius * 2f))
            });
        }
    }
}
