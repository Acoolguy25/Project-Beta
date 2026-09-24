using System.Collections.Generic;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Server-side command and combat policy for one <see cref="WV_Unit"/>.
    /// <para>
    /// Attached at spawn rather than serialized on the prefab, the same way <c>WV_NPC</c> is, because
    /// the war_valley Server assembly is compiled only for <c>UNITY_SERVER</c> and a client build
    /// would otherwise load a prefab with a missing script.
    /// </para>
    /// </summary>
    public sealed class WV_UnitBrain : MonoBehaviour, WV_ICommandable {
        /// <summary>An idle unit chases an opportunistic target this far past its detection radius before giving up.</summary>
        const float IdleLeashMultiplier = 1.4f;
        const float RetargetInterval = 0.5f;
        /// <summary>How long a line-of-sight answer is trusted before the ray is cast again.</summary>
        const float LineOfSightInterval = 0.2f;

        static readonly List<WV_UnitBrain> all = new();

        public static IReadOnlyList<WV_UnitBrain> All => all;

        WV_Unit unit;
        WV_UnitMotor motor;
        WV_UnitAnimation unitAnimation;

        WV_OrderType order = WV_OrderType.Stop;
        Vector3 orderPosition;
        IEntity orderTarget;
        Vector3 holdOrigin;

        IEntity currentTarget;
        float nextFireTime;
        float nextRetargetTime;
        IEntity sightTarget;
        bool hasSight;
        float nextSightTime;

        public WV_Unit Unit => unit;
        public WV_OrderType CurrentOrder => order;

        /// <summary>
        /// Builds the right motor for this chassis and starts the brain. Called once, immediately
        /// after the unit is network-spawned.
        /// </summary>
        public static WV_UnitBrain Attach(WV_Unit unit) {
            GameObject go = unit.gameObject;
            WV_UnitMotor motor;
            if (unit.IsAircraft)
                motor = go.AddComponent<WV_AircraftMotor>();
            else if (go.TryGetComponent(out NavMeshAgent agent)) {
                // Gates open for their own side's vehicles; walls never do.
                WV_NavAreas.Apply(agent, unit.Team);
                motor = go.AddComponent<WV_GroundUnitMotor>();
            }
            else {
                Debug.LogError(
                    $"{go.name} ({unit.Kind}) is a ground unit with no NavMeshAgent, so it cannot move. " +
                    "Add one to the unit prefab.");
                return null;
            }

            motor.Initialize(unit);
            WV_UnitBrain brain = go.AddComponent<WV_UnitBrain>();
            brain.Initialize(unit, motor);
            return brain;
        }

        void Initialize(WV_Unit ownerUnit, WV_UnitMotor ownerMotor) {
            unit = ownerUnit;
            motor = ownerMotor;
            unitAnimation = GetComponent<WV_UnitAnimation>();
            holdOrigin = transform.position;
            orderPosition = holdOrigin;
        }

        void OnEnable() => all.Add(this);

        void OnDisable() => all.Remove(this);

        // --- Orders ----------------------------------------------------------

        public void OrderMove(Vector3 destination) {
            order = WV_OrderType.Move;
            orderTarget = null;
            currentTarget = null;
            orderPosition = motor.TryResolveDestination(destination, out Vector3 resolved) ? resolved : destination;
        }

        public void OrderAttackMove(Vector3 destination) {
            OrderMove(destination);
            order = WV_OrderType.AttackMove;
        }

        public void OrderAttack(IEntity target) {
            if (!WV_Combat.IsValidTarget(target, unit.Team))
                return;
            order = WV_OrderType.Attack;
            orderTarget = target;
            currentTarget = target;
        }

        public void OrderStop() {
            order = WV_OrderType.Stop;
            orderTarget = null;
            currentTarget = null;
            holdOrigin = transform.position;
            motor.Stop();
        }

        public void OrderHoldPosition() {
            OrderStop();
            order = WV_OrderType.HoldPosition;
        }

        // --- Tick ------------------------------------------------------------

        void Update() {
            if (unit == null || unit.IsDead) {
                enabled = false;
                return;
            }

            UpdateTargeting();

            switch (order) {
                case WV_OrderType.Move:
                    TickMove(engageOnTheWay: false);
                    break;
                case WV_OrderType.AttackMove:
                    TickMove(engageOnTheWay: true);
                    break;
                case WV_OrderType.Attack:
                    TickAttackOrder();
                    break;
                case WV_OrderType.HoldPosition:
                    TickStationary(allowChase: false);
                    break;
                default:
                    TickStationary(allowChase: true);
                    break;
            }

            TryFire();
        }

        void UpdateTargeting() {
            // An explicit attack order outranks opportunistic acquisition until the target dies.
            if (order == WV_OrderType.Attack) {
                if (WV_Combat.IsValidTarget(orderTarget, unit.Team)) {
                    currentTarget = orderTarget;
                    return;
                }
                // The ordered target is gone; fall back to holding the ground it died on.
                orderTarget = null;
                order = WV_OrderType.Stop;
                holdOrigin = transform.position;
            }

            if (order == WV_OrderType.Move) {
                currentTarget = null;
                return;
            }

            if (WV_Combat.IsValidTarget(currentTarget, unit.Team) && !HasBrokenLeash(currentTarget))
                return;

            float now = NetworkHelper.ServerTime;
            if (now < nextRetargetTime)
                return;

            nextRetargetTime = now + RetargetInterval;
            // Acquisition is measured the same way engagement is, so an aircraft never picks up a
            // target it then turns out to be unable to shoot, or vice versa.
            currentTarget = WV_Combat.FindNearestEnemy(
                transform.position, unit.DetectionRadius, unit.Team, preferCharacters: true,
                ignoreRoot: transform, verticalReach: EngagementAltitude);
        }

        /// <summary>Keeps an idle unit from being walked across the map by a fleeing enemy.</summary>
        bool HasBrokenLeash(IEntity target) {
            if (order is WV_OrderType.Attack or WV_OrderType.AttackMove)
                return false;
            // Measured the same way acquisition is. Straight-line here would let an aircraft pick up
            // a target at the edge of its reach and then immediately drop it for being out of leash,
            // retargeting the same enemy every interval without ever engaging it.
            float distance = unit.IsAircraft
                ? WV_Combat.FlatDistanceTo(holdOrigin, target)
                : WV_Combat.DistanceTo(holdOrigin, target);
            return distance > unit.DetectionRadius * IdleLeashMultiplier;
        }

        void TickMove(bool engageOnTheWay) {
            if (engageOnTheWay && currentTarget != null && !CanEngage(currentTarget)) {
                // Close on the threat but keep the original destination, so the group resumes its
                // advance once the fight in front of it is finished.
                if (WV_Combat.TryGetPosition(currentTarget, out Vector3 targetPosition)) {
                    MoveToward(targetPosition);
                    return;
                }
            }

            if (engageOnTheWay && currentTarget != null && CanEngage(currentTarget)) {
                motor.Stop();
                FaceCurrentTarget();
                return;
            }

            if (motor.HasArrived(orderPosition)) {
                order = WV_OrderType.Stop;
                holdOrigin = transform.position;
                motor.Stop();
                return;
            }

            motor.MoveTo(orderPosition);
        }

        void TickAttackOrder() {
            if (currentTarget == null || !WV_Combat.TryGetPosition(currentTarget, out Vector3 targetPosition))
                return;

            if (CanEngage(currentTarget)) {
                motor.Stop();
                FaceCurrentTarget();
                return;
            }

            // Out of range, or in range with a wall in the way: the NavMesh route closes on the
            // target around the wall, and the shot is taken once the line is clear.
            MoveToward(targetPosition);
        }

        void TickStationary(bool allowChase) {
            if (currentTarget == null) {
                motor.Stop();
                return;
            }

            if (CanEngage(currentTarget)) {
                motor.Stop();
                FaceCurrentTarget();
                return;
            }

            if (!allowChase) {
                motor.Stop();
                FaceCurrentTarget();
                return;
            }

            if (WV_Combat.TryGetPosition(currentTarget, out Vector3 targetPosition))
                MoveToward(targetPosition);
        }

        void MoveToward(Vector3 worldPosition) {
            if (motor.TryResolveDestination(worldPosition, out Vector3 resolved))
                motor.MoveTo(resolved);
            else
                motor.MoveTo(worldPosition);
        }

        void FaceCurrentTarget() {
            if (WV_Combat.TryGetPosition(currentTarget, out Vector3 targetPosition))
                motor.FaceTowards(targetPosition);
        }

        /// <summary>
        /// How high this chassis sits above what it shoots at, or 0 for anything on the ground.
        /// <para>
        /// An aircraft cannot descend to close the distance - its cruise altitude is held by the
        /// motor - so height is not range it can spend. Measuring engagement straight-line instead
        /// charged it that height twice over and left three of the four aircraft unable to fire at
        /// all: a jet at 34m carries a 28m gun, so nothing on the ground was ever in range of it,
        /// and it would circle directly above a target forever without shooting.
        /// </para>
        /// </summary>
        float EngagementAltitude => unit.IsAircraft ? WV_Rules.GetCruiseAltitude(unit.Kind) : 0f;

        bool InAttackRange(IEntity target) =>
            (unit.IsAircraft
                ? WV_Combat.FlatDistanceTo(transform.position, target)
                : WV_Combat.DistanceTo(transform.position, target)) <= unit.AttackRange;

        /// <summary>
        /// Ground weapons fire straight, so they cannot shoot through a wall, fence, or closed gate.
        /// Aircraft fire down over them, and artillery lobs its shells over them.
        /// </summary>
        bool NeedsLineOfSight => !unit.IsAircraft && unit.Kind != WV_UnitKind.Artillery;

        /// <summary>In range, and - for a ground weapon - with a clear line to the target.</summary>
        bool CanEngage(IEntity target) {
            if (!InAttackRange(target))
                return false;
            if (!NeedsLineOfSight)
                return true;

            float now = NetworkHelper.ServerTime;
            if (target != sightTarget || now >= nextSightTime) {
                sightTarget = target;
                nextSightTime = now + LineOfSightInterval;
                hasSight = WV_Combat.HasLineOfSight(unit.WeaponMuzzle.position, target, transform);
            }
            return hasSight;
        }

        void TryFire() {
            if (currentTarget == null || !CanEngage(currentTarget))
                return;

            float now = NetworkHelper.ServerTime;
            if (now < nextFireTime)
                return;

            nextFireTime = now + unit.AttackCooldown;
            WV_Combat.DealDamage(currentTarget, unit.AttackDamage, unit.AttackDamageType, unit);

            // Presentation only; the hit above is already authoritative.
            if (unitAnimation != null)
                unitAnimation.PlayAttack();
        }
    }
}
