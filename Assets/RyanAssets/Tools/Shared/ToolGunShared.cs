using FishNet.Object;
using System;

#if !UNITY_SERVER
    using RyanAssets.Clients.ClientEffects;
#endif
using UnityEngine;
using RpcGen;

namespace RyanAssets.Tools.Shared { 
    public partial class ToolGunShared : ToolBaseShared {
        [Header("Gun Stats")]
        [SerializeField]
        public int BestAccuracy = 240; // 0.0 to 360.0, where 0.0 is perfect accuracy
        [SerializeField]
        public int WorstAccuracy = 300; // 0.0 to 360.0, where 0.0 is perfect accuracy
        [SerializeField]
        public float MaxRange = 30f;
        [SerializeField]
        public float FireRate = 0.3f;
        [SerializeField, Min(0.01f)]
        public float MuzzleCollisionRadius = 0.1f;

        [Header("Gun Fire Mode")]
        [SerializeField]
        public int BurstCount = 1;
        [SerializeField]
        public float BurstDelay = 0.1f;

        public ParticleSystem FireParticleSystem;
        static LayerMask hitLayers;
        protected override void Awake() {
            base.Awake();
            hitLayers = ~LayerMask.GetMask("Ignore Raycast");
        }
        public RaycastHit? Shoot(Vector3 targetLocation) {
            //Debug.DrawRay(
            //    weaponRoot.transform.position,
            //    (targetLocation - weaponRoot.transform.position).normalized * MaxRange,
            //    Color.red,
            //    2f
            //);
            Vector3 origin = weaponRoot.transform.position;
            if (targetLocation == origin)
                return null; // safety check!
            if (TryGetMuzzleObstruction(origin, out RaycastHit muzzleHit))
                return muzzleHit; // The muzzle is inside a solid object; never raycast past it.
            Debug.DrawLine(origin, origin + Vector3.forward*0.3f, Color.red, 2f);

            Vector3 dir = GetSpreadDirection(origin, targetLocation, UnityEngine.Random.Range(WorstAccuracy / 360f, BestAccuracy / 360f));
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, MaxRange, hitLayers);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits) {
                if (h.transform != null && h.transform.root != null && !h.transform.IsChildOf(connectedCharacter.transform)) {
                    return h;
                }
            }
            return new RaycastHit {
                point = origin + dir * MaxRange,
                normal = -dir,
                distance = MaxRange,
            };
        }
        bool TryGetMuzzleObstruction(Vector3 origin, out RaycastHit hit) {
            // Raycasts do not reliably report a collider when their origin is already inside it.
            // Check the muzzle volume first, ignoring the wielder and the weapon itself.
            Collider[] overlaps = Physics.OverlapSphere(
                origin,
                MuzzleCollisionRadius,
                hitLayers,
                QueryTriggerInteraction.Ignore);

            foreach (Collider overlap in overlaps) {
                if (IsShooterOrWeaponCollider(overlap))
                    continue;

                Vector3 closestPoint = overlap.ClosestPoint(origin);
                Vector3 normal = origin - closestPoint;
                if (normal.sqrMagnitude < 0.0001f)
                    normal = -weaponRoot.transform.forward;

                // RaycastHit cannot be constructed with a Collider, but a hit with no
                // transform is intentionally non-damaging and stops the bullet visual here.
                hit = new RaycastHit {
                    point = closestPoint,
                    normal = normal.normalized,
                    distance = 0f,
                };
                return true;
            }

            hit = default;
            return false;
        }
        bool IsShooterOrWeaponCollider(Collider collider) {
            Transform colliderTransform = collider.transform;
            return colliderTransform.IsChildOf(transform) ||
                   (connectedCharacter != null && colliderTransform.IsChildOf(connectedCharacter.transform));
        }
        Vector3 GetSpreadDirection(Vector3 origin, Vector3 targetPosition, float accuracy) {
            Vector3 baseDir = (targetPosition - origin).normalized;
            float spreadAngle = (1f - accuracy) * 15f;

            Vector3 randomAxis = Vector3.Cross(baseDir, UnityEngine.Random.onUnitSphere);
            if (randomAxis.sqrMagnitude < 0.0001f)
                randomAxis = Vector3.Cross(baseDir, Vector3.up); // fallback

            Quaternion spread = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, spreadAngle), randomAxis.normalized);
            return spread * baseDir;
        }
        //[ObserversRpc(ExcludeOwner = true)]
        //public void VisualizeBulletRpc(Vector3 targetLocation) {
        //    VisualizeBullet(Shoot(targetLocation));
        //}
        public void VisualizeBulletLocally(RaycastHit? hit) {
            if (hit == null)
                return;
#if !UNITY_SERVER
            //GunVisualEffects.VisualizeBullet(hit.Value, weaponRoot.transform.position);
            GunVisualEffects.VisualizeBullet(hit.Value, FireParticleSystem.transform.position, FireParticleSystem);
#endif
            PlayAudio(attackAudio);
        }
        //[ServerRpc]
        //public void ShootServerRpc(Vector3 targetLocation) {
        //    VisualizeBulletRpc(targetLocation);
        //}
        [SharedRpc(RunOnServer = true, RunOnCallingClient = false)]
        public void VisualizeBullet(Vector3 targetLocation) {
            VisualizeBulletLocally(Shoot(targetLocation));
        }
    }
}
