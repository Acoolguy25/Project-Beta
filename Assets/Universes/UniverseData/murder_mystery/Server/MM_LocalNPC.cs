using System;
using System.Collections.Generic;
using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// The murder mystery NPC body: townsfolk run from murderers, murderers carry the knife, stab what
    /// LocalNPC brings them to and, in Classic, sometimes throw it at a target further off. The game mode
    /// plugs its rules in through the hooks below (when kills are allowed, how the knife is issued, how a
    /// throw is made).
    /// </summary>
    public class MM_LocalNPC : NetworkBehaviour {
        // How long the knife stays in hand after a strike before it is put away again.
        const float UnequipAfterAttackSeconds = 1f;

        LocalNPC localNPC;
        GameCharacter gameCharacter;
        CharacterAnimator characterAnimator;
        Animator animator;

        ToolBaseShared sharedWeapon;
        GameCharacter pendingAttackTarget;
        GameCharacter pendingThrowTarget;
        float throwAt;
        float lastAttack = float.MinValue;
        bool holstered;

        /// <summary>
        /// Asked before every attack. When it says no (Classic grace period or kill cooldown) the murderer
        /// holsters: it stops hunting and wanders like a townsperson until it may kill again. Null means
        /// the game mode puts no limit on kills.
        /// </summary>
        public Func<bool> KillAllowed;

        /// <summary>Issues this murderer's knife. Null spawns a plain dagger.</summary>
        public Func<GameCharacter, ToolBaseShared> IssueKnife;

        /// <summary>Throws this NPC's knife at a point (returns false if the game mode refused). Null = this NPC never throws.</summary>
        public Func<GameCharacter, Vector3, bool> ThrowKnife;

        /// <summary>Chance per second to throw at the current target, the distances it throws from, and the wind-up.</summary>
        public float ThrowChancePerSecond;
        /// <summary>Characters this murderer cannot aim at right now (inside a smoke bomb's cloud). Null = none.</summary>
        public Func<GameCharacter, bool> TargetHidden;
        public Vector2 ThrowDistance;
        public float ThrowWindupSeconds;

        /// <summary>Whether this NPC may strike right now.</summary>
        public bool CanKillNow => KillAllowed == null || KillAllowed();

        void Awake() {
            gameCharacter = GetComponent<GameCharacter>();
            localNPC = GetComponent<LocalNPC>();
            characterAnimator = GetComponent<CharacterAnimator>();
            animator = GetComponent<Animator>();
            characterAnimator.LethalAttackStarted += HandleLethalAttackStarted;
            characterAnimator.LethalAttackEnded += HandleLethalAttackEnded;
            localNPC.WalkSpeed = 18f;
            localNPC.FleeSpeed = 21f;
            localNPC.AttackSpeed = 21f;
            localNPC.AttackFunction = AttackFunction;
        }

        void Start() {
            InitializeAttackState();
        }

        // NPC-specific components are added after ServerNPC.SpawnNPC has already
        // spawned the object. Do the setup from our own lifecycle instead of relying
        // on FishNet invoking OnStartServer for a component added post-spawn.
        public void InitializeAttackState() {
            bool isMurderer = gameCharacter.GetTeam().realTeam == TeamColor.Red;

            // Townsfolk run from murderers (Classic narrows that to murderers that give themselves away,
            // through LocalNPC.FleeThreatFilter). Infection can change an NPC's role at runtime, so always
            // refresh this policy here and clear it as soon as that NPC joins the infected team.
            localNPC.SetFleeTeams(isMurderer
                ? new List<TeamColor>()
                : new List<TeamColor> { TeamColor.Red });

            if (!isMurderer)
                return;

            // Death disables LocalNPC and leaves it in None. Make Random the base state
            // before entering Attack so losing sight of every enemy resumes roaming
            // instead of reverting the revived infected NPC to a permanent standstill.
            if (localNPC.TargetingType == NPCTargetingType.None)
                localNPC.SetTargetingType(NPCTargetingType.Random);

            if (sharedWeapon != null)
                return;

            sharedWeapon = IssueKnife != null
                ? IssueKnife(gameCharacter)
                : ServerTool.Instance.SpawnTool(gameCharacter.NetworkObject, ToolEnum.Dagger);
            if (sharedWeapon == null)
                return;

            localNPC.AttackDamageType = sharedWeapon.defaultDamageType;
            localNPC.SetTargetingType(NPCTargetingType.Attack);
        }

        void AttackFunction(GameCharacter targetCharacter) {
            if (sharedWeapon == null || targetCharacter == null || !CanKillNow)
                return;

            lastAttack = Time.time;
            pendingAttackTarget = targetCharacter;
            gameCharacter.SwitchTool(sharedWeapon);
            animator.SetBool("KnifeAttack", true);
        }

        void HandleLethalAttackStarted() {
            GameCharacter targetCharacter = pendingAttackTarget;
            pendingAttackTarget = null;

            if (sharedWeapon == null
                || targetCharacter == null
                || gameCharacter.IsDead
                || gameCharacter.ActiveTool.Value != sharedWeapon
                || !CanKillNow
                || !localNPC.IsTargetInAttackRange(targetCharacter))
                return;

            targetCharacter.TakeDamage(
                sharedWeapon.hitDamage,
                sharedWeapon.defaultDamageType,
                gameCharacter.NetworkObject);
        }

        void HandleLethalAttackEnded() {
            animator.SetBool("KnifeAttack", false);
        }

        void Update() {
            UpdateThrow();
            // Only the knife is managed here; an NPC sheriff's gun is drawn and holstered by MM_NpcSheriff.
            if (sharedWeapon != null && lastAttack + UnequipAfterAttackSeconds <= Time.time) {
                pendingAttackTarget = null;
                gameCharacter.SwitchTool(null);
            }
            UpdateHolster();
        }

        /// <summary>
        /// Now and then a murderer that may kill throws at a target it can see from mid range, after drawing
        /// the knife for a short wind-up (the sheriff's chance to react). Targets dodge by moving: it aims
        /// where they stand when it lets go.
        /// </summary>
        void UpdateThrow() {
            if (ThrowKnife == null || sharedWeapon == null || gameCharacter.IsDead || !CanKillNow) {
                pendingThrowTarget = null;
                return;
            }
            if (pendingThrowTarget != null) {
                if (Time.time < throwAt)
                    return;
                GameCharacter target = pendingThrowTarget;
                pendingThrowTarget = null;
                if (!target.IsDead && !Hidden(target) && MM_Sight.HasLineOfSight(gameCharacter, target))
                    ThrowKnife(gameCharacter, MM_Sight.Chest(target));
                lastAttack = Time.time;
                return;
            }
            GameCharacter current = localNPC.CurrentAttackTarget;
            if (current == null || current.IsDead || Hidden(current))
                return;
            float distance = Vector3.Distance(transform.position, current.transform.position);
            if (distance < ThrowDistance.x || distance > ThrowDistance.y)
                return;
            if (UnityEngine.Random.value > ThrowChancePerSecond * Time.deltaTime)
                return;
            if (!MM_Sight.HasLineOfSight(gameCharacter, current))
                return;
            pendingThrowTarget = current;
            throwAt = Time.time + ThrowWindupSeconds;
            lastAttack = Time.time;
            gameCharacter.SwitchTool(sharedWeapon);
        }

        bool Hidden(GameCharacter target) => TargetHidden != null && TargetHidden(target);

        /// <summary>
        /// Only an armed murderer holsters. While holstered it does not pick targets on its own, so it blends
        /// into the crowd instead of visibly stalking a victim it is not allowed to kill yet.
        /// </summary>
        void UpdateHolster() {
            bool holster = sharedWeapon != null && !gameCharacter.IsDead && !CanKillNow;
            if (holster == holstered)
                return;
            holstered = holster;
            localNPC.AutomaticTargeting = !holster;
            if (holster && localNPC.TargetingType == NPCTargetingType.Attack)
                localNPC.SetTargetingType(NPCTargetingType.Random);
        }

        void OnDestroy() {
            if (characterAnimator == null)
                return;

            characterAnimator.LethalAttackStarted -= HandleLethalAttackStarted;
            characterAnimator.LethalAttackEnded -= HandleLethalAttackEnded;
        }
    }
}
