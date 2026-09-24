using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// What a War Valley foot soldier does when it is told to attack something.
    /// <para>
    /// Wave NPCs and the troops a commander trains are the same character carrying a different tool,
    /// so the weapon half lives here and the two brains above it - <see cref="WV_NPC"/> for the wave
    /// and <see cref="WV_TroopBrain"/> for a player's squad - only decide what to point it at.
    /// </para>
    /// <para>
    /// A gun is not a slower knife. It has to be held at a distance, and the engagement band
    /// <see cref="LocalNPC"/> moves inside is re-tuned for it here rather than being authored on a
    /// prefab, because the same character prefab is spawned for both loadouts.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter))]
    public sealed class WV_NpcCombat : MonoBehaviour {
        /// <summary>A knife is put away shortly after a swing so the troop walks unarmed.</summary>
        const float KnifeUnequipDelay = 1f;

        /// <summary>
        /// A gun stays out for as long as there is something to shoot, and is only holstered once the
        /// fight is over. Re-drawing it between shots would restart the hold animation every second.
        /// </summary>
        const float GunUnequipDelay = 4f;

        /// <summary>
        /// How long the authored firing pose is held after a shot. The character controller already
        /// carries a GunFire state that nothing drove; a shot is what it was authored for.
        /// </summary>
        const float GunFireAnimationSeconds = 0.2f;

        /// <summary>Height above a soldier's feet its shots leave from, for the line-of-sight check.</summary>
        const float EyeHeight = 1.4f;

        LocalNPC localNPC;
        GameCharacter gameCharacter;
        CharacterAnimator characterAnimator;
        Animator animator;

        WV_TroopKind kind = WV_TroopKind.Knife;
        ToolBaseShared weapon;
        ToolGunClient gunClient;
        IEntity pendingAttackTarget;
        Vector3 aimPoint;
        float lastAttack = float.MinValue;
        float gunFireUntil;
        bool loadoutEquipped;
        /// <summary>True while a wall blocks the shot and the gunner is closing in to get a clear one.</summary>
        bool advancingForSight;

        public WV_TroopKind Kind => kind;

        /// <summary>The far edge of this soldier's engagement band, in metres.</summary>
        public float EngageRange => localNPC != null ? localNPC.AttackMaxRange : 0f;

        /// <summary>
        /// Adds the combat half to a freshly spawned character and chooses its loadout. Called before
        /// the brain is attached so the weapon exists by the time the brain hands it a target.
        /// </summary>
        public static WV_NpcCombat Attach(GameObject character, WV_TroopKind kind) {
            WV_NpcCombat combat = character.GetComponent<WV_NpcCombat>();
            if (combat == null)
                combat = character.AddComponent<WV_NpcCombat>();
            combat.kind = kind;
            return combat;
        }

        void Awake() {
            localNPC = GetComponent<LocalNPC>();
            gameCharacter = GetComponent<GameCharacter>();
            characterAnimator = GetComponent<CharacterAnimator>();
            animator = GetComponent<Animator>();

            characterAnimator.LethalAttackStarted += HandleLethalAttackStarted;
            characterAnimator.LethalAttackEnded += HandleLethalAttackEnded;
            localNPC.AttackEntityFunction = AttackEntity;
        }

        void Start() {
            EquipLoadout();
        }

        void EquipLoadout() {
            ToolEnum tool = kind == WV_TroopKind.Gunner ? ToolEnum.Pistol : ToolEnum.Dagger;
            weapon = ServerTool.Instance.SpawnTool(gameCharacter.NetworkObject, tool);
            if (weapon == null) {
                Debug.LogError($"{name} could not be issued a {tool} and will not be able to fight.", this);
                return;
            }

            localNPC.AttackDamageType = weapon.defaultDamageType;
            if (kind != WV_TroopKind.Gunner)
                return;

            gunClient = weapon.GetComponent<ToolGunClient>();
            if (gunClient == null) {
                Debug.LogError(
                    $"{weapon.name} has no {nameof(ToolGunClient)}, so {name} cannot fire it. The tool " +
                    "prefab's clientScript must name it.", this);
                return;
            }

            // The tool re-reads this every shot after the first of a burst, so it has to resolve to
            // wherever the current target actually is rather than to a captured point.
            gunClient.GetTargetPosition = () => aimPoint;

            ConfigureGunnerRange();
        }

        /// <summary>
        /// A pistol out-ranges a knife by an order of magnitude, so the whole movement band moves
        /// with it: stand off at GunnerStandoffRange, open fire from GunnerEngageRange, and give
        /// ground rather than let anything walk inside the standoff.
        /// </summary>
        void ConfigureGunnerRange() {
            localNPC.ConfigureAttackRange(
                WV_Rules.GunnerStandoffRange,
                WV_Rules.GunnerEngageRange,
                holdDistance: true,
                WV_Rules.GunnerAttackInterval);
        }

        /// <summary>
        /// A gun cannot fire through a wall, fence, or closed gate. While one is in the way the
        /// gunner stops holding its distance and keeps closing on the target along its NavMesh
        /// route - around the wall, or up to it - until the line clears, then settles back at range.
        /// </summary>
        void SetAdvancingForSight(bool advancing) {
            if (advancingForSight == advancing)
                return;
            advancingForSight = advancing;
            if (advancing)
                localNPC.ConfigureAttackRange(0f, WV_Rules.GunnerEngageRange, holdDistance: false, WV_Rules.GunnerAttackInterval);
            else
                ConfigureGunnerRange();
        }

        void Update() {
            if (weapon == null)
                return;

            if (kind == WV_TroopKind.Gunner) {
                if (gunFireUntil > 0f && Time.time >= gunFireUntil)
                    SetFiring(false);

                // Holster only once there is nothing left to shoot at, so the hold pose survives the
                // gap between shots and the reload.
                bool engaged = localNPC.CurrentAttackEntityTarget != null;
                if (loadoutEquipped && !engaged && lastAttack + GunUnequipDelay <= Time.time) {
                    loadoutEquipped = false;
                    SetFiring(false);
                    gameCharacter.SwitchTool(null);
                }
                return;
            }

            if (lastAttack + KnifeUnequipDelay <= Time.time) {
                pendingAttackTarget = null;
                loadoutEquipped = false;
                gameCharacter.SwitchTool(null);
            }
        }

        /// <summary>Invoked by <see cref="LocalNPC"/> on its own cooldown once a target is in band.</summary>
        void AttackEntity(IEntity target) {
            if (weapon == null || target == null || gameCharacter.IsDead)
                return;

            if (kind == WV_TroopKind.Gunner)
                FireGun(target);
            else
                SwingKnife(target);
        }

        void SwingKnife(IEntity target) {
            lastAttack = Time.time;
            pendingAttackTarget = target;
            loadoutEquipped = true;
            gameCharacter.SwitchTool(weapon);
            animator.SetBool("KnifeAttack", true);
        }

        void FireGun(IEntity target) {
            if (gunClient == null)
                return;

            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            if (!WV_Combat.HasLineOfSight(eye, target, transform)) {
                SetAdvancingForSight(true);
                return;
            }
            SetAdvancingForSight(false);

            // A shield is shot at its near edge. Its dome does not stop bullets, so the hit is
            // applied to it directly rather than left to whatever the round goes on to strike.
            WV_ShieldBarrier shield = target as WV_ShieldBarrier;
            if (shield != null)
                aimPoint = shield.GetEdgePoint(eye);
            else if (!WV_Combat.TryGetAimPoint(target, out aimPoint))
                return;

            lastAttack = Time.time;
            loadoutEquipped = true;
            // Equipping is idempotent, so this only does work on the first shot of an engagement.
            gameCharacter.SwitchTool(weapon);

            // The tool refuses to fire while holstered, and the equip above only takes effect once
            // the switch has actually run, so a first call on the frame the weapon is drawn is
            // deliberately allowed to be a no-op rather than being forced through.
            int ammoBefore = weapon.currentAmmo;
            gunClient.TryActivate(aimPoint);

            // The tool decides for itself whether this call became a shot, a reload, or nothing at
            // all. A spent round is the only honest evidence that one was actually fired, so the
            // firing pose follows it rather than following the attempt.
            bool fired = weapon.currentAmmo < 0 || weapon.currentAmmo != ammoBefore;
            if (fired)
                SetFiring(true);
            if (fired && shield != null)
                WV_Combat.DealDamage(shield, weapon.hitDamage, weapon.defaultDamageType, gameCharacter);
        }

        /// <summary>
        /// Drives the authored firing pose. Set on the server, it reaches every client through the
        /// character's NetworkAnimator, the same way the knife swing does.
        /// </summary>
        void SetFiring(bool firing) {
            gunFireUntil = firing ? Time.time + GunFireAnimationSeconds : 0f;
            animator.SetBool("GunFire", firing);
        }

        void HandleLethalAttackStarted() {
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

        void HandleLethalAttackEnded() {
            animator.SetBool("KnifeAttack", false);
        }

        void OnDestroy() {
            if (characterAnimator != null) {
                characterAnimator.LethalAttackStarted -= HandleLethalAttackStarted;
                characterAnimator.LethalAttackEnded -= HandleLethalAttackEnded;
            }
            if (localNPC != null && localNPC.AttackEntityFunction == AttackEntity)
                localNPC.AttackEntityFunction = null;
            if (gunClient != null)
                gunClient.GetTargetPosition = null;
        }
    }
}
