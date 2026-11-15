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
        public static int Overlap<T>(PhysicsScene scene, Bounds bounds, List<T> results, int layerMask = ~0) where T : Behaviour {
            results.Clear();
            int count = scene.OverlapBox(bounds.center, bounds.extents, Hits, Quaternion.identity, layerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++) {
                if (Hits[i] == null || !Hits[i].enabled) continue;
                T component = Hits[i].GetComponentInParent<T>();
                if (component != null && component.isActiveAndEnabled && !results.Contains(component))
                    results.Add(component);
            }
            return results.Count;
        }
    }
}
