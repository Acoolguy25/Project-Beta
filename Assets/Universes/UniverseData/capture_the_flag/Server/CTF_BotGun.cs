using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// The weapon half of a Flag Wars bot: it holds the shared pistol tool tuned as its gun and fires
    /// it through the tool's own path, so a bot's shots trace, hit, replicate and show exactly like a
    /// player's (the same pattern as War Valley's gunners). <see cref="CTF_Bot"/> decides what to aim at.
    /// </summary>
    [RequireComponent(typeof(GameCharacter))]
    public sealed class CTF_BotGun : MonoBehaviour {
        /// <summary>How long the authored firing pose is held after a shot.</summary>
        [SerializeField, Min(0.05f)] float firePoseSeconds = 0.2f;

        GameCharacter character;
        Animator animator;
        ToolBaseShared tool;
        ToolGunShared gun;
        ToolGunClient gunClient;
        CTF_GunStats stats;
        GameCharacter target;
        Vector3 aimPoint;
        float firePoseUntil;
        bool armed;

        public bool Armed => armed && gun != null && gunClient != null;
        /// <summary>How far this gun reaches (metres).</summary>
        public float Range => stats.range;

        void Awake() {
            character = GetComponent<GameCharacter>();
            animator = GetComponent<Animator>();
        }

        /// <summary>Takes a spawned pistol tool as this bot's gun with <paramref name="gunStats"/> (replicated values already set by the squad).</summary>
        public void Arm(ToolBaseShared pistol, CTF_GunStats gunStats) {
            tool = pistol;
            stats = gunStats.Validated();
            gun = pistol as ToolGunShared;
            gunClient = pistol != null ? pistol.GetComponent<ToolGunClient>() : null;
            if (gun == null || gunClient == null) {
                Debug.LogError($"{name}: the pistol tool has no {nameof(ToolGunShared)}/{nameof(ToolGunClient)}; this bot cannot shoot. " +
                               "The tool prefab's clientScript must name ToolGunClient.", this);
                return;
            }
            CTF_GunSetup.ApplyFiringPattern(gun, stats, refill: true);
            // A burst or a scattergun blast asks for the target again on every round after the first.
            gunClient.GetTargetPosition = () => aimPoint;
            gunClient.CanFireShot = CanFireRound;
            armed = true;
            // Hold it from the start: a bot is always armed, like a player who spawns with the gun out.
            character.SwitchTool(tool);
        }

        /// <summary>Whether a round aimed at <paramref name="point"/> would reach <paramref name="victim"/> first (no wall, no teammate in the way).</summary>
        public bool HasClearShot(GameCharacter victim, Vector3 point) =>
            Armed && victim != null && gun.HasClearShot(point, victim.transform);

        /// <summary>
        /// Pulls the trigger at <paramref name="point"/> on <paramref name="victim"/> when the gun is ready
        /// (it reloads by itself when empty). Returns true when a round left the barrel.
        /// </summary>
        public bool TryFire(GameCharacter victim, Vector3 point) {
            if (!Armed || victim == null || character.IsDead) return false;
            target = victim;
            aimPoint = point;
            if (character.ActiveTool.Value != tool) character.SwitchTool(tool);
            if (!HasClearShot(victim, point)) return false;
            int before = tool.currentAmmo;
            gunClient.TryActivate(point);
            // The tool decides whether this became a shot, a reload or nothing; a spent round is the proof.
            bool fired = tool.currentAmmo != before;
            if (fired) SetFirePose(true);
            return fired;
        }

        /// <summary>Later rounds of a burst stop when the target is gone or dead.</summary>
        bool CanFireRound() => Armed && !character.IsDead && target != null && !target.IsDead;

        void Update() {
            if (firePoseUntil > 0f && Time.time >= firePoseUntil) SetFirePose(false);
        }

        /// <summary>The character controller's authored firing pose; it reaches clients through the NetworkAnimator.</summary>
        void SetFirePose(bool firing) {
            firePoseUntil = firing ? Time.time + firePoseSeconds : 0f;
            if (animator != null) animator.SetBool("GunFire", firing);
        }

        void OnDestroy() {
            if (gunClient == null) return;
            gunClient.GetTargetPosition = null;
            gunClient.CanFireShot = null;
        }
    }
}
