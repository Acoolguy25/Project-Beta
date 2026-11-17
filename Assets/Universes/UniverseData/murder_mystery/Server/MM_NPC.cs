using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// Murder Mystery's NPC threat policy. LocalNPC supplies the generic target-selection and
    /// retaliation hooks; role and weapon knowledge stays here with the game mode.
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter))]
    public sealed class MM_NPC : MonoBehaviour {
        // LocalNPC's default detection radius is 10. The pistol reaches 60, so by default a sixfold
        // multiplier makes an armed sheriff visible across their full firing distance. Classic replaces
        // these through ConfigureSheriffHunting (MM_ClassicTuning), so a sheriff is only rushed once noticed.
        private float sheriffRangeMultiplier = 6f;
        private float sheriffPriority = 1f;
        private const float DefaultSheriffSightRadius = 100f;
        private System.Func<GameCharacter, float> sheriffSightRadius;
        // Closing in on an armed sheriff this NPC slows to a creep (0 distance = never), which is the
        // sheriff's chance to spot it and shoot; a charge at full attack speed covers 10 m in half a second.
        private float sheriffCreepDistance;
        private float sheriffCreepSpeed;
        private float chargeSpeed;
        private bool creeping;

        private LocalNPC localNPC;
        private GameCharacter gameCharacter;
        private MM_LocalNPC murderPolicy;

        private void Awake() {
            localNPC = GetComponent<LocalNPC>();
            gameCharacter = GetComponent<GameCharacter>();
            murderPolicy = GetComponent<MM_LocalNPC>();
            chargeSpeed = localNPC.AttackSpeed;

            localNPC.AttackTargetRangeMultiplier = GetAttackRangeMultiplier;
            localNPC.AttackTargetPriority = GetAttackPriority;
            gameCharacter.OnDamage += HandleDamage;
        }

        /// <summary>
        /// How hard this NPC goes after an armed sheriff: <paramref name="sightRadius"/> says how close a given
        /// sheriff must be right now before they are rushed (the round widens it after a shot and late in the hunt),
        /// and inside <paramref name="creepDistance"/> of one it slows to <paramref name="creepSpeed"/>.
        /// </summary>
        public void ConfigureSheriffHunting(System.Func<GameCharacter, float> sightRadius, float rangeMultiplier, float priority,
            float creepDistance, float creepSpeed) {
            sheriffSightRadius = sightRadius;
            sheriffRangeMultiplier = rangeMultiplier;
            sheriffPriority = priority;
            sheriffCreepDistance = creepDistance;
            sheriffCreepSpeed = creepSpeed;
        }

        /// <summary>Forced targeting only applies while this NPC may actually kill (not during a grace period or cooldown).</summary>
        private bool MayForceTarget => localNPC.AllowAttackTargetOverrides && (murderPolicy == null || murderPolicy.CanKillNow);

        private void Update() {
            GameCharacter armedSheriff = MayForceTarget ? GetNearestArmedSheriff() : null;
            // Enforce the game-mode priority every frame rather than waiting for LocalNPC's
            // normal retarget interval. This makes a newly armed sheriff immediately replace
            // an NPC's current victim.
            if (armedSheriff != null)
                localNPC.TargetCharacter(armedSheriff);
            SetCreeping(armedSheriff != null
                && (armedSheriff.transform.position - transform.position).sqrMagnitude < sheriffCreepDistance * sheriffCreepDistance);
        }

        private void SetCreeping(bool creep) {
            if (creep == creeping)
                return;
            creeping = creep;
            localNPC.AttackSpeed = creep ? Mathf.Min(sheriffCreepSpeed, chargeSpeed) : chargeSpeed;
            localNPC.UpdateSpeed();
        }

        private void OnDestroy() {
            if (gameCharacter != null)
                gameCharacter.OnDamage -= HandleDamage;

            if (localNPC != null) {
                if (localNPC.AttackTargetRangeMultiplier == GetAttackRangeMultiplier)
                    localNPC.AttackTargetRangeMultiplier = null;
                if (localNPC.AttackTargetPriority == GetAttackPriority)
                    localNPC.AttackTargetPriority = null;
            }
        }

        private float GetAttackRangeMultiplier(GameCharacter target) {
            return IsArmedSheriff(target) ? sheriffRangeMultiplier : 1f;
        }

        private float GetAttackPriority(GameCharacter target) {
            return IsArmedSheriff(target) ? sheriffPriority : 0f;
        }

        private void HandleDamage(DamageType source, IEntity attacker) {
            if (MayForceTarget
                && source == DamageType.Gun
                && attacker is GameCharacter attackerCharacter)
                localNPC.TargetCharacter(attackerCharacter);
        }

        private GameCharacter GetNearestArmedSheriff() {
            if (!GameCharacter.TeamToCharacter.TryGetValue(TeamColor.Blue, out var sheriffs))
                return null;

            GameCharacter nearest = null;
            float nearestDistanceSquared = float.MaxValue;
            foreach (GameCharacter sheriff in sheriffs) {
                if (!IsArmedSheriff(sheriff)) continue;

                float sight = sheriffSightRadius != null ? sheriffSightRadius(sheriff) : DefaultSheriffSightRadius;
                float distanceSquared = (sheriff.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared >= sight * sight || distanceSquared >= nearestDistanceSquared) continue;

                nearest = sheriff;
                nearestDistanceSquared = distanceSquared;
            }
            return nearest;
        }

        private static bool IsArmedSheriff(GameCharacter character) {
            if (character == null || character.GetTeam().realTeam != TeamColor.Blue)
                return false;

            ToolBaseShared activeTool = character.ActiveTool.Value;
            if (activeTool != null && activeTool.toolEnum == ToolEnum.Pistol)
                return true;

            // Weapon-root activation is observer-only, so a dedicated server cannot always
            // read it. The authoritative tool registry provides the reliable fallback.
            return ServerTool.Instance != null
                && ServerTool.Instance.GetTool(character.NetworkObject, ToolEnum.Pistol) != null;
        }
    }
}
