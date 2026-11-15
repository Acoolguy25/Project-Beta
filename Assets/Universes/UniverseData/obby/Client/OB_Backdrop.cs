using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Dresses the sky around the generated course: cloud clusters at the course's height and below it,
    /// distant floating islands themed after the difficulty tier of the course beside them (white
    /// columns, grass and trees, desert mesas, snow and ice, volcanic vents, void crystals), and a
    /// cloud sea under everything. Placement is seeded by the course,
    /// so every racer sees the same sky, and it keeps clear of the course so it never blocks a jump.
    /// Instances are created once and moved for each new course.
    /// </summary>
    public sealed class OB_Backdrop : MonoBehaviour {
        [Header("Authored pieces")]
        [Tooltip("Cloud cluster prefabs, picked at random.")]
        public GameObject[] cloudPrefabs;
        [Tooltip("Floating island prefabs per difficulty tier (indexed by OB_Tier).")]
        public TierPrefabs[] tierIslands;
        [Tooltip("The cloud sea; centred under the course and placed below its lowest point.")]
        public Transform cloudSea;

        [Header("Clouds")]
        [Min(0)] public int cloudCount = 50;
        [Tooltip("Horizontal distance from the nearest course piece (m).")]
        public Vector2 cloudDistance = new(45f, 320f);
        [Tooltip("Height relative to the course's lowest and highest points (m).")]
        public Vector2 cloudHeight = new(-75f, 10f);
        public Vector2 cloudScale = new(0.6f, 1.7f);

        [Header("Islands")]
        [Min(0)] public int islandCount = 14;
        public Vector2 islandDistance = new(110f, 420f);
        [Tooltip("Height relative to the stretch of course an island floats beside (m).")]
        public Vector2 islandHeight = new(-60f, 5f);
        public Vector2 islandScale = new(0.8f, 1.8f);

        [Header("Cloud sea")]
        [Tooltip("How far below the course's lowest point the cloud sea sits (m).")]
        [Min(0f)] public float seaDepth = 80f;

        [System.Serializable]
        public struct TierPrefabs {
            public GameObject[] prefabs;
        }

        readonly List<Transform> clouds = new();
        readonly List<Transform> islands = new();
        readonly List<Vector3> obstacles = new();
        readonly List<OB_Tier> obstacleTiers = new();
        readonly GameObject[] single = new GameObject[1];

        /// <summary>Arranges the sky around <paramref name="layout"/>, or around <paramref name="fallbackCentre"/> when there is no course yet.</summary>
        public void Arrange(OB_CourseLayout layout, Vector3 fallbackCentre) {
            obstacles.Clear();
            obstacleTiers.Clear();
            obstacles.Add(fallbackCentre);
            obstacleTiers.Add(OB_Tier.Basic);
            if (layout != null)
                foreach (var piece in layout.pieces) {
                    obstacles.Add(piece.position);
                    obstacleTiers.Add(layout.stages[Mathf.Clamp(piece.stage, 0, layout.StageCount - 1)].tier);
                }
            var bounds = new Bounds(obstacles[0], Vector3.zero);
            foreach (var point in obstacles) bounds.Encapsulate(point);

            var random = new System.Random(layout != null ? layout.seed : 7);
            Scatter(clouds, cloudPrefabs, cloudCount, cloudDistance, cloudHeight, cloudScale, bounds, random, true);
            PlaceIslands(bounds, random);

            if (cloudSea != null) {
                Vector3 centre = bounds.center;
                cloudSea.position = new Vector3(centre.x, bounds.min.y - seaDepth, centre.z);
            }
        }

        void Scatter(List<Transform> pool, GameObject[] prefabs, int count, Vector2 distance, Vector2 height, Vector2 scale,
            Bounds bounds, System.Random random, bool faceCamera, int anchorIndex = -1) {
            if (prefabs == null || prefabs.Length == 0) count = 0;
            while (pool.Count < count) {
                var prefab = prefabs[pool.Count % prefabs.Length];
                if (prefab == null) break;
                pool.Add(Instantiate(prefab, transform).transform);
            }
            for (int i = 0; i < pool.Count; i++) {
                bool used = i < count;
                pool[i].gameObject.SetActive(used);
                if (!used) continue;
                // Anchor on a random course point and push outwards until clear of every piece.
                Vector3 anchor = obstacles[anchorIndex >= 0 ? anchorIndex : random.Next(obstacles.Count)];
                Vector3 position = anchor;
                for (int attempt = 0; attempt < 8; attempt++) {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radius = Mathf.Lerp(distance.x, distance.y, Mathf.Pow((float)random.NextDouble(), 1.6f));
                    position = anchor + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (Clearance(position) >= distance.x) break;
                }
                // Anchored pieces (islands) float near their own stretch of course; clouds span the whole course.
                position.y = anchorIndex >= 0
                    ? anchor.y + Mathf.Lerp(height.x, height.y, (float)random.NextDouble())
                    : Mathf.Lerp(bounds.min.y + height.x, bounds.max.y + height.y, (float)random.NextDouble());
                float size = Mathf.Lerp(scale.x, scale.y, (float)random.NextDouble());
                pool[i].SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                pool[i].localScale = Vector3.one * size;
                if (faceCamera) pool[i].rotation = Quaternion.Euler(0f, Mathf.Atan2(position.x - bounds.center.x, position.z - bounds.center.z) * Mathf.Rad2Deg, 0f);
            }
        }

        /// <summary>
        /// Islands match the tier of the course point they float beside, so they are rebuilt for each
        /// course (a handful of objects once per round).
        /// </summary>
        void PlaceIslands(Bounds bounds, System.Random random) {
            foreach (var island in islands) Destroy(island.gameObject);
            islands.Clear();
            if (tierIslands == null || tierIslands.Length == 0) return;
            for (int i = 0; i < islandCount; i++) {
                int anchor = random.Next(obstacles.Count);
                var set = tierIslands[Mathf.Min((int)obstacleTiers[anchor], tierIslands.Length - 1)].prefabs;
                if (set == null || set.Length == 0) continue;
                single[0] = set[random.Next(set.Length)];
                var placed = new List<Transform>(1);
                Scatter(placed, single, 1, islandDistance, islandHeight, islandScale, bounds, random, false, anchor);
                islands.AddRange(placed);
            }
        }

        float Clearance(Vector3 position) {
            float nearest = float.MaxValue;
            foreach (var point in obstacles) {
                float dx = point.x - position.x, dz = point.z - position.z;
                nearest = Mathf.Min(nearest, dx * dx + dz * dz);
            }
            return Mathf.Sqrt(nearest);
        }
    }
}
