using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>Server-side overlap queries for authoritative platformer rules.</summary>
    public static class PlatformerQuery {
        static readonly Collider[] Hits = new Collider[64];

        /// <summary>
        /// Collects the enabled <typeparamref name="T"/> components on colliders (solid or trigger)
        /// that overlap <paramref name="bounds"/> in <paramref name="scene"/>.
        /// </summary>
        public static int Overlap<T>(PhysicsScene scene, Bounds bounds, List<T> results, int layerMask = ~0) where T : Behaviour =>
            Overlap(scene, bounds.center, bounds.extents, Quaternion.identity, results, layerMask);

        /// <summary>
        /// Like <see cref="Overlap{T}(PhysicsScene, Bounds, List{T}, int)"/> for an oriented box, which stays
        /// tight around a character that is tilted or standing on a wall (its axis-aligned bounds would not).
        /// </summary>
        public static int Overlap<T>(PhysicsScene scene, Vector3 center, Vector3 halfExtents, Quaternion orientation,
            List<T> results, int layerMask = ~0) where T : Behaviour {
            results.Clear();
            int count = scene.OverlapBox(center, halfExtents, Hits, orientation, layerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++) {
                if (Hits[i] == null || !Hits[i].enabled) continue;
                T component = Hits[i].GetComponentInParent<T>();
                if (component != null && component.isActiveAndEnabled && !results.Contains(component))
                    results.Add(component);
            }
            return results.Count;
        }

        /// <summary>
        /// Whether two oriented boxes (centre, half extents, rotation) overlap: an exact separating-axis
        /// test, so it works on colliders that are switched off (a laser judged at another moment).
        /// </summary>
        public static bool BoxesOverlap(Vector3 centerA, Vector3 halfA, Quaternion rotationA,
            Vector3 centerB, Vector3 halfB, Quaternion rotationB) {
            Vector3 a0 = rotationA * Vector3.right, a1 = rotationA * Vector3.up, a2 = rotationA * Vector3.forward;
            Vector3 b0 = rotationB * Vector3.right, b1 = rotationB * Vector3.up, b2 = rotationB * Vector3.forward;
            Vector3 d = centerB - centerA;
            return !Separates(a0, d, halfA, halfB, a0, a1, a2, b0, b1, b2) && !Separates(a1, d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(a2, d, halfA, halfB, a0, a1, a2, b0, b1, b2) && !Separates(b0, d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(b1, d, halfA, halfB, a0, a1, a2, b0, b1, b2) && !Separates(b2, d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a0, b0), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a0, b1), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a0, b2), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a1, b0), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a1, b1), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a1, b2), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a2, b0), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a2, b1), d, halfA, halfB, a0, a1, a2, b0, b1, b2)
                && !Separates(Vector3.Cross(a2, b2), d, halfA, halfB, a0, a1, a2, b0, b1, b2);
        }

        static bool Separates(Vector3 axis, Vector3 d, Vector3 halfA, Vector3 halfB,
            Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2) {
            if (axis.sqrMagnitude < 1e-8f) return false; // parallel edges: covered by the face axes
            float ra = halfA.x * Mathf.Abs(Vector3.Dot(a0, axis)) + halfA.y * Mathf.Abs(Vector3.Dot(a1, axis)) + halfA.z * Mathf.Abs(Vector3.Dot(a2, axis));
            float rb = halfB.x * Mathf.Abs(Vector3.Dot(b0, axis)) + halfB.y * Mathf.Abs(Vector3.Dot(b1, axis)) + halfB.z * Mathf.Abs(Vector3.Dot(b2, axis));
            return Mathf.Abs(Vector3.Dot(d, axis)) > ra + rb;
        }

        /// <summary>
        /// The oriented box of <paramref name="collider"/> grown by <paramref name="skin"/>: exact for a
        /// <see cref="BoxCollider"/>, its world bounds for any other shape.
        /// </summary>
        public static void GetBox(Collider collider, float skin, out Vector3 center, out Vector3 halfExtents, out Quaternion orientation) {
            if (collider is BoxCollider box) {
                Transform t = box.transform;
                center = t.TransformPoint(box.center);
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                halfExtents = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f + Vector3.one * skin;
                orientation = t.rotation;
                return;
            }
            Bounds bounds = collider.bounds;
            center = bounds.center;
            halfExtents = bounds.extents + Vector3.one * skin;
            orientation = Quaternion.identity;
        }
    }
}
