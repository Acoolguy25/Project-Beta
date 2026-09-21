using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A structure that defends itself: guard post or missile battery. Acquires through the same
    /// <see cref="WV_Combat"/> sweep the mobile units use, and stays inert until its construction
    /// finishes so a half-built battery cannot hold a line on its own.
    /// </summary>
    [RequireComponent(typeof(StructureComponent), typeof(WV_Constructable))]
    public sealed class WV_DefenseTurret : NetworkBehaviour {
        [Header("Weapon")]
        [SerializeField, Min(1f)] float range = 26f;
        [SerializeField, Min(1)] long damage = 30;
        [SerializeField, Min(0.1f)] float cooldown = 1.6f;
        [SerializeField] DamageType damageType = DamageType.Gun;
        [Tooltip("Only engages aircraft when true; otherwise engages ground targets only.")]
        [SerializeField] bool antiAir;

        [Header("Authored References")]
        [Tooltip("Yaw part that tracks the current target.")]
        [SerializeField] Transform turretYaw;
        [Tooltip("Pitch part, if the model has one. Optional.")]
        [SerializeField] Transform turretPitch;

        [Header("Tracking")]
        [SerializeField, Min(1f)] float traverseSpeed = 120f;
        [SerializeField, Min(0.1f)] float retargetInterval = 0.5f;

        /// <summary>Replicated so clients can aim the barrel at what the server is actually shooting.</summary>
        readonly SyncVar<Vector3> aimPoint = new();
        readonly SyncVar<bool> hasTarget = new();

        WV_Constructable constructable;
        StructureComponent structure;
        float nextFireTime;
        float nextRetargetTime;
        IEntity target;

        public float Range => range;
        public bool IsAntiAir => antiAir;

        void Awake() {
            constructable = GetComponent<WV_Constructable>();
            structure = GetComponent<StructureComponent>();
        }

        void Update() {
#if UNITY_SERVER
            TickServer();
#endif
            TrackVisually();
        }

        void TrackVisually() {
            if (!hasTarget.Value || turretYaw == null)
                return;

            Vector3 toTarget = aimPoint.Value - turretYaw.position;
            Vector3 flat = new(toTarget.x, 0f, toTarget.z);
            if (flat.sqrMagnitude > 0.001f) {
                turretYaw.rotation = Quaternion.RotateTowards(
                    turretYaw.rotation,
                    Quaternion.LookRotation(flat, Vector3.up),
                    traverseSpeed * Time.deltaTime);
            }

            if (turretPitch == null || toTarget.sqrMagnitude <= 0.001f)
                return;

            float pitch = -Mathf.Asin(Mathf.Clamp(toTarget.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            turretPitch.localRotation = Quaternion.RotateTowards(
                turretPitch.localRotation,
                Quaternion.Euler(pitch, 0f, 0f),
                traverseSpeed * Time.deltaTime);
        }

#if UNITY_SERVER
        void TickServer() {
            if (!IsServerStarted || !constructable.IsOperational || structure.IsDead) {
                hasTarget.Value = false;
                target = null;
                return;
            }

            float now = NetworkHelper.ServerTime;
            if (!WV_Combat.IsValidTarget(target, structure.Team) || now >= nextRetargetTime) {
                nextRetargetTime = now + retargetInterval;
                target = AcquireTarget();
            }

            if (target == null || !WV_Combat.TryGetPosition(target, out Vector3 targetPosition)) {
                hasTarget.Value = false;
                return;
            }

            hasTarget.Value = true;
            aimPoint.Value = targetPosition;

            if (now < nextFireTime || WV_Combat.DistanceTo(transform.position, target) > range)
                return;

            nextFireTime = now + cooldown;
            WV_Combat.DealDamage(target, damage, damageType, structure);
        }

        IEntity AcquireTarget() {
            IEntity candidate = WV_Combat.FindNearestEnemy(
                transform.position, range, structure.Team, preferCharacters: true, ignoreRoot: transform);

            // A battery built for one altitude band should not waste its cooldown on the other.
            if (candidate is Component component && component != null) {
                WV_Unit unit = component.GetComponent<WV_Unit>();
                bool candidateIsAir = unit != null && unit.IsAircraft;
                if (candidateIsAir != antiAir)
                    return null;
            } else if (antiAir) {
                return null;
            }

            return candidate;
        }
#endif
    }
}
