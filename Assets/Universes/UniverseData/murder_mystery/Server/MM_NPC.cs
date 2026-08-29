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
        // LocalNPC's default detection radius is 10. The pistol reaches 60, so use a sixfold
        // multiplier to make an armed sheriff visible across their full firing distance.
        private const float ArmedSheriffRangeMultiplier = 6f;
        private const float ArmedSheriffPriority = 1f;
        private const float ArmedSheriffSightRadius = 100f;

        private LocalNPC localNPC;
        private GameCharacter gameCharacter;

        private void Awake() {
            localNPC = GetComponent<LocalNPC>();
            gameCharacter = GetComponent<GameCharacter>();

            localNPC.AttackTargetRangeMultiplier = GetAttackRangeMultiplier;
            localNPC.AttackTargetPriority = GetAttackPriority;
            gameCharacter.OnDamage += HandleDamage;
        }

        private void Update() {
            if (!localNPC.AllowAttackTargetOverrides)
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

        private static float GetAttackRangeMultiplier(GameCharacter target) {
            return IsArmedSheriff(target) ? ArmedSheriffRangeMultiplier : 1f;
        }

        private static float GetAttackPriority(GameCharacter target) {
            return IsArmedSheriff(target) ? ArmedSheriffPriority : 0f;
        }

        private void HandleDamage(DamageType source, IEntity attacker) {
            if (localNPC.AllowAttackTargetOverrides
                && source == DamageType.Gun
                && attacker is GameCharacter attackerCharacter)
                localNPC.TargetCharacter(attackerCharacter);
        }

        private GameCharacter GetNearestArmedSheriff() {
            if (!GameCharacter.TeamToCharacter.TryGetValue(TeamColor.Blue, out var sheriffs))
                return null;

            GameCharacter nearest = null;
            float nearestDistanceSquared = ArmedSheriffSightRadius * ArmedSheriffSightRadius;
            foreach (GameCharacter sheriff in sheriffs) {
                if (!IsArmedSheriff(sheriff)) continue;

                float distanceSquared = (sheriff.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared >= nearestDistanceSquared) continue;

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
