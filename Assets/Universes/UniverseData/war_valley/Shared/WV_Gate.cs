using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Declarations;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>How the owner has set a gate. Travels on the wire as a byte; append, never renumber.</summary>
    public enum WV_GateMode : byte {
        /// <summary>Opens by itself for allies who need to pass, and closes behind them.</summary>
        Auto = 0,
        /// <summary>Held open for everyone - enemies included - until the owner says otherwise.</summary>
        HeldOpen = 1,
        /// <summary>Locked shut, even for allies. Their troops and vehicles route around it.</summary>
        Locked = 2
    }

    /// <summary>
    /// A wall section that lets its own side through.
    /// <para>
    /// A gate is a <see cref="WV_DestructibleObstacle"/> - carved out of the NavMesh, bridged by a
    /// breach link only the waves may use, and broken down the same way a wall is - with a second
    /// link, the <see cref="WV_NavAreas.GatePassage"/>, that only its own side may path over. An
    /// allied troop or vehicle therefore goes through the gate only when its route actually needs
    /// to, and pathfinding still prefers the open valley when that is shorter.
    /// </para>
    /// <para>
    /// The server opens the gate: for an allied player who walks up to it, and for an allied NPC
    /// or vehicle the moment its path reaches the passage. It closes again once nobody has needed
    /// it for a moment. The owner can instead hold it open for everyone, or lock it: locked, the
    /// passage is withdrawn from the NavMesh, so allied units plan a route around, and one with no
    /// other way through stays where it is.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WV_DestructibleObstacle), typeof(StructureComponent), typeof(WV_Owned))]
    public sealed class WV_Gate : NetworkBehaviour {
        const float ScanInterval = 0.15f;
        static readonly Collider[] OverlapBuffer = new Collider[64];

        [Tooltip("The moving part: slid by the open offset while the gate stands open.")]
        [SerializeField] Transform door;
        [Tooltip("Local offset the door moves by to open. The default sinks it into the ground.")]
        [SerializeField] Vector3 doorOpenOffset = new(0f, -3f, 0f);
        [SerializeField, Min(0.01f)] float doorSeconds = 0.35f;
        [Tooltip("The link allied troops and vehicles path through. Sits on a child so it can be " +
                 "switched off without touching the wall's breach link.")]
        [SerializeField] NavMeshLink passageLink;
        [Tooltip("An allied player this close opens the gate.")]
        [SerializeField, Min(0.5f)] float playerOpenRadius = 7f;
        [Tooltip("An allied NPC whose next link is the passage opens the gate once this close, so " +
                 "the door is already down when it arrives.")]
        [SerializeField, Min(0.5f)] float npcOpenRadius = 8f;
        [Tooltip("How long the gate stays open after the last ally needed it.")]
        [SerializeField, Min(0f)] float holdOpenSeconds = 1.5f;

        readonly SyncVar<byte> mode = new((byte)WV_GateMode.Auto);
        readonly SyncVar<bool> isOpen = new();

        StructureComponent structure;
        WV_DestructibleObstacle obstacle;
        WV_Constructable constructable;
        WV_Owned owned;
        BoxCollider blocker;
        Vector3 doorClosedPosition;
        float doorOpenAmount;
        float nextScanTime;
        float holdOpenUntil;

        public WV_GateMode Mode => (WV_GateMode)mode.Value;
        public bool IsOpen => isOpen.Value;
        public WV_Owned Owned => owned;

        bool IsOperational =>
            structure != null && !structure.IsDead && (constructable == null || constructable.IsOperational);

        void Awake() {
            structure = GetComponent<StructureComponent>();
            obstacle = GetComponent<WV_DestructibleObstacle>();
            constructable = GetComponent<WV_Constructable>();
            owned = GetComponent<WV_Owned>();
            blocker = GetComponent<BoxCollider>();
            if (door != null)
                doorClosedPosition = door.localPosition;
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            ConfigurePassage();
            ApplyState();
        }

        /// <summary>
        /// Lays the passage over exactly the ground the breach link spans, so the two routes through
        /// the gate are the same crossing and differ only in who may take them.
        /// </summary>
        void ConfigurePassage() {
            NavMeshLink breach = obstacle.BreachLink;
            if (passageLink == null || breach == null) {
                Debug.LogError($"{name} is a gate with no passage link; allies will not be able to path through it.", this);
                return;
            }

            passageLink.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            passageLink.startPoint = breach.startPoint;
            passageLink.endPoint = breach.endPoint;
            passageLink.width = breach.width;
            passageLink.agentTypeID = breach.agentTypeID;
            passageLink.area = WV_NavAreas.GatePassage;
            passageLink.costModifier = -1f;
            passageLink.bidirectional = true;
            passageLink.autoUpdate = true;
        }

        void Update() {
            // A placement preview is an unspawned copy; it must not open, carve, or add links.
            if (!IsSpawned)
                return;
#if UNITY_SERVER
            if (IsServerStarted)
                UpdateServer();
#endif
            ApplyState();
            AnimateDoor();
        }

        /// <summary>
        /// Keeps the physical and navigational gate in step with its replicated state, on every
        /// build: the collider players bump into, the carve the waves path around, and the passage
        /// allies path through.
        /// </summary>
        void ApplyState() {
            bool operational = IsOperational;
            bool open = operational && isOpen.Value;

            // A trigger still answers selection and targeting raycasts that include triggers, but no
            // longer stops a player walking through.
            if (blocker != null && blocker.isTrigger != open)
                blocker.isTrigger = open;

            obstacle.SetPassable(operational && Mode == WV_GateMode.HeldOpen);

            bool passage = operational && Mode != WV_GateMode.Locked;
            if (passageLink != null && passageLink.enabled != passage)
                passageLink.enabled = passage;
        }

        void AnimateDoor() {
            if (door == null)
                return;
            float target = IsOperational && isOpen.Value ? 1f : 0f;
            if (Mathf.Approximately(doorOpenAmount, target))
                return;
            doorOpenAmount = Mathf.MoveTowards(doorOpenAmount, target, Time.deltaTime / doorSeconds);
            door.localPosition = doorClosedPosition + doorOpenOffset * doorOpenAmount;
        }

#if UNITY_SERVER
        /// <summary>Sets how the gate behaves. Callers check that the requester owns it.</summary>
        [Server]
        public void SetMode(WV_GateMode newMode) {
            mode.Value = (byte)newMode;
            holdOpenUntil = 0f;
            nextScanTime = 0f;
        }

        void UpdateServer() {
            if (Time.time < nextScanTime)
                return;
            nextScanTime = Time.time + ScanInterval;

            bool open;
            switch (Mode) {
                case WV_GateMode.HeldOpen:
                    open = true;
                    break;
                case WV_GateMode.Locked:
                    open = false;
                    break;
                default:
                    if (IsOperational && HasAlliedTraffic())
                        holdOpenUntil = Time.time + holdOpenSeconds;
                    open = Time.time < holdOpenUntil;
                    break;
            }

            open &= IsOperational;
            if (isOpen.Value != open)
                isOpen.Value = open;
        }

        /// <summary>
        /// True when an ally needs the gate: a player standing close to it, or an NPC or vehicle
        /// whose route runs through the passage and has nearly reached it. An allied NPC merely
        /// walking past does not open it.
        /// </summary>
        bool HasAlliedTraffic() {
            TeamConfig gateTeam = structure.Team;
            float radius = Mathf.Max(playerOpenRadius, npcOpenRadius);
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, OverlapBuffer, WV_Combat.TargetMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++) {
                Collider collider = OverlapBuffer[i];
                if (collider == null || collider.transform.IsChildOf(transform))
                    continue;

                IEntity entity = collider.GetComponentInParent<IEntity>();
                if (entity is not Component component || entity.IsDead || !CombatTeams.AreAllies(entity.Team, gateTeam))
                    continue;

                float distance = Vector3.Distance(transform.position, component.transform.position);
                if (component.TryGetComponent(out NavMeshAgent agent) && agent.enabled) {
                    if (distance <= npcOpenRadius && IsUsingPassage(agent))
                        return true;
                    continue;
                }

                // Anything else walking is a player: their movement is their own, so nearness is
                // the only signal there is that they mean to pass.
                if (distance <= playerOpenRadius
                    && component.TryGetComponent(out NetworkObject networkObject)
                    && networkObject.Owner.IsValid)
                    return true;
            }
            return false;
        }

        bool IsUsingPassage(NavMeshAgent agent) {
            if (passageLink == null || !agent.isOnNavMesh)
                return false;
            if (agent.isOnOffMeshLink)
                return agent.currentOffMeshLinkData.owner == passageLink;
            return agent.hasPath && agent.nextOffMeshLinkData.owner == passageLink;
        }
#endif
    }
}
