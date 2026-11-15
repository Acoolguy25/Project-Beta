using UnityEngine;
using UnityEngine.AI;

namespace RyanAssets.Server.ServerFeatures{
    public static class ServerPathfinding {
        public static GameObject FindClosestWithTag(Vector3 origin, string tag) {
            GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);

            GameObject closest = null;
            float shortestDistance = float.MaxValue;

            foreach (GameObject obj in objects) {
                float distance = (obj.transform.position - origin).sqrMagnitude;

                if (distance < shortestDistance) {
                    shortestDistance = distance;
                    closest = obj;
                }
            }

            return closest;
        }

        public static GameObject FindClosestReachableWithTag(Vector3 origin, string tag) {
            GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);

            GameObject closest = null;
            float shortestPath = float.MaxValue;

            NavMeshPath path = new();

            foreach (GameObject obj in objects) {
                if (!NavMesh.CalculatePath(origin, obj.transform.position, NavMesh.AllAreas, path))
                    continue;

                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float pathLength = GetPathLength(path);

                if (pathLength < shortestPath) {
                    shortestPath = pathLength;
                    closest = obj;
                }
            }

            return closest;
        }

        public static bool UpdateTarget(
            NavMeshAgent agent,
            ref GameObject currentTarget,
            string tag,
            float directChaseDistance = 15f) {
            if (currentTarget == null)
                currentTarget = FindClosestReachableWithTag(agent.transform.position, tag);

            if (currentTarget == null)
                return false;

            float sqrDistance =
                (currentTarget.transform.position - agent.transform.position).sqrMagnitude;

            // Close enough: don't waste path searches.
            if (sqrDistance <= directChaseDistance * directChaseDistance) {
                agent.SetDestination(currentTarget.transform.position);
                return true;
            }

            GameObject betterTarget =
                FindClosestReachableWithTag(agent.transform.position, tag);

            if (betterTarget != null)
                currentTarget = betterTarget;

            agent.SetDestination(currentTarget.transform.position);
            return true;
        }

        private static float GetPathLength(NavMeshPath path) {
            float length = 0f;

            for (int i = 1; i < path.corners.Length; i++)
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

            return length;
        }



        private static NavMeshTriangulation triangulation;
        private static float[] cumulativeAreas;
        private static float totalArea;
        private static bool didBuild;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            didBuild = false;
        }
        public static void Build() {
            if (didBuild)
                return;
            didBuild = true;
            triangulation = NavMesh.CalculateTriangulation();

            int triangleCount = triangulation.indices.Length / 3;
            cumulativeAreas = new float[triangleCount];

            totalArea = 0f;

            for (int i = 0; i < triangleCount; i++) {
                int t = i * 3;

                Vector3 a = triangulation.vertices[triangulation.indices[t]];
                Vector3 b = triangulation.vertices[triangulation.indices[t + 1]];
                Vector3 c = triangulation.vertices[triangulation.indices[t + 2]];

                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;

                totalArea += area;
                cumulativeAreas[i] = totalArea;
            }
        }

        public static Vector3 GetRandomPosition() {
            if (!didBuild)
                Build();

            if (totalArea <= 0f || cumulativeAreas == null || cumulativeAreas.Length == 0) {
                Debug.LogWarning("NavMeshSampler: no valid navmesh area.");
                return Vector3.zero;
            }

            float r = Random.value * totalArea;

            int tri = System.Array.BinarySearch(cumulativeAreas, r);
            if (tri < 0)
                tri = ~tri;

            tri = Mathf.Clamp(tri, 0, cumulativeAreas.Length - 1);

            int t = tri * 3;

            // Guard against malformed triangulation data from Unity
            if (t + 2 >= triangulation.indices.Length) {
                Debug.LogWarning($"NavMeshSampler: tri index {t} out of range for indices array ({triangulation.indices.Length}). Returning zero.");
                return Vector3.zero;
            }

            int ia = triangulation.indices[t];
            int ib = triangulation.indices[t + 1];
            int ic = triangulation.indices[t + 2];

            // Guard against vertex index overflow too
            if (ia >= triangulation.vertices.Length ||
                ib >= triangulation.vertices.Length ||
                ic >= triangulation.vertices.Length) {
                Debug.LogWarning("NavMeshSampler: vertex index out of range. Returning zero.");
                return Vector3.zero;
            }

            Vector3 a = triangulation.vertices[ia];
            Vector3 b = triangulation.vertices[ib];
            Vector3 c = triangulation.vertices[ic];

            float u = Mathf.Sqrt(Random.value);
            float v = Random.value;

            return (1 - u) * a + u * (1 - v) * b + u * v * c;
        }

        public static void GetRandomPositionRef(ref Vector3? pos) {
            pos ??= GetRandomPosition();
        }

        public static Vector3 GetRandomPositionOnCircle(Vector3 center, float maxRadius, float minRadius = 0f) {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(UnityEngine.Random.Range(
                minRadius * minRadius,
                maxRadius * maxRadius
            ));

            return center + new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius
            );
        }
    }
}