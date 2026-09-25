using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// The circular shield a shield generator raises around itself: a target in its own right, with
    /// its own health, that keeps the enemy out of the circle and keeps anything inside it from
    /// being hurt from outside.
    /// <para>
    /// The rules, all enforced on the server:
    /// </para>
    /// <list type="bullet">
    /// <item>Allies cross the shield freely; it neither blocks them nor their shots.</item>
    /// <item>An enemy that walks into the circle is put back outside it.</item>
    /// <item>
    /// Nothing inside can be harmed by an enemy outside - including the generator. The shield
    /// registers as an <see cref="IDamageShield"/>, so the refusal happens where every hit is
    /// already checked, and enemy targeting, which asks the same question, looks elsewhere.
    /// </item>
    /// <item>The one thing an enemy can hurt is the shield itself. It has to be shot down first.</item>
    /// <item>
    /// A broken shield comes back at full strength after <see cref="RegenerationSeconds"/>, for as
    /// long as its generator is still standing.
    /// </item>
    /// </list>
    /// <para>
    /// Shields of the same side overlap into one protected area: an ally's shield next to yours is
    /// part of the same wall, and an enemy that got into one circle is not "outside" just because it
    /// is also inside the next. The shield sits on a child of the generator and shares its
    /// NetworkObject, so it spawns, replicates, and leaves with the building.
    /// </para>
    /// </summary>
    public sealed class WV_ShieldBarrier : EntityBase, IDamageShield {
        const float EnforcementInterval = 0.1f;
        /// <summary>How far past the edge an enemy is set down, so it is not immediately back inside.</summary>
        const float EjectionMargin = 1f;
        static readonly Collider[] OverlapBuffer = new Collider[128];

        [SerializeField, Min(1f)] float radius = 18f;
        [SerializeField, Min(1)] long maxHealth = 1500;
        [Tooltip("Seconds a broken shield stays down before it comes back at full strength, as long " +
                 "as its generator is still standing.")]
        [SerializeField, Min(0f)] float regenerationSeconds = 30f;
        [Tooltip("The generator this shield belongs to. It takes its team and lifetime from it.")]
        [SerializeField] StructureComponent generator;
        [Tooltip("The dome effect. Its renderers are authored disabled so a shield that is down, " +
                 "or still being built, draws nothing and never widens placement bounds.")]
        [SerializeField] Renderer[] domeRenderers = System.Array.Empty<Renderer>();
        [Tooltip("The shield's hit volume: a trigger on Ignore Raycast, so it is measured and aimed " +
                 "at without stopping bullets or bodies.")]
        [SerializeField] SphereCollider hitVolume;

        /// <summary>Server time the shield comes back, or 0 while it is up.</summary>
        readonly SyncVar<float> restoreTime = new();

        static readonly List<WV_ShieldBarrier> all = new();

        WV_Constructable generatorConstructable;
        float nextEnforcementTime;
        bool domeShown;

        public static IReadOnlyList<WV_ShieldBarrier> All => all;

        public override string DisplayName {
            get => "Shield";
            set { }
        }

        public override TeamConfig Team => generator != null ? generator.Team : null;

        public float Radius => radius;
        public float RegenerationSeconds => regenerationSeconds;
        public StructureComponent Generator => generator;

        /// <summary>True while the shield is standing: its generator is built and alive, and it has not been broken.</summary>
        public bool IsUp =>
            IsSpawned
            && !IsDead
            && generator != null
            && !generator.IsDead
            && (generatorConstructable == null || generatorConstructable.IsOperational);

        /// <summary>Seconds until a broken shield returns, or 0 when it is up or has no generator left.</summary>
        public float SecondsUntilRestored =>
            restoreTime.Value > 0f ? Mathf.Max(0f, restoreTime.Value - NetworkHelper.ServerTime) : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        protected override void Awake() {
            base.Awake();
            if (generator == null)
                generator = GetComponentInParent<StructureComponent>();
            generatorConstructable = generator != null ? generator.GetComponent<WV_Constructable>() : null;
            if (hitVolume != null)
                hitVolume.radius = radius / Mathf.Max(0.0001f, transform.lossyScale.x);
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            all.Add(this);
            DamageShields.Register(this);
        }

        public override void OnStopNetwork() {
            DamageShields.Unregister(this);
            all.Remove(this);
            base.OnStopNetwork();
        }

        /// <summary>True when <paramref name="position"/> is inside this shield's circle.</summary>
        public bool Contains(Vector3 position) {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude < radius * radius;
        }

        /// <summary>Distance from <paramref name="position"/> to the edge of the circle; negative inside it.</summary>
        public float DistanceToEdge(Vector3 position) {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.magnitude - radius;
        }

        /// <summary>The point on the shield's edge facing <paramref name="from"/>, at that point's height.</summary>
        public Vector3 GetEdgePoint(Vector3 from) {
            Vector3 outward = from - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f)
                outward = Vector3.forward;
            Vector3 edge = transform.position + outward.normalized * radius;
            edge.y = from.y;
            return edge;
        }

        // --- Queries shared by targeting and damage ------------------------------

        /// <summary>True when <paramref name="position"/> is inside any standing shield of <paramref name="side"/>.</summary>
        public static bool IsInsideShieldOf(Vector3 position, TeamConfig side) {
            foreach (WV_ShieldBarrier shield in all) {
                if (shield.IsUp && CombatTeams.AreAllies(shield.Team, side) && shield.Contains(position))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when an attacker at <paramref name="attackerPosition"/> cannot hurt
        /// <paramref name="target"/> because a shield of the target's side stands between them. A
        /// shield itself is never protected: it is the thing to attack.
        /// </summary>
        public static bool Protects(IEntity target, Vector3 attackerPosition, TeamConfig attackerTeam) {
            if (target is not Component component || component == null || target is WV_ShieldBarrier)
                return false;
            if (!CombatTeams.AreEnemies(attackerTeam, target.Team))
                return false;
            return IsInsideShieldOf(component.transform.position, target.Team)
                && !IsInsideShieldOf(attackerPosition, target.Team);
        }

        /// <summary>
        /// The standing shield of an enemy side that covers <paramref name="position"/> and is nearest
        /// to <paramref name="from"/> - what an attacker has to bring down to reach that point.
        /// </summary>
        public static WV_ShieldBarrier FindHostileShieldCovering(Vector3 position, Vector3 from, TeamConfig attackerTeam) {
            WV_ShieldBarrier best = null;
            float bestDistance = float.MaxValue;
            foreach (WV_ShieldBarrier shield in all) {
                if (!shield.IsUp || !CombatTeams.AreEnemies(attackerTeam, shield.Team) || !shield.Contains(position))
                    continue;
                float distance = shield.DistanceToEdge(from);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = shield;
                }
            }
            return best;
        }

        /// <summary>
        /// Where a placed generator's circle would stand, and whether it joins the commander's
        /// existing shields. Shields have to form one connected area: a commander's first generator
        /// may go anywhere, and every later one must overlap a shield of theirs already standing.
        /// </summary>
        public static bool ConnectsToOwnShields(int clientId, Vector3 center, float newRadius, WV_ShieldBarrier ignore = null) {
            bool hasAny = false;
            foreach (WV_ShieldBarrier shield in all) {
                if (shield == ignore
                    || shield.generator == null
                    || shield.generator.IsDead
                    || !shield.generator.TryGetComponent(out WV_Owned owner)
                    || !owner.IsOwnedBy(clientId))
                    continue;
                hasAny = true;
                Vector3 offset = shield.transform.position - center;
                offset.y = 0f;
                float reach = shield.radius + newRadius;
                if (offset.sqrMagnitude <= reach * reach)
                    return true;
            }
            return !hasAny;
        }

        /// <summary>The <see cref="IDamageShield"/> half: refuses hits from outside on anything inside this circle.</summary>
        public bool Blocks(IEntity target, IEntity source, DamageType damageType) {
            if (!IsUp || source is not Component sourceComponent || sourceComponent == null)
                return false;
            if (target is not Component targetComponent || !Contains(targetComponent.transform.position))
                return false;
            return Protects(target, sourceComponent.transform.position, source.Team);
        }

        // --- Presentation ----------------------------------------------------------

        void Update() {
#if UNITY_SERVER
            if (IsServerStarted)
                UpdateServer();
#endif
            bool up = IsUp;
            if (domeShown == up)
                return;
            domeShown = up;
            foreach (Renderer dome in domeRenderers) {
                if (dome != null)
                    dome.enabled = up;
            }
            if (hitVolume != null)
                hitVolume.enabled = up;
        }

#if UNITY_SERVER
        public override void OnStartServer() {
            base.OnStartServer();
            Init(maxHealth);
            OnDied += HandleBroken;
        }

        public override void OnStopServer() {
            OnDied -= HandleBroken;
            base.OnStopServer();
        }

        void HandleBroken(DamageType source, IEntity attacker) {
            restoreTime.Value = NetworkHelper.ServerTime + regenerationSeconds;
        }

        void UpdateServer() {
            if (generator == null || generator.IsDead)
                return;

            if (IsDead) {
                if (restoreTime.Value > 0f && NetworkHelper.ServerTime >= restoreTime.Value) {
                    restoreTime.Value = 0f;
                    Revive(maxHealth);
                }
                return;
            }

            if (!IsUp || Time.time < nextEnforcementTime)
                return;
            nextEnforcementTime = Time.time + EnforcementInterval;
            EjectEnemies();
        }

        /// <summary>
        /// Puts every enemy that has crossed into the circle back outside it. Server-driven movers -
        /// NPCs and vehicles on the NavMesh - are moved; a player's own movement belongs to their
        /// client, so an enemy player is kept out by the damage rule alone.
        /// </summary>
        void EjectEnemies() {
            TeamConfig team = Team;
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, OverlapBuffer, WV_Combat.TargetMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;
                IEntity entity = collider.GetComponentInParent<IEntity>();
                if (entity is not Component component
                    || entity.IsDead
                    || !CombatTeams.AreEnemies(entity.Team, team)
                    || !Contains(component.transform.position)
                    || !component.TryGetComponent(out NavMeshAgent agent)
                    || !agent.enabled
                    || !agent.isOnNavMesh)
                    continue;

                Vector3 outward = component.transform.position - transform.position;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f)
                    outward = Vector3.forward;
                Vector3 edge = transform.position + outward.normalized * (radius + EjectionMargin);
                edge.y = component.transform.position.y;
                if (NavMesh.SamplePosition(edge, out NavMeshHit hit, 6f, agent.areaMask))
                    agent.Warp(hit.position);
            }
        }
#endif
    }
}
