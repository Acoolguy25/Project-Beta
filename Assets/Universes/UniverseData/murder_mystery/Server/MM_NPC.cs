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
        // these through Configure (MM_ClassicTuning), so a sheriff is only rushed once noticed.
        private float sheriffRangeMultiplier = 6f;
        private float sheriffPriority = 1f;
        private float sheriffSightRadius = 100f;
        private float exposedSheriffSightRadius = 100f;
        private System.Func<GameCharacter, bool> isSheriffExposed;

        private LocalNPC localNPC;
        private GameCharacter gameCharacter;
        private MM_LocalNPC murderPolicy;

        private void Awake() {
            localNPC = GetComponent<LocalNPC>();
            gameCharacter = GetComponent<GameCharacter>();
            murderPolicy = GetComponent<MM_LocalNPC>();

            localNPC.AttackTargetRangeMultiplier = GetAttackRangeMultiplier;
            localNPC.AttackTargetPriority = GetAttackPriority;
            gameCharacter.OnDamage += HandleDamage;
        }

        /// <summary>
        /// How hard this NPC goes after an armed sheriff: <paramref name="sightRadius"/> is how close a
        /// quiet sheriff must be before it is rushed, <paramref name="exposedSightRadius"/> applies while
        /// <paramref name="isExposed"/> says the sheriff has just shot someone.
        /// </summary>
        public void ConfigureSheriffHunting(float sightRadius, float exposedSightRadius, float rangeMultiplier, float priority,
            System.Func<GameCharacter, bool> isExposed) {
            sheriffSightRadius = sightRadius;
            exposedSheriffSightRadius = exposedSightRadius;
            sheriffRangeMultiplier = rangeMultiplier;
            sheriffPriority = priority;
            isSheriffExposed = isExposed;
        }

        /// <summary>Forced targeting only applies while this NPC may actually kill (not during a grace period or cooldown).</summary>
        private bool MayForceTarget => localNPC.AllowAttackTargetOverrides && (murderPolicy == null || murderPolicy.CanKillNow);

        private void Update() {
            if (!MayForceTarget)
                return;

            // Enforce the game-mode priority every frame rather than waiting for LocalNPC's
            // normal retarget interval. This makes a newly armed sheriff immediately replace
            // an NPC's current victim.
            GameCharacter armedSheriff = GetNearestArmedSheriff();
            if (armedSheriff != null)
                localNPC.TargetCharacter(armedSheriff);
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

                float sight = isSheriffExposed != null && isSheriffExposed(sheriff) ? exposedSheriffSightRadius : sheriffSightRadius;
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
