using System.Collections;
using FishNet.Object;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Gives a structure replicated health and a costly NavMesh breach link. Agents prefer
    /// an ordinary route when one is reasonably short, but can stop at the link, destroy the
    /// wall, and continue through the cleared opening.
    /// </summary>
    [RequireComponent(typeof(StructureComponent), typeof(EffectsComponent), typeof(HealthComponent))]
    [RequireComponent(typeof(NavMeshObstacle), typeof(NavMeshLink))]
    public sealed class WV_DestructibleObstacle : NetworkBehaviour {
        private const int WarValleyAgentType = -902729914;

        [SerializeField, Min(1)] private long maxHealth = 300;
        [SerializeField, Min(1.01f)] private float breachCost = 8f;
        [SerializeField, Min(0.05f)] private float linkClearance = 0.35f;
        [SerializeField, Min(0f)] private float despawnDelay = 3f;

        private StructureComponent structure;
        private NavMeshObstacle obstacle;
        private NavMeshLink breachLink;
        private bool destroyed;

        public IEntity Entity => structure;
        public bool IsDestroyed => destroyed || (structure != null && structure.IsDead);
        public NavMeshLink BreachLink => breachLink;

        private void Awake() {
            CacheComponents();
            ConfigureNavigation();
        }

        private void OnValidate() {
            CacheComponents();
            ConfigureNavigation();
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            CacheComponents();
            ConfigureNavigation();
            structure.OnDied += HandleDied;
        }

        public override void OnStopNetwork() {
            if (structure != null)
                structure.OnDied -= HandleDied;
            base.OnStopNetwork();
        }

#if UNITY_SERVER
        public override void OnStartServer() {
            base.OnStartServer();
            structure.Init(maxHealth);
        }
#endif

        private void CacheComponents() {
            structure ??= GetComponent<StructureComponent>();
            obstacle ??= GetComponent<NavMeshObstacle>();
            breachLink ??= GetComponent<NavMeshLink>();
        }

        private void ConfigureNavigation() {
            if (obstacle == null || breachLink == null)
                return;

            BoxCollider wallCollider = GetComponent<BoxCollider>();
            if (wallCollider == null)
                return;

            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = wallCollider.center;
            obstacle.size = wallCollider.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            float worldDepth = Mathf.Abs(wallCollider.size.z * transform.lossyScale.z);
            float worldWidth = Mathf.Abs(wallCollider.size.x * transform.lossyScale.x);
            float endpointDistance = worldDepth * 0.5f + linkClearance;
            Vector3 scaledColliderCenter = Vector3.Scale(
                wallCollider.center,
                new Vector3(
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Abs(transform.lossyScale.y),
                    Mathf.Abs(transform.lossyScale.z)));
            Vector3 linkCenter = transform.position
                + transform.rotation * scaledColliderCenter
                - transform.up * (Mathf.Abs(wallCollider.size.y * transform.lossyScale.y) * 0.5f)
                + transform.up * 0.05f;
            Quaternion inverseRotation = Quaternion.Inverse(transform.rotation);

            // NavMeshLink applies position and rotation but deliberately ignores transform
            // scale, so convert world offsets with inverse rotation rather than InverseTransformPoint.
            breachLink.startPoint = inverseRotation
                * (linkCenter - transform.forward * endpointDistance - transform.position);
            breachLink.endPoint = inverseRotation
                * (linkCenter + transform.forward * endpointDistance - transform.position);
            breachLink.width = Mathf.Max(0f, worldWidth - linkClearance * 2f);
            breachLink.agentTypeID = WarValleyAgentType;
            breachLink.area = NavMesh.GetAreaFromName("Walkable");
            breachLink.costModifier = breachCost;
            breachLink.bidirectional = true;
            breachLink.autoUpdate = true;
        }

        private void HandleDied(DamageType source, IEntity attacker) {
            if (destroyed)
                return;

            destroyed = true;
            if (obstacle != null)
                obstacle.enabled = false;

            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;

#if UNITY_SERVER
            if (IsServerStarted && IsSpawned)
                StartCoroutine(DespawnAfterDelay());
#endif
        }

#if UNITY_SERVER
        private IEnumerator DespawnAfterDelay() {
            if (despawnDelay > 0f)
                yield return new WaitForSeconds(despawnDelay);
            if (IsSpawned)
                Despawn();
        }
#endif
    }
}
