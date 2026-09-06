using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// War Valley's NPC combat policy. The flag is the strategic target; a costly wall link
    /// temporarily becomes the target when it blocks the selected route.
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter))]
    public sealed class WV_NPC : MonoBehaviour {
        private const float UnequipAttackDelay = 1f;
        private const float BlockingWallProbeDistance = 6f;
        private const float LongRangeFallbackDistance = 12f;
        private const float StalledDuration = 1.5f;
        private const float ProgressDistance = 0.25f;
        private const float FallbackPathInterval = 0.5f;
        private const float TargetNavMeshSampleRadius = 8f;

        private LocalNPC localNPC;
        private GameCharacter gameCharacter;
        private CharacterAnimator characterAnimator;
        private Animator animator;
        private NavMeshAgent agent;
        private ToolBaseShared weapon;
        private IEntity pendingAttackTarget;
        private float lastAttack = float.MinValue;
        private float lastProgressTime;
        private float nextFallbackPathTime;
        private Vector3 lastProgressPosition;
        private WV_DestructibleObstacle wallTarget;
        private bool traversingClearedWall;

        private void Awake() {
            localNPC = GetComponent<LocalNPC>();
            gameCharacter = GetComponent<GameCharacter>();
            characterAnimator = GetComponent<CharacterAnimator>();
            animator = GetComponent<Animator>();
            agent = GetComponent<NavMeshAgent>();

            characterAnimator.LethalAttackStarted += HandleLethalAttackStarted;
            characterAnimator.LethalAttackEnded += HandleLethalAttackEnded;
            localNPC.WalkSpeed = 18f;
            localNPC.FleeSpeed = 21f;
            localNPC.AttackSpeed = 21f;
            localNPC.AttackEntityFunction = AttackEntity;
            agent.autoTraverseOffMeshLink = false;
            lastProgressPosition = transform.position;
            lastProgressTime = Time.time;
        }

        private void Start() {
            weapon = ServerTool.Instance.SpawnTool(gameCharacter.NetworkObject, ToolEnum.Dagger);
            if (weapon != null)
                localNPC.AttackDamageType = weapon.defaultDamageType;

            localNPC.SetTargetingType(NPCTargetingType.Attack);
            TargetFlag();
        }

        private void Update() {
            UpdateWallTraversal();

            if (lastAttack + UnequipAttackDelay <= Time.time) {
                pendingAttackTarget = null;
                gameCharacter.SwitchTool(null);
            }
        }

        private void LateUpdate() {
            EnsureLongRangeMovement();
        }

        private void UpdateWallTraversal() {
            if (traversingClearedWall) {
                if (!agent.isOnOffMeshLink) {
                    traversingClearedWall = false;
                    agent.autoTraverseOffMeshLink = false;
                    wallTarget = null;
                    TargetFlag();
                }
                return;
            }

            if (agent.isOnOffMeshLink) {
                WV_DestructibleObstacle wall = GetCurrentWall();
                if (wall != null && !wall.IsDestroyed) {
                    TargetWall(wall);
                    return;
                }

                // The wall is gone (or this is a normal link), so let Unity complete this
                // traversal. War Valley keeps automatic traversal off otherwise so an NPC
                // cannot pass through a living destructible wall.
                agent.autoTraverseOffMeshLink = true;
                traversingClearedWall = true;
                return;
            }

            agent.autoTraverseOffMeshLink = false;
            if (wallTarget != null) {
                if (!wallTarget.IsDestroyed) {
                    if (localNPC.CurrentAttackEntityTarget != wallTarget.Entity)
                        localNPC.TargetEntity(wallTarget.Entity);
                    return;
                }

                wallTarget = null;
                TargetFlag();
            }

            WV_DestructibleObstacle blockingWall = FindBlockingWall();
            if (blockingWall != null && TargetWall(blockingWall))
                return;

            WV_Flag flag = WV_Flag.Instance;
            if (flag != null && !flag.IsDead && localNPC.CurrentAttackEntityTarget != flag.GetComponent<IEntity>())
                TargetFlag();
        }

        private bool TargetWall(WV_DestructibleObstacle wall) {
            if (wall == null || wall.IsDestroyed || !localNPC.TargetEntity(wall.Entity))
                return false;

            wallTarget = wall;
            return true;
        }

        private WV_DestructibleObstacle FindBlockingWall() {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return null;

            Vector3 destination = agent.hasPath ? agent.steeringTarget : GetFlagPosition();
            Vector3 direction = destination - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f)
                return null;

            Vector3 origin = transform.position + Vector3.up * Mathf.Max(agent.height * 0.5f, 0.5f);
            float radius = Mathf.Max(agent.radius * 0.8f, 0.1f);
            if (!Physics.SphereCast(
                    origin,
                    radius,
                    direction.normalized,
                    out RaycastHit hit,
                    BlockingWallProbeDistance,
                    LayerMask.GetMask("Structure"),
                    QueryTriggerInteraction.Ignore))
                return null;

            return hit.collider.GetComponentInParent<WV_DestructibleObstacle>();
        }

        private WV_DestructibleObstacle GetCurrentWall() {
            Object owner = agent.currentOffMeshLinkData.owner;
            if (owner is Component component)
                return component.GetComponentInParent<WV_DestructibleObstacle>();
            if (owner is GameObject gameObject)
                return gameObject.GetComponentInParent<WV_DestructibleObstacle>();
            return null;
        }

        private void TargetFlag() {
            WV_Flag flag = WV_Flag.Instance;
            if (flag != null && !flag.IsDead)
                localNPC.TargetEntity(flag);
        }

        private Vector3 GetFlagPosition() {
            WV_Flag flag = WV_Flag.Instance;
            return flag != null ? flag.transform.position : transform.position;
        }

        private void EnsureLongRangeMovement() {
            if (agent == null
                || !agent.enabled
                || !agent.isOnNavMesh
                || agent.isOnOffMeshLink
                || traversingClearedWall
                || gameCharacter.IsDead)
                return;

            if ((transform.position - lastProgressPosition).sqrMagnitude >= ProgressDistance * ProgressDistance) {
                lastProgressPosition = transform.position;
                lastProgressTime = Time.time;
            }

            IEntity target = localNPC.CurrentAttackEntityTarget;
            if (target is not Component targetComponent || targetComponent == null)
                return;

            Vector3 targetPosition = targetComponent.transform.position;
            if ((targetPosition - transform.position).sqrMagnitude < LongRangeFallbackDistance * LongRangeFallbackDistance
                || Time.time - lastProgressTime < StalledDuration
                || Time.time < nextFallbackPathTime)
                return;

            nextFallbackPathTime = Time.time + FallbackPathInterval;
            if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, TargetNavMeshSampleRadius, agent.areaMask))
                return;

            // LocalNPC deliberately accepts only complete paths. War Valley spans a large map
            // with runtime-carved walls, so a temporarily partial path is still useful: it gets
            // the NPC moving toward the reachable edge where wall detection can take over.
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }

        private void AttackEntity(IEntity target) {
            if (weapon == null || target == null)
                return;

            lastAttack = Time.time;
            pendingAttackTarget = target;
            gameCharacter.SwitchTool(weapon);
            animator.SetBool("KnifeAttack", true);
        }

        private void HandleLethalAttackStarted() {
            IEntity target = pendingAttackTarget;
            pendingAttackTarget = null;
            if (weapon == null
                || target is not Component targetComponent
                || targetComponent == null
                || gameCharacter.IsDead
                || gameCharacter.ActiveTool.Value != weapon
                || !localNPC.IsTargetInAttackRange(target))
                return;

            HealthComponent targetHealth = targetComponent.GetComponent<HealthComponent>();
            if (targetHealth != null)
                targetHealth.TakeDamage(weapon.hitDamage, weapon.defaultDamageType, gameCharacter);
        }

        private void HandleLethalAttackEnded() {
            animator.SetBool("KnifeAttack", false);
        }

        private void OnDestroy() {
            if (characterAnimator != null) {
                characterAnimator.LethalAttackStarted -= HandleLethalAttackStarted;
                characterAnimator.LethalAttackEnded -= HandleLethalAttackEnded;
            }
            if (localNPC != null && localNPC.AttackEntityFunction == AttackEntity)
                localNPC.AttackEntityFunction = null;
        }
    }
}
