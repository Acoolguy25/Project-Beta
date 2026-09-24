using System.Collections.Generic;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// A foot soldier that takes its orders from a commander instead of from the wave script.
    /// <para>
    /// This is the counterpart to <see cref="WV_NPC"/>: same character, same
    /// <see cref="WV_NpcCombat"/> weapon handling, but the strategic target is whatever its owner
    /// last right-clicked rather than the flag. It drives <see cref="LocalNPC"/> rather than
    /// steering the agent itself, so a troop paths, stands off, and gives ground through exactly the
    /// rules a wave NPC does.
    /// </para>
    /// <para>
    /// Ownership is recorded here on the server only. The troop's NetworkObject deliberately stays
    /// server-owned, for the same reason <see cref="WV_Owned"/> exists: handing a client the object
    /// would put it on the authority side of its own army and despawn the squad when that player
    /// dropped.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter), typeof(WV_NpcCombat))]
    public sealed class WV_TroopBrain : MonoBehaviour, WV_ICommandable {
        /// <summary>How close counts as having reached an ordered position.</summary>
        const float ArrivalTolerance = 2f;

        /// <summary>How far an idle troop may be dragged from where it was left before it walks back.</summary>
        const float DefendLeash = 10f;

        static readonly List<WV_TroopBrain> all = new();

        public static IReadOnlyList<WV_TroopBrain> All => all;

        LocalNPC localNPC;
        GameCharacter gameCharacter;
        WV_NpcCombat combat;

        WV_OrderType order = WV_OrderType.Stop;
        Vector3 orderPosition;
        IEntity orderTarget;
        Vector3 holdOrigin;

        public int OwnerClientId { get; private set; } = WV_Owned.NoOwner;
        public GameCharacter Character => gameCharacter;
        public WV_TroopKind Kind => combat != null ? combat.Kind : WV_TroopKind.Knife;
        public WV_OrderType CurrentOrder => order;

        /// <summary>
        /// Arms a freshly spawned character and puts it under a commander. The combat half is added
        /// first so its loadout is chosen before either component's first frame.
        /// </summary>
        public static WV_TroopBrain Attach(GameCharacter character, WV_TroopKind kind, int ownerClientId) {
            WV_NpcCombat.Attach(character.gameObject, kind);
            WV_TroopBrain brain = character.gameObject.AddComponent<WV_TroopBrain>();
            brain.OwnerClientId = ownerClientId;
            return brain;
        }

        public bool IsOwnedBy(int clientId) =>
            clientId != WV_Owned.NoOwner && OwnerClientId == clientId;

        void Awake() {
            localNPC = GetComponent<LocalNPC>();
            gameCharacter = GetComponent<GameCharacter>();
            combat = GetComponent<WV_NpcCombat>();
            holdOrigin = transform.position;
            orderPosition = holdOrigin;
        }

        void Start() {
            // A bare LocalNPC wanders. A troop holds the ground it was trained on until it is either
            // ordered somewhere or something hostile walks into its reach.
            localNPC.SetTargetingType(NPCTargetingType.None);
        }

        void OnEnable() => all.Add(this);

        void OnDisable() => all.Remove(this);

        // --- Orders ----------------------------------------------------------

        public void OrderMove(Vector3 destination) {
            order = WV_OrderType.Move;
            orderPosition = destination;
            orderTarget = null;
        }

        public void OrderAttackMove(Vector3 destination) {
            OrderMove(destination);
            order = WV_OrderType.AttackMove;
        }

        public void OrderAttack(IEntity target) {
            if (!WV_Combat.IsValidTarget(target, gameCharacter.GetTeam()))
                return;
            order = WV_OrderType.Attack;
            orderTarget = target;
            localNPC.TargetEntity(target);
        }

        public void OrderStop() {
            order = WV_OrderType.Stop;
            orderTarget = null;
            holdOrigin = transform.position;
            orderPosition = holdOrigin;
            localNPC.SetTargetingType(NPCTargetingType.None);
        }

        public void OrderHoldPosition() {
            OrderStop();
            order = WV_OrderType.HoldPosition;
        }

        // --- Tick ------------------------------------------------------------

        /// <summary>True while <see cref="LocalNPC"/> is running its own fight or retreat.</summary>
        bool IsEngaged =>
            localNPC.TargetingType is NPCTargetingType.Attack or NPCTargetingType.Flee;

        float MoveSpeed => localNPC.WalkSpeed * LocalNPC.WalkSpeedMultiplier;

        void Update() {
            if (gameCharacter == null || gameCharacter.IsDead) {
                enabled = false;
                return;
            }

            switch (order) {
                case WV_OrderType.Move:
                    TickMove(engageOnTheWay: false);
                    break;
                case WV_OrderType.AttackMove:
                    TickMove(engageOnTheWay: true);
                    break;
                case WV_OrderType.Attack:
                    TickAttack();
                    break;
                case WV_OrderType.HoldPosition:
                    TickDefend(leash: 0f);
                    break;
                default:
                    TickDefend(DefendLeash);
                    break;
            }
        }

        void TickMove(bool engageOnTheWay) {
            // A plain move order is a move order: the squad crossing open ground should not be
            // pulled into every skirmish it passes. Ctrl-click (attack-move) is what opts in.
            localNPC.AutomaticTargeting = engageOnTheWay;
            if (engageOnTheWay && IsEngaged)
                return;

            if (HasArrived(orderPosition)) {
                OrderStop();
                return;
            }

            localNPC.MoveTo(orderPosition, MoveSpeed);
        }

        void TickAttack() {
            if (!WV_Combat.IsValidTarget(orderTarget, gameCharacter.GetTeam())) {
                // The ordered target is dead or gone; hold the ground it was killed on.
                OrderStop();
                return;
            }

            localNPC.AutomaticTargeting = true;
            if (localNPC.CurrentAttackEntityTarget != orderTarget)
                localNPC.TargetEntity(orderTarget);
        }

        void TickDefend(float leash) {
            localNPC.AutomaticTargeting = true;
            if (IsEngaged)
                return;

            if (leash > 0f && (transform.position - holdOrigin).sqrMagnitude > leash * leash) {
                // A fight pulled it off its post. Walk back rather than standing wherever the chase
                // happened to end.
                localNPC.MoveTo(holdOrigin, MoveSpeed);
                return;
            }

            if (localNPC.TargetingType != NPCTargetingType.None)
                localNPC.SetTargetingType(NPCTargetingType.None);
        }

        bool HasArrived(Vector3 destination) {
            Vector3 offset = destination - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= ArrivalTolerance * ArrivalTolerance;
        }
    }
}
