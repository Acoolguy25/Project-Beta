using FishNet.Object;
using RyanAssets.Shared.Declarations;
using UnityEngine;
#if UNITY_SERVER
using System.Collections;
#endif

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// One configurable, replicated explosion, shared by every game.
    /// <para>
    /// The authoritative half runs only on the server: it decides who is caught in the blast and
    /// applies the damage. The presentation is then replicated to every observer, so an explosion
    /// looks the same on the machine that caused it and on a client watching from across the map.
    /// Nothing about the blast is re-derived per client, because a client re-rolling the effect
    /// would drift from what the server actually charged for.
    /// </para>
    /// <para>
    /// Damage is deliberately optional. A structure being demolished sets <c>damage</c> to zero and
    /// still gets the full blast, mesh launch, and burn-away; a bomb sets a radius and a figure and
    /// gets the same effect with teeth. Both go through this one component rather than a decorative
    /// explosion and a separate damaging one.
    /// </para>
    /// </summary>
    public sealed class ExplosionEffect : NetworkBehaviour {
        [Header("Damage")]
        [Tooltip("Metres. Entities within this distance of the blast are damaged. 0 disables damage entirely.")]
        [SerializeField, Min(0f)] float damageRadius;
        [Tooltip("Damage applied at the centre of the blast. Falls off linearly to zero at the radius.")]
        [SerializeField, Min(0)] long damage;
        [SerializeField] DamageType damageType = DamageType.Explosion;
        [Tooltip("Also damage entities on the source's own side. Off by default so a demolition " +
                 "does not wipe out the buildings around it.")]
        [SerializeField] bool damageOwnTeam;

        [Header("Presentation")]
        [Tooltip("Authored explosion VFX prefab, instantiated at the blast for every observer.")]
        [SerializeField] GameObject explosionVfxPrefab;
        [Tooltip("Uniform scale applied to the VFX. With Fit Vfx To Debris on, this multiplies the " +
                 "fitted size; otherwise it is the whole scale.")]
        [SerializeField, Min(0.01f)] float vfxScale = 1f;
        [Tooltip("Orientation the effects are spawned with. The project's effect packs author their " +
                 "plumes along +Z, as their own demo scenes show by placing them at -90 on X; spawned " +
                 "unrotated, the fire and smoke of a blast shot out sideways along the ground.")]
        [SerializeField] Vector3 vfxEulerAngles = new(-90f, 0f, 0f);
        [Tooltip("Draw the effects at the middle of the debris and sized to it, so one effect engulfs " +
                 "a fence and a hangar alike instead of flickering at the foot of either.")]
        [SerializeField] bool fitVfxToDebris = true;
        [Tooltip("Metres across the effects are authored at. A wreck this size plays them at Vfx Scale; " +
                 "a larger one scales them up to match.")]
        [SerializeField, Min(0.1f)] float vfxAuthoredSpan = 4f;
        [Tooltip("Optional. Lingering smoke left behind after the blast.")]
        [SerializeField] GameObject smokeVfxPrefab;
        [Tooltip("Optional. Report of the blast. Played at the blast position on every client.")]
        [SerializeField] AudioClip explosionAudio;
        [Tooltip("Optional. Spatial settings the report is played with. Authored on the prefab.")]
        [SerializeField] AudioSource audioSource;

        [Header("Debris")]
        [Tooltip("Visual subtree thrown into the air. A copy is launched; the original is hidden. " +
                 "Leave empty for a blast with no debris.")]
        [SerializeField] Transform debrisSource;
        [SerializeField, Min(0f)] float debrisUpwardForce = 9f;
        [SerializeField, Min(0f)] float debrisOutwardForce = 4f;
        [SerializeField, Min(0f)] float debrisSpin = 220f;
        [Tooltip("Seconds the launched debris takes to burn away to nothing after the blast.")]
        [SerializeField, Min(0.1f)] float debrisBurnSeconds = 3.5f;

        [Header("Lifecycle")]
        [Tooltip("Detonate automatically when this object's entity dies.")]
        [SerializeField] bool detonateOnDeath = true;
        [Tooltip("Seconds the server waits after the blast before despawning the object, so the " +
                 "replicated effect reaches every observer before its sender disappears.")]
        [SerializeField, Min(0f)] float despawnDelay = 0.5f;

        bool detonated;

        public bool HasDetonated => detonated;

#if UNITY_SERVER
        IEntity entity;

        public override void OnStartServer() {
            base.OnStartServer();
            if (!detonateOnDeath)
                return;

            entity = GetComponent<IEntity>();
            if (entity == null) {
                Debug.LogError(
                    $"{name} has an {nameof(ExplosionEffect)} set to detonate on death but no " +
                    $"{nameof(IEntity)} to die. Clear the flag or add the entity component.", this);
                return;
            }
            entity.OnDied += HandleDied;
        }

        public override void OnStopServer() {
            if (entity != null)
                entity.OnDied -= HandleDied;
            base.OnStopServer();
        }

        void HandleDied(DamageType source, IEntity attacker) =>
            Detonate(attacker, attacker != null ? attacker.Team : null);

        /// <summary>
        /// Fires the blast. <paramref name="source"/> and <paramref name="sourceTeam"/> are credited
        /// with any damage dealt and decide who counts as friendly; both may be null for a blast
        /// nobody is responsible for.
        /// </summary>
        [Server]
        public void Detonate(IEntity source = null, TeamConfig sourceTeam = null) {
            if (detonated || !IsServerStarted || !IsSpawned)
                return;

            detonated = true;
            Vector3 position = BlastPosition;

            if (damage > 0 && damageRadius > 0f)
                ApplyDamage(position, source, sourceTeam);

            PlayExplosion(position);

            if (despawnDelay >= 0f)
                StartCoroutine(DespawnAfterBlast());
        }

        void ApplyDamage(Vector3 position, IEntity source, TeamConfig sourceTeam) {
            Collider[] caught = Physics.OverlapSphere(
                position, damageRadius, ~0, QueryTriggerInteraction.Ignore);

            foreach (Collider collider in caught) {
                if (collider == null)
                    continue;

                IEntity target = collider.GetComponentInParent<IEntity>();
                if (target == null || target.IsDead || ReferenceEquals(target, source))
                    continue;
                if (!damageOwnTeam && !CombatTeams.AreEnemies(sourceTeam, target.Team))
                    continue;

                // Linear falloff, measured to the entity root rather than to the collider, so a
                // large building is not charged full damage because one corner clipped the radius.
                float distance = Vector3.Distance(position, ((UnityEngine.Component)target).transform.position);
                float scale = Mathf.Clamp01(1f - distance / damageRadius);
                long applied = (long)(damage * scale);
                if (applied > 0)
                    target.TakeDamage(applied, damageType, source);
            }
        }

        IEnumerator DespawnAfterBlast() {
            if (despawnDelay > 0f)
                yield return new WaitForSeconds(despawnDelay);
            if (IsSpawned)
                Despawn();
        }
#endif

        Vector3 BlastPosition => debrisSource != null ? debrisSource.position : transform.position;

        /// <summary>
        /// Draws the blast on every observer, and on the server, which no-ops there.
        /// <para>
        /// Only the position crosses the wire. Every other property of the explosion is authored on
        /// this prefab and therefore already identical on every machine.
        /// </para>
        /// </summary>
        [ObserversRpc(RunLocally = true, BufferLast = false)]
        void PlayExplosion(Vector3 position) {
            detonated = true;
#if !UNITY_SERVER
            // Where and how big to draw the blast is read from the model every machine already has,
            // so it needs nothing more on the wire. Measured before the debris hides the original.
            Vector3 centre = position;
            float scale = vfxScale;
            if (fitVfxToDebris && ExplosionDebris.TryGetVisualBounds(debrisSource, out Bounds bounds)) {
                centre = bounds.center;
                // Tall, thin things - a watchtower - count some of their height, or a forty-metre
                // tower would go up in a fireball sized for its four-metre footprint.
                float span = Mathf.Max(bounds.size.x, bounds.size.z, bounds.size.y * 0.6f);
                scale *= Mathf.Max(1f, span / vfxAuthoredSpan);
            }

            Quaternion rotation = Quaternion.Euler(vfxEulerAngles);
            SpawnVfx(explosionVfxPrefab, centre, rotation, scale);
            SpawnVfx(smokeVfxPrefab, centre, rotation, scale);

            if (explosionAudio != null) {
                if (audioSource != null && audioSource.isActiveAndEnabled)
                    RyanAssets.Client.ClientAudio.MusicService.CreateOneShot(audioSource, explosionAudio);
                else
                    RyanAssets.Client.ClientAudio.MusicService.CreateOneShot(explosionAudio, centre);
            }

            ExplosionDebris.Launch(
                debrisSource, centre, debrisUpwardForce, debrisOutwardForce, debrisSpin, debrisBurnSeconds);
#endif
        }

#if !UNITY_SERVER
        /// <summary>
        /// Instantiates one authored effect at the blast and cleans it up.
        /// <para>
        /// Play is called explicitly rather than relying on the effect's Play On Awake setting, so a
        /// prefab authored for manual triggering still fires. Play reaches the whole hierarchy, which
        /// matters because these effects are built from a root system with several child systems.
        /// The effect's own authored root scale is kept and multiplied, not replaced.
        /// </para>
        /// </summary>
        void SpawnVfx(GameObject prefab, Vector3 position, Quaternion rotation, float scale) {
            if (prefab == null)
                return;

            GameObject instance = Instantiate(prefab, position, rotation);
            instance.transform.localScale = prefab.transform.localScale * scale;
            if (instance.TryGetComponent(out ParticleSystem particles))
                particles.Play(true);
            Destroy(instance, debrisBurnSeconds + 2f);
        }
#endif
    }
}
