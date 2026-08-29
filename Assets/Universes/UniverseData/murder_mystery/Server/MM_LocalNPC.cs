using RyanAssets.DataService;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RyanAssets.Characters.Shared;
using RyanAssets.Characters.Server;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Declarations;
using RyanAssets.Server.ServerCore;
using RyanAssets.Tools.Shared;
using FishNet.Object;

namespace Universes.murder_mystery.Server {
    public class MM_LocalNPC : NetworkBehaviour {
        //[Header("Attack")]
        //[SerializeField] public List<TeamColor> TargetTeams = new List<TeamColor>();
        //[SerializeField] private float AttackEnterRadius = 6f;
        float unequipAttackTime = 1f;

        LocalNPC localNPC;
        GameCharacter gameCharacter;
        CharacterAnimator characterAnimator;
        Animator animator;

        ToolBaseShared sharedWeapon;
        GameCharacter pendingAttackTarget;
        float lastAttack = float.MinValue;
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

            // Innocent and sheriff NPCs run from nearby murderers in both Classic and
            // Infection. Infection can change an NPC's role at runtime, so always refresh
            // this policy here and clear it as soon as that NPC joins the infected team.
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

            sharedWeapon = ServerTool.Instance.SpawnTool(gameCharacter.NetworkObject, ToolEnum.Dagger);
            if (sharedWeapon == null)
                return;

            localNPC.AttackDamageType = sharedWeapon.defaultDamageType;
            localNPC.SetTargetingType(NPCTargetingType.Attack);
        }
        void AttackFunction(GameCharacter targetCharacter) {
            if (sharedWeapon == null || targetCharacter == null)
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
            if (lastAttack + unequipAttackTime <= Time.time) {
                pendingAttackTarget = null;
                gameCharacter.SwitchTool(null);
            }
        }

        void OnDestroy() {
            if (characterAnimator == null)
                return;

            characterAnimator.LethalAttackStarted -= HandleLethalAttackStarted;
            characterAnimator.LethalAttackEnded -= HandleLethalAttackEnded;
        }
    }
}
