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
    public sealed class WV_UnitBrain : MonoBehaviour {
        /// <summary>An idle unit chases an opportunistic target this far past its detection radius before giving up.</summary>
        const float IdleLeashMultiplier = 1.4f;
        const float RetargetInterval = 0.5f;

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
            else if (go.GetComponent<NavMeshAgent>() != null)
                motor = go.AddComponent<WV_GroundUnitMotor>();
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
            currentTarget = WV_Combat.FindNearestEnemy(
                transform.position, unit.DetectionRadius, unit.Team, preferCharacters: true, ignoreRoot: transform);
        }

        /// <summary>Keeps an idle unit from being walked across the map by a fleeing enemy.</summary>
        bool HasBrokenLeash(IEntity target) {
            if (order is WV_OrderType.Attack or WV_OrderType.AttackMove)
                return false;
            return WV_Combat.DistanceTo(holdOrigin, target) > unit.DetectionRadius * IdleLeashMultiplier;
        }

        void TickMove(bool engageOnTheWay) {
            if (engageOnTheWay && currentTarget != null && !InAttackRange(currentTarget)) {
                // Close on the threat but keep the original destination, so the group resumes its
                // advance once the fight in front of it is finished.
                if (WV_Combat.TryGetPosition(currentTarget, out Vector3 targetPosition)) {
                    MoveToward(targetPosition);
                    return;
                }
            }

            if (engageOnTheWay && currentTarget != null && InAttackRange(currentTarget)) {
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

            if (InAttackRange(currentTarget)) {
                motor.Stop();
                FaceCurrentTarget();
                return;
            }

            MoveToward(targetPosition);
        }

        void TickStationary(bool allowChase) {
            if (currentTarget == null) {
                motor.Stop();
                return;
            }

            if (InAttackRange(currentTarget)) {
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

        bool InAttackRange(IEntity target) =>
            WV_Combat.DistanceTo(transform.position, target) <= unit.AttackRange;

        void TryFire() {
            if (currentTarget == null || !InAttackRange(currentTarget))
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
