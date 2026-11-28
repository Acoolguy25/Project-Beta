using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using UnityEngine;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// An NPC dealt the sheriff. It is not told who the murderer is: it wanders like everyone else with the
    /// gun holstered until it sees a murderer with the knife out, sees (or hears, close by) a kill, then
    /// reacts after a human-like delay, draws and shoots with an aim that drifts with distance. It forgets
    /// a murderer it loses sight of for long enough. A stray bullet that kills an innocent costs it its
    /// life, exactly as it would a player.
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter))]
    public sealed class MM_NpcSheriff : MonoBehaviour {
        const float ScanInterval = 0.2f;
        const float HolsterAfterSeconds = 2.5f;
        const float FireAnimationSeconds = 0.3f;
        // Keeps the sheriff from walking into the murderer's knife while it takes aim.
        const float StandoffDistance = 9f;
        const float AttackInterval = 0.4f;

        MM_ClassicRound round;
        MM_ClassicTuning tuning;
        LocalNPC npc;
        GameCharacter self;
        Animator animator;
        ToolGunShared gun;
        ToolGunClient gunClient;

        GameCharacter suspect;
        float suspectSeenAt, fireAllowedAt, nextScan, lastShotAt = float.NegativeInfinity, fireAnimationUntil;
        Vector3 aimPoint;
        bool drawn;

        void Awake() {
            npc = GetComponent<LocalNPC>();
            self = GetComponent<GameCharacter>();
            animator = GetComponent<Animator>();
        }

        /// <summary>Called once by the runner after the NPC has been armed with the sheriff's gun.</summary>
        public void Configure(MM_ClassicRound classicRound, MM_ClassicTuning classicTuning, ToolBaseShared pistol) {
            round = classicRound;
            tuning = classicTuning;
            gun = pistol as ToolGunShared;
            gunClient = pistol != null ? pistol.GetComponent<ToolGunClient>() : null;
            if (gun == null || gunClient == null) {
                Debug.LogError($"{name} was dealt the sheriff but its gun cannot be fired by an NPC ({nameof(ToolGunClient)} missing).", this);
                enabled = false;
                return;
            }
            gunClient.GetTargetPosition = () => aimPoint;
            // The sheriff only shoots who it has identified, never whoever the auto-targeting would pick.
            npc.AutomaticTargeting = false;
            npc.AttackDamageType = pistol.defaultDamageType;
            // A target beyond the gun's own range can never be hit, so never stand off further than it reaches.
            float fireRange = Mathf.Min(tuning.npcSheriffFireRange, gun.MaxRange * 0.95f);
            npc.ConfigureAttackRange(StandoffDistance, fireRange, holdDistance: true, AttackInterval);
            npc.AttackFunction = Fire;
            round.MurderCommitted += OnMurder;
        }

        void OnDestroy() {
            if (round != null)
                round.MurderCommitted -= OnMurder;
            if (npc != null && npc.AttackFunction == Fire)
                npc.AttackFunction = null;
            if (gunClient != null)
                gunClient.GetTargetPosition = null;
        }

        /// <summary>A murder happened somewhere: this sheriff knows who did it if it saw it, or heard it close by.</summary>
        void OnMurder(GameCharacter murderer, GameCharacter victim) {
            if (!enabled || self.IsDead || murderer == null || murderer.IsDead)
                return;
            float distance = Vector3.Distance(transform.position, murderer.transform.position);
            bool heard = distance <= tuning.npcSheriffHearingDistance;
            bool saw = distance <= tuning.npcSheriffSightDistance
                && MM_Sight.InView(self, murderer, tuning.npcSheriffViewAngle)
                && MM_Sight.HasLineOfSight(self, murderer);
            if (heard || saw)
                Identify(murderer);
        }

        void Identify(GameCharacter murderer) {
            if (suspect != murderer) {
                suspect = murderer;
                Vector2 reaction = tuning.npcSheriffReactionSeconds;
                fireAllowedAt = Time.time + Random.Range(Mathf.Min(reaction.x, reaction.y), Mathf.Max(reaction.x, reaction.y));
            }
            suspectSeenAt = Time.time;
        }

        void Update() {
            if (round == null || self.IsDead)
                return;
            if (Time.time >= nextScan) {
                nextScan = Time.time + ScanInterval;
                Scan();
            }
            if (suspect != null && (suspect.IsDead || Time.time - suspectSeenAt > tuning.npcSheriffMemorySeconds))
                Forget();
            if (suspect != null && Time.time >= fireAllowedAt && npc.CurrentAttackTarget != suspect)
                npc.TargetCharacter(suspect);
            if (fireAnimationUntil > 0f && Time.time >= fireAnimationUntil) {
                fireAnimationUntil = 0f;
                animator.SetBool("GunFire", false);
            }
            if (drawn && suspect == null && Time.time - lastShotAt > HolsterAfterSeconds) {
                drawn = false;
                self.SwitchTool(null);
            }
        }

        /// <summary>Looks for a murderer giving themselves away, and keeps track of the one it already knows.</summary>
        void Scan() {
            if (suspect != null && MM_Sight.HasLineOfSight(self, suspect)
                && Vector3.Distance(transform.position, suspect.transform.position) <= tuning.npcSheriffSightDistance) {
                suspectSeenAt = Time.time;
                return;
            }
            foreach (GameCharacter murderer in round.LivingWithRole(MM_Role.Murderer)) {
                if (!round.IsVisibleThreatFrom(murderer, transform.position))
                    continue;
                if (Vector3.Distance(transform.position, murderer.transform.position) > tuning.npcSheriffSightDistance)
                    continue;
                if (MM_Sight.InView(self, murderer, tuning.npcSheriffViewAngle) && MM_Sight.HasLineOfSight(self, murderer)) {
                    Identify(murderer);
                    return;
                }
            }
        }

        void Forget() {
            suspect = null;
            if (npc.TargetingType == NPCTargetingType.Attack)
                npc.SetTargetingType(NPCTargetingType.Random);
        }

        /// <summary>Called by LocalNPC on its attack interval while the suspect is in range.</summary>
        void Fire(GameCharacter target) {
            if (target == null || target != suspect || Time.time < fireAllowedAt || self.IsDead)
                return;
            float distance = Vector3.Distance(transform.position, target.transform.position);
            // The aim drifts further the further away the target is, so long shots can miss.
            aimPoint = MM_Sight.Chest(target) + Random.insideUnitSphere * (tuning.npcSheriffAimErrorPer10m * distance / 10f);
            drawn = true;
            // Equipping is idempotent; the first call of an engagement only draws the gun.
            self.SwitchTool(gun);
            if (!gun.HasClearShot(aimPoint, target.transform))
                return;
            int ammoBefore = gun.currentAmmo;
            gunClient.TryActivate(aimPoint);
            if (gun.currentAmmo != ammoBefore) {
                lastShotAt = Time.time;
                fireAnimationUntil = Time.time + FireAnimationSeconds;
                animator.SetBool("GunFire", true);
            }
        }
    }
}
