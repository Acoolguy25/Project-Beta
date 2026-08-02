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
        public int Accuracy = 300; // 0.0 to 1.0, where 1.0 is perfect accuracy
        [SerializeField]
        public float MaxRange = 30f;
        [SerializeField]
        public float FireRate = 0.3f;

        [Header("Gun Fire Mode")]
        [SerializeField]
        public int BurstCount = 1;
        [SerializeField]
        public float BurstDelay = 0.1f;

        public ParticleSystem FireParticleSystem;
        static LayerMask hitLayers;
        protected override void Awake() {
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
            Vector3 dir = GetSpreadDirection(origin, targetLocation, UnityEngine.Random.Range(Accuracy / 360f, 1f));
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
