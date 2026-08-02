using Cysharp.Threading.Tasks;
using FishNet.Object;
#if !UNITY_SERVER
using RyanAssets.Input;
#endif
using RyanAssets.Tools.Shared;
using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace RyanAssets.Tools.Client {
    public class ToolGunClient : ToolBaseClient {
        protected ToolGunShared toolGunShared;
        public Func<Vector3> GetTargetPosition;
        protected override void Awake() {
            base.Awake();
            toolGunShared = (ToolGunShared) toolBaseShared;
        }
        protected override void OnEquip(ToolBaseShared _) {
            base.OnEquip(_);
            animator.SetBool("GunHold", true);
        }
        protected override void OnUnequip(ToolBaseShared _) {
            base.OnUnequip(_);
            animator.SetBool("GunHold", false);
        }

        protected override async void OnActivate(Vector3 targetLocation) {
            base.OnActivate(targetLocation);
            CancellationTokenSource cancellationTokenSource = AddCancellationToken();
            Debug.Assert(toolGunShared.currentAmmo > 0, $"ToolGun {toolGunShared.name} has no ammo to shoot!");
            SetCooldown(toolGunShared.FireRate);
            for (int i = 0; i < toolGunShared.BurstCount; i++) {
                if (!ConsumeAmmo(1))
                    break;
                if (i != 0)
                    targetLocation = RefreshTargetPosition();
                RaycastHit? hit = toolGunShared.Shoot(targetLocation);
                toolGunShared.VisualizeBulletLocally(hit);
                toolGunShared.VisualizeBullet(targetLocation);
                if (hit != null && hit.Value.transform != null) {
                    NetworkObject character = hit.Value.transform.GetComponentInParent<NetworkObject>();
                    if (character)
                        toolBaseShared.OnHit(character);
                }
                bool cancelled = await UniTask.WaitForSeconds(toolGunShared.BurstDelay, cancellationToken: cancellationTokenSource.Token).SuppressCancellationThrow();
                if (cancelled)
                    return;
            }
        }

        protected virtual Vector3 RefreshTargetPosition() {
#if UNITY_SERVER
            return GetTargetPosition();
#else
            return ToolControls.GetCursorWorldPosition();
#endif
        }
    }
}
