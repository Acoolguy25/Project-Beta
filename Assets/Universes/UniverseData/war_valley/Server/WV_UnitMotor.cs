using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// How a unit gets from A to B. <see cref="WV_UnitBrain"/> decides where to go and what to shoot;
    /// a motor knows only how this particular chassis moves. Splitting the two is what lets infantry,
    /// tanks and jets all answer the same selection and order path.
    /// </summary>
    public abstract class WV_UnitMotor : MonoBehaviour {
        protected WV_Unit Unit { get; private set; }

        /// <summary>How close counts as arrived. Aircraft need a looser threshold than ground units.</summary>
        public virtual float ArrivalTolerance => 2f;

        public virtual void Initialize(WV_Unit unit) {
            Unit = unit;
        }

        /// <summary>Path toward a world position. Called every frame while moving, so it must be cheap.</summary>
        public abstract void MoveTo(Vector3 destination);

        /// <summary>Hold position. Combat may still rotate the unit.</summary>
        public abstract void Stop();

        /// <summary>Whether the unit has reached its last destination.</summary>
        public abstract bool HasArrived(Vector3 destination);

        /// <summary>Turn to face a point without moving toward it.</summary>
        public virtual void FaceTowards(Vector3 worldPoint) {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude <= 0.01f)
                return;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(flat, Vector3.up),
                Unit.TurnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Snaps a requested destination onto something the chassis can actually reach, so an order
        /// clicked on a cliff face or inside a building does not strand the unit.
        /// </summary>
        public virtual bool TryResolveDestination(Vector3 requested, out Vector3 resolved) {
            resolved = requested;
            return true;
        }
    }

    /// <summary>
    /// Infantry, tanks, APCs and artillery. A NavMeshAgent on War Valley's baked agent type, so ground
    /// units route around the valley walls and through breached ones exactly as the wave NPCs do.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class WV_GroundUnitMotor : WV_UnitMotor {
        const float NavMeshSampleRadius = 12f;

        NavMeshAgent agent;
        Vector3 committedDestination;
        bool hasCommitted;

        public override float ArrivalTolerance => Mathf.Max(agent.stoppingDistance + 0.5f, 2.5f);

        public override void Initialize(WV_Unit unit) {
            base.Initialize(unit);
            agent = GetComponent<NavMeshAgent>();
            agent.agentTypeID = WV_Rules.NavMeshAgentTypeId;
            agent.speed = unit.MoveSpeed;
            agent.angularSpeed = unit.TurnSpeed;
            agent.acceleration = unit.MoveSpeed * 2f;
            agent.autoBraking = true;

            // A unit leaves its building on an apron that may sit just off the baked surface.
            if (!agent.isOnNavMesh && TrySampleNavMesh(transform.position, out Vector3 start))
                agent.Warp(start);
        }

        public override void MoveTo(Vector3 destination) {
            if (!agent.enabled || !agent.isOnNavMesh)
                return;

            agent.isStopped = false;
            // Re-pathing every frame to what is effectively the same point burns CPU and makes the
            // agent stutter, so only commit when the goal has genuinely moved.
            if (hasCommitted && (destination - committedDestination).sqrMagnitude < 1f)
                return;

            if (agent.SetDestination(destination)) {
                committedDestination = destination;
                hasCommitted = true;
            }
        }

        public override void Stop() {
            if (!agent.enabled || !agent.isOnNavMesh)
                return;
            agent.isStopped = true;
            agent.ResetPath();
            hasCommitted = false;
        }

        public override bool HasArrived(Vector3 destination) {
            if (!agent.enabled || !agent.isOnNavMesh)
                return true;

            Vector3 flat = transform.position - destination;
            flat.y = 0f;
            if (flat.magnitude <= ArrivalTolerance)
                return true;

            // remainingDistance reads 0 until the agent actually holds a path, so on the frame an
            // order is issued it would otherwise claim the unit had already arrived and the move
            // would be cancelled before it ever started. Only trust it once a path exists.
            return agent.hasPath && !agent.pathPending && agent.remainingDistance <= ArrivalTolerance;
        }

        public override void FaceTowards(Vector3 worldPoint) {
            // The agent already steers along its path; only turn manually while parked.
            if (agent.enabled && agent.isOnNavMesh && !agent.isStopped && agent.hasPath)
                return;
            base.FaceTowards(worldPoint);
        }

        public override bool TryResolveDestination(Vector3 requested, out Vector3 resolved) =>
            TrySampleNavMesh(requested, out resolved);

        static bool TrySampleNavMesh(Vector3 requested, out Vector3 resolved) {
            if (NavMesh.SamplePosition(requested, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas)) {
                resolved = hit.position;
                return true;
            }
            resolved = requested;
            return false;
        }
    }

    /// <summary>
    /// Choppers, jets, bombers and UAVs. Aircraft ignore the NavMesh entirely and fly a direct course
    /// at their cruise altitude, climbing over whatever terrain passes beneath them.
    /// </summary>
    public sealed class WV_AircraftMotor : WV_UnitMotor {
        const float GroundProbeHeight = 400f;
        const float GroundProbeDistance = 900f;

        float cruiseAltitude;

        public override float ArrivalTolerance => 6f;

        public override void Initialize(WV_Unit unit) {
            base.Initialize(unit);
            cruiseAltitude = WV_Rules.GetCruiseAltitude(unit.Kind);
            // Lift onto the cruise band immediately; a jet must never start its life on the dirt.
            transform.position = ApplyCruiseAltitude(transform.position);
        }

        public override void MoveTo(Vector3 destination) {
            Vector3 goal = ApplyCruiseAltitude(destination);
            Vector3 toGoal = goal - transform.position;
            if (toGoal.sqrMagnitude <= 0.0001f)
                return;

            FaceTowards(goal);

            // Fly along the nose rather than straight at the goal, so aircraft arc into a turn
            // instead of sliding sideways across the valley.
            Vector3 heading = Vector3.Slerp(transform.forward, toGoal.normalized, 0.5f).normalized;
            transform.position += heading * Mathf.Min(Unit.MoveSpeed * Time.deltaTime, toGoal.magnitude);
        }

        public override void Stop() {
            // Fixed-wing aircraft cannot truly hover, but holding station at altitude keeps orders
            // readable without simulating a landing pattern the rest of the mode has no use for.
            transform.position = ApplyCruiseAltitude(transform.position);
        }

        public override bool HasArrived(Vector3 destination) {
            Vector3 flat = transform.position - destination;
            flat.y = 0f;
            return flat.magnitude <= ArrivalTolerance;
        }

        public override void FaceTowards(Vector3 worldPoint) {
            Vector3 toPoint = worldPoint - transform.position;
            if (toPoint.sqrMagnitude <= 0.01f)
                return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(toPoint.normalized, Vector3.up),
                Unit.TurnSpeed * Time.deltaTime);
        }

        public override bool TryResolveDestination(Vector3 requested, out Vector3 resolved) {
            resolved = ApplyCruiseAltitude(requested);
            return true;
        }

        /// <summary>Holds the aircraft a fixed height above whatever it is currently flying over.</summary>
        Vector3 ApplyCruiseAltitude(Vector3 position) {
            Vector3 origin = position + Vector3.up * GroundProbeHeight;
            position.y = Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                GroundProbeDistance,
                WV_Rules.OrderGroundMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y + cruiseAltitude
                : position.y;
            return position;
        }
    }
}
