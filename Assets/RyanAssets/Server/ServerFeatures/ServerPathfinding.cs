using UnityEngine;
using UnityEngine.AI;

namespace RyanAssets.Server.ServerFeatures{
    public static class ServerPathfinding
    {
        public static GameObject FindClosestWithTag(Vector3 origin, string tag)
        {
            GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);

            GameObject closest = null;
            float shortestDistance = float.MaxValue;

            foreach (GameObject obj in objects)
            {
                float distance = (obj.transform.position - origin).sqrMagnitude;

                if (distance < shortestDistance)
                {
                    shortestDistance = distance;
                    closest = obj;
                }
            }

            return closest;
        }

        public static GameObject FindClosestReachableWithTag(Vector3 origin, string tag)
        {
            GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);

            GameObject closest = null;
            float shortestPath = float.MaxValue;

            NavMeshPath path = new();

            foreach (GameObject obj in objects)
            {
                if (!NavMesh.CalculatePath(origin, obj.transform.position, NavMesh.AllAreas, path))
                    continue;

                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float pathLength = GetPathLength(path);

                if (pathLength < shortestPath)
                {
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
            float directChaseDistance = 15f)
        {
            if (currentTarget == null)
                currentTarget = FindClosestReachableWithTag(agent.transform.position, tag);

            if (currentTarget == null)
                return false;

            float sqrDistance =
                (currentTarget.transform.position - agent.transform.position).sqrMagnitude;

            // Close enough: don't waste path searches.
            if (sqrDistance <= directChaseDistance * directChaseDistance)
            {
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

        private static float GetPathLength(NavMeshPath path)
        {
            float length = 0f;

            for (int i = 1; i < path.corners.Length; i++)
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

            return length;
        }
    }
}