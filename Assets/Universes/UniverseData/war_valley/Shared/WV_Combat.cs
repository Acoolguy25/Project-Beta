using System.Collections.Generic;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
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

        /// <summary>Layers that can hold a damageable entity.</summary>
        public static int TargetMask => LayerMask.GetMask("Character", "LocalCharacter", "Structure");

        public static bool AreEnemies(TeamConfig attacker, TeamConfig target) {
            if (attacker == null || target == null)
                return false;
            if (attacker.realTeam == target.realTeam)
                return false;

            Dictionary<TeamColor, HashSet<TeamColor>> enemies = SharedGlobalEvents.TeamEnemies;
            // Before a runner publishes its team table, treat nothing as hostile rather than
            // letting freshly spawned units open fire on their own side.
            return enemies != null
                && enemies.TryGetValue(attacker.realTeam, out HashSet<TeamColor> hostile)
                && hostile.Contains(target.realTeam);
        }

        public static bool IsValidTarget(IEntity target, TeamConfig attackerTeam) =>
            target is Component component
            && component != null
            && !target.IsDead
            && AreEnemies(attackerTeam, target.Team);

        /// <summary>
        /// Nearest hostile entity within <paramref name="radius"/>, or null.
        /// <paramref name="preferCharacters"/> breaks ties toward living threats so a tank engages
        /// infantry standing next to a wall rather than chewing on the wall.
        /// </summary>
        public static IEntity FindNearestEnemy(
            Vector3 origin,
            float radius,
            TeamConfig attackerTeam,
            bool preferCharacters = true,
            Transform ignoreRoot = null) {
            if (radius <= 0f)
                return null;

            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, OverlapBuffer, TargetMask, QueryTriggerInteraction.Ignore);

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
                if (!IsValidTarget(candidate, attackerTeam))
                    continue;

                Transform candidateTransform = ((Component)candidate).transform;
                float distance = Vector3.Distance(origin, candidateTransform.position);
                float score = preferCharacters && candidate is StructureComponent ? distance + radius : distance;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = candidate;
            }

            return best;
        }

        /// <summary>Straight-line distance between two entity roots, ignoring collider shape.</summary>
        public static float DistanceTo(Vector3 origin, IEntity target) =>
            target is Component component && component != null
                ? Vector3.Distance(origin, component.transform.position)
                : float.MaxValue;

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
