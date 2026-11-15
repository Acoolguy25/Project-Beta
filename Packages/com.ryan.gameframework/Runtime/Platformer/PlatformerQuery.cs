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
