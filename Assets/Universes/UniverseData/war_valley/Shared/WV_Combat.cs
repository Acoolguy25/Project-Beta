using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Target acquisition shared by unit brains, defensive structures, and the wave NPCs.
    /// <para>
    /// Everything War Valley can shoot at - player units, structures, and hostile characters - is an
    /// <see cref="IEntity"/> with a <see cref="HealthComponent"/>, so one physics-based sweep serves
    /// all three rather than each system growing its own perception rules.
    /// </para>
    /// </summary>
    public static class WV_Combat {
        // Sized for the densest engagement the map produces. Reused across every query so
        // acquisition never allocates, which matters because this runs on a retarget cadence for
        // every unit and turret on the field.
        static readonly Collider[] OverlapBuffer = new Collider[128];
        static readonly RaycastHit[] SightBuffer = new RaycastHit[16];
        static int structureMask = -1;

        static int StructureMask => structureMask >= 0 ? structureMask : structureMask = LayerMask.GetMask("Structure");

        /// <summary>Layers that can hold a damageable entity.</summary>
        public static int TargetMask => LayerMask.GetMask("Character", "LocalCharacter", "Structure");

        /// <summary>
        /// Forwards to the shared hostility rule, so a War Valley unit, a shared explosion, and any
        /// other game all answer this question the same way.
        /// </summary>
        public static bool AreEnemies(TeamConfig attacker, TeamConfig target) =>
            CombatTeams.AreEnemies(attacker, target);

        /// <summary>
        /// A living, hostile entity that can currently be hurt. Something invulnerable is not worth
        /// a shot or an order: it is skipped exactly like something already dead.
        /// </summary>
        public static bool IsValidTarget(IEntity target, TeamConfig attackerTeam) =>
            target is Component component
            && component != null
            && !target.IsDead
            && !target.IsEffectActive(CharacterEffect.Invul)
            && AreEnemies(attackerTeam, target.Team);

        /// <summary>
        /// Nearest hostile entity within <paramref name="radius"/>, or null.
        /// <paramref name="preferCharacters"/> breaks ties toward living threats so a tank engages
        /// infantry standing next to a wall rather than chewing on the wall.
        /// </summary>
        /// <param name="verticalReach">
        /// Height the searcher sits above what it is looking for, for an attacker whose altitude is a
        /// fixed property of its chassis rather than something it can close. Pass it and the search
        /// becomes a cylinder of <paramref name="radius"/> rather than a sphere: an aircraft looks
        /// the same distance across the ground whether it is a chopper or a jet, instead of a jet
        /// spending most of its detection radius on the empty air underneath itself.
        /// </param>
        public static IEntity FindNearestEnemy(
            Vector3 origin,
            float radius,
            TeamConfig attackerTeam,
            bool preferCharacters = true,
            Transform ignoreRoot = null,
            float verticalReach = 0f) {
            if (radius <= 0f)
                return null;

            // The broad phase still has to be a sphere, so it is widened to enclose the cylinder the
            // caller actually wants; the flat test below trims the corners back off.
            float queryRadius = verticalReach > 0f
                ? Mathf.Sqrt(radius * radius + verticalReach * verticalReach)
                : radius;

            int count = Physics.OverlapSphereNonAlloc(
                origin, queryRadius, OverlapBuffer, TargetMask, QueryTriggerInteraction.Ignore);

            IEntity best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < count; i++) {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;
                if (ignoreRoot != null && collider.transform.IsChildOf(ignoreRoot))
                    continue;

                // Colliders usually hang off a child of the networked root, so resolve upward.
                IEntity candidate = collider.GetComponentInParent<IEntity>();
                // Anything behind an enemy shield cannot be hurt from here, so it is not a target;
                // the shield is, and is offered below.
                if (!IsValidTarget(candidate, attackerTeam)
                    || WV_ShieldBarrier.Protects(candidate, origin, attackerTeam))
                    continue;

                Transform candidateTransform = ((Component)candidate).transform;
                float distance;
                if (verticalReach > 0f) {
                    distance = FlatDistance(origin, candidateTransform.position);
                    // Trims the widened sphere back to the cylinder that was asked for. Only the
                    // flattened path does this: a plain sphere query is already bounded by the
                    // broad phase, and re-testing it here would additionally reject a target whose
                    // collider overlapped while its root sat just outside the radius.
                    if (distance > radius)
                        continue;
                } else {
                    distance = Vector3.Distance(origin, candidateTransform.position);
                }
                float score = preferCharacters && candidate is StructureComponent ? distance + radius : distance;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = candidate;
            }

            // A shield's hit volume is a trigger that physics queries skip, and its centre is far
            // inside it, so shields are measured to their edge instead: the part an attacker can reach.
            foreach (WV_ShieldBarrier shield in WV_ShieldBarrier.All) {
                if (!shield.IsUp || !AreEnemies(attackerTeam, shield.Team))
                    continue;
                float distance = Mathf.Max(0f, shield.DistanceToEdge(origin));
                if (distance > radius)
                    continue;
                float score = preferCharacters ? distance + radius : distance;
                if (score >= bestScore)
                    continue;
                bestScore = score;
                best = shield;
            }

            return best;
        }

        /// <summary>
        /// Whether a shot from <paramref name="origin"/> reaches <paramref name="target"/> without a
        /// wall, fence, or closed gate in the way. Only those block: a ground weapon cannot fire
        /// through a wall, but it can fire past a building's corner, and an aircraft ignores this
        /// entirely because it shoots down over walls.
        /// </summary>
        /// <param name="ignoreRoot">The shooter, whose own colliders never block its shot.</param>
        public static bool HasLineOfSight(Vector3 origin, IEntity target, Transform ignoreRoot = null) {
            // A shield is shot at its surface, which is on this side of anything it protects.
            if (target is WV_ShieldBarrier || !TryGetAimPoint(target, out Vector3 aimPoint))
                return true;

            Vector3 toTarget = aimPoint - origin;
            float distance = toTarget.magnitude;
            if (distance < 0.01f)
                return true;

            int count = Physics.RaycastNonAlloc(
                origin, toTarget / distance, SightBuffer, distance, StructureMask,
                QueryTriggerInteraction.Ignore);
            Transform targetRoot = ((Component)target).transform;
            for (int i = 0; i < count; i++) {
                Transform hit = SightBuffer[i].transform;
                if (hit == null || hit.IsChildOf(targetRoot) || (ignoreRoot != null && hit.IsChildOf(ignoreRoot)))
                    continue;
                if (hit.GetComponentInParent<WV_DestructibleObstacle>() != null)
                    return false;
            }
            return true;
        }

        /// <summary>Straight-line distance between two entity roots, ignoring collider shape.</summary>
        public static float DistanceTo(Vector3 origin, IEntity target) =>
            target is WV_ShieldBarrier shield && shield != null
                ? Mathf.Max(0f, shield.DistanceToEdge(origin))
                : target is Component component && component != null
                    ? Vector3.Distance(origin, component.transform.position)
                    : float.MaxValue;

        /// <summary>
        /// Distance across the ground, ignoring height.
        /// <para>
        /// This is how an aircraft measures its reach. Its cruise altitude is fixed by the chassis,
        /// so a straight-line check spends the whole weapon range climbing down to the target and
        /// leaves nothing to shoot with: a jet cruising at 34m with a 28m gun is never within 28m of
        /// anything on the ground, even parked directly above it.
        /// </para>
        /// </summary>
        public static float FlatDistanceTo(Vector3 origin, IEntity target) =>
            target is WV_ShieldBarrier shield && shield != null
                ? Mathf.Max(0f, shield.DistanceToEdge(origin))
                : target is Component component && component != null
                    ? FlatDistance(origin, component.transform.position)
                    : float.MaxValue;

        static float FlatDistance(Vector3 a, Vector3 b) {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Where a shooter should aim to actually hit this target.
        /// <para>
        /// An entity's transform sits at its base - a character's feet, a building's footprint - so a
        /// shot fired at it from any distance travels into the ground short of the target. Aiming at
        /// the middle of the body the target actually occupies is what makes a hitscan weapon
        /// connect.
        /// </para>
        /// </summary>
        public static bool TryGetAimPoint(IEntity target, out Vector3 point) {
            point = default;
            if (target is not Component component || component == null)
                return false;

            Collider collider = component.GetComponent<Collider>();
            if (collider == null)
                collider = component.GetComponentInChildren<Collider>();

            point = collider != null
                ? collider.bounds.center
                : component.transform.position + Vector3.up;
            return true;
        }

        public static bool TryGetPosition(IEntity target, out Vector3 position) {
            if (target is Component component && component != null) {
                position = component.transform.position;
                return true;
            }
            position = default;
            return false;
        }

#if UNITY_SERVER
        /// <summary>Applies a hit and reports whether the target actually took it.</summary>
        public static bool DealDamage(IEntity target, long damage, DamageType damageType, IEntity attacker) {
            if (target == null || target.IsDead || damage <= 0)
                return false;
            return target.TakeDamage(damage, damageType, attacker);
        }
#endif
    }
}
