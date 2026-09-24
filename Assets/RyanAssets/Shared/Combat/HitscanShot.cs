using System;
using UnityEngine;

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// The hitscan trace behind every instant-fire weapon in the project.
    /// <para>
    /// This is the tool gun's firing solution lifted out of <c>ToolGunShared</c> so a weapon that is
    /// not held in a character's hand - a defensive turret, a vehicle mount - fires through exactly
    /// the same rules: the same muzzle-obstruction check, the same spread cone, the same
    /// nearest-first hit resolution, and the same fall-back hit at maximum range when nothing is
    /// struck. A second implementation would drift out of step the first time one of them was tuned.
    /// </para>
    /// </summary>
    public static class HitscanShot {
        /// <summary>Everything a bullet can stop on. Cached because it is read on every shot.</summary>
        static int hitLayers = ~0;
        static bool hitLayersResolved;

        public static int HitLayers {
            get {
                if (!hitLayersResolved) {
                    hitLayers = ~LayerMask.GetMask("Ignore Raycast");
                    hitLayersResolved = true;
                }
                return hitLayers;
            }
        }

        /// <summary>
        /// Traces one shot from <paramref name="origin"/> toward <paramref name="targetPosition"/>.
        /// <para>
        /// Returns null only when the shot is degenerate (the target is the muzzle itself). Every
        /// other outcome is a hit: a real collider, the solid the muzzle is buried inside, or a
        /// point at maximum range with no collider, which is what the tracer is drawn to.
        /// </para>
        /// </summary>
        /// <param name="accuracy">0 to 1. 1 fires down the exact line to the target.</param>
        /// <param name="ignoreRoot">The shooter. Hits on it and its children are skipped.</param>
        /// <param name="muzzleRadius">
        /// Radius of the muzzle volume checked for an obstruction before the ray is cast.
        /// </param>
        /// <param name="selfRoot">
        /// The weapon itself, when it is not part of <paramref name="ignoreRoot"/>. A handheld tool
        /// passes its own root here so its casing is not mistaken for something the muzzle is
        /// buried in; a fixed emplacement whose muzzle already hangs off the shooter passes null.
        /// </param>
        public static RaycastHit? Fire(
            Vector3 origin,
            Vector3 targetPosition,
            float maxRange,
            float accuracy,
            Transform ignoreRoot,
            float muzzleRadius,
            Transform selfRoot = null) {
            if (targetPosition == origin)
                return null;

            Vector3 toTarget = (targetPosition - origin).normalized;
            if (TryGetMuzzleObstruction(origin, muzzleRadius, ignoreRoot, selfRoot, -toTarget, out RaycastHit muzzleHit))
                return muzzleHit; // The muzzle is inside a solid object; never raycast past it.

            Vector3 direction = GetSpreadDirection(origin, targetPosition, accuracy);
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, maxRange, HitLayers);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits) {
                if (hit.transform == null || hit.transform.root == null)
                    continue;
                if (ignoreRoot != null && hit.transform.IsChildOf(ignoreRoot))
                    continue;
                return hit;
            }

            return new RaycastHit {
                point = origin + direction * maxRange,
                normal = -direction,
                distance = maxRange
            };
        }

        /// <summary>
        /// Reports a solid the muzzle is already inside.
        /// <para>
        /// Raycasts do not reliably report a collider when their origin sits within it, so the muzzle
        /// volume is checked with an overlap first. The returned hit deliberately carries no
        /// transform: it stops the bullet visual at the obstruction without damaging anything.
        /// </para>
        /// </summary>
        public static bool TryGetMuzzleObstruction(
            Vector3 origin,
            float muzzleRadius,
            Transform ignoreRoot,
            Transform selfRoot,
            Vector3 fallbackNormal,
            out RaycastHit hit) {
            hit = default;
            if (muzzleRadius <= 0f)
                return false;

            Collider[] overlaps = Physics.OverlapSphere(
                origin, muzzleRadius, HitLayers, QueryTriggerInteraction.Ignore);

            foreach (Collider overlap in overlaps) {
                if (overlap == null || IsIgnored(overlap.transform, ignoreRoot, selfRoot))
                    continue;

                Vector3 closestPoint = overlap.ClosestPoint(origin);
                Vector3 normal = origin - closestPoint;
                // The muzzle can sit exactly on the surface, which gives no usable normal. Facing
                // the shot back down its own line is the only meaningful answer there.
                if (normal.sqrMagnitude < 0.0001f)
                    normal = fallbackNormal.sqrMagnitude > 0.0001f ? fallbackNormal : Vector3.up;

                hit = new RaycastHit {
                    point = closestPoint,
                    normal = normal.normalized,
                    distance = 0f
                };
                return true;
            }

            return false;
        }

        static bool IsIgnored(Transform candidate, Transform ignoreRoot, Transform selfRoot) =>
            (selfRoot != null && candidate.IsChildOf(selfRoot))
            || (ignoreRoot != null && candidate.IsChildOf(ignoreRoot));

        /// <summary>
        /// The line the bullet actually travels, scattered inside a cone that widens as accuracy drops.
        /// </summary>
        public static Vector3 GetSpreadDirection(Vector3 origin, Vector3 targetPosition, float accuracy) {
            Vector3 baseDirection = (targetPosition - origin).normalized;
            float spreadAngle = (1f - Mathf.Clamp01(accuracy)) * MaxSpreadDegrees;
            if (spreadAngle <= 0f)
                return baseDirection;

            Vector3 randomAxis = Vector3.Cross(baseDirection, UnityEngine.Random.onUnitSphere);
            if (randomAxis.sqrMagnitude < 0.0001f)
                randomAxis = Vector3.Cross(baseDirection, Vector3.up); // fallback

            Quaternion spread = Quaternion.AngleAxis(
                UnityEngine.Random.Range(0f, spreadAngle), randomAxis.normalized);
            return spread * baseDirection;
        }

        /// <summary>Cone half-angle at zero accuracy. Matches the tool gun's original spread.</summary>
        public const float MaxSpreadDegrees = 15f;
    }
}
