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
        const float LookAheadSeconds = 2f;

        float cruiseAltitude;
        float clearanceRadius;
        int flightSurfaceMask;

        public override float ArrivalTolerance => 6f;

        public override void Initialize(WV_Unit unit) {
            base.Initialize(unit);
            cruiseAltitude = WV_Rules.GetCruiseAltitude(unit.Kind);
            flightSurfaceMask = LayerMask.GetMask("Default", "Ground", "Structure");
            // The footprint comes from the authored chassis, not a point ray that can miss a roof
            // under the edge of the aircraft. Characters and other aircraft never affect altitude.
            CapsuleCollider body = GetComponent<CapsuleCollider>();
            clearanceRadius = body != null
                ? body.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z))
                : 1f;
            // Lift onto the cruise band immediately; a jet must never start its life on the dirt.
            Vector3 position = transform.position;
            position.y = GetFlightAltitude(position, transform.forward, 0f);
            transform.position = position;
        }

        public override void MoveTo(Vector3 destination) {
            Vector3 position = transform.position;
            Vector3 toGoal = destination - position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude <= 0.0001f) {
                Stop();
                return;
            }

            FaceTowards(destination);

            // Fly along the nose rather than straight at the goal, so aircraft arc into a turn
            // instead of sliding sideways across the valley.
            Vector3 forward = transform.forward;
            forward.y = 0f;
            Vector3 heading = Vector3.Slerp(forward.normalized, toGoal.normalized, 0.5f).normalized;
            float distance = toGoal.magnitude;
            float lookAhead = Mathf.Min(Unit.MoveSpeed * LookAheadSeconds, distance);
            float altitude = GetFlightAltitude(position, heading, lookAhead);
            Vector3 next = position + heading * Mathf.Min(Unit.MoveSpeed * Time.deltaTime, distance);
            next.y = MoveAltitude(position.y, altitude);

            // A sudden tall obstacle or a large frame must not let horizontal travel outrun the
            // climb. Pause at its edge until the aircraft has gained the required clearance.
            if (next.y < GetFlightAltitude(next, heading, 0f)) {
                next.x = position.x;
                next.z = position.z;
            }
            transform.position = next;
        }

        public override void Stop() {
            // Fixed-wing aircraft cannot truly hover, but holding station at altitude keeps orders
            // readable without simulating a landing pattern the rest of the mode has no use for.
            Vector3 position = transform.position;
            position.y = MoveAltitude(position.y, GetFlightAltitude(position, transform.forward, 0f));
            transform.position = position;
        }

        public override bool HasArrived(Vector3 destination) {
            Vector3 flat = transform.position - destination;
            flat.y = 0f;
            return flat.magnitude <= ArrivalTolerance;
        }

        public override void FaceTowards(Vector3 worldPoint) {
            // A ground target changes heading, not flight pitch. Pitching at an enemy used to
            // pull the next movement step downward, then Stop snapped the aircraft upward again.
            base.FaceTowards(worldPoint);
        }

        public override bool TryResolveDestination(Vector3 requested, out Vector3 resolved) {
            // Orders specify a map position. Altitude is determined along the actual route rather
            // than from distant terrain beneath the destination.
            resolved = requested;
            return true;
        }

        float MoveAltitude(float current, float desired) => Mathf.MoveTowards(
            current, desired, Unit.MoveSpeed * (desired > current ? 1f : 0.5f) * Time.deltaTime);

        /// <summary>Finds clearance over the full footprint and the continuous corridor ahead.</summary>
        float GetFlightAltitude(Vector3 position, Vector3 heading, float lookAhead) {
            heading.y = 0f;
            Quaternion orientation = heading.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(heading, Vector3.up)
                : Quaternion.identity;
            Vector3 origin = position + heading.normalized * (lookAhead * 0.5f)
                + Vector3.up * GroundProbeHeight;
            return Physics.BoxCast(
                origin,
                new Vector3(clearanceRadius, 0.05f, clearanceRadius + lookAhead * 0.5f),
                Vector3.down,
                out RaycastHit hit,
                orientation,
                GroundProbeDistance,
                flightSurfaceMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y + cruiseAltitude
                : position.y;
        }
    }
}
