using Cysharp.Threading.Tasks;
using FishNet.Object;
using RyanAssets.Tools.Shared;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace RyanAssets.Tools.Client {
    public class ToolGunClient : ToolBaseClient {
        protected ToolGunShared toolGunShared;
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

                RaycastHit? hit = toolGunShared.Shoot(targetLocation);
                toolGunShared.VisualizeBullet(hit);
                toolGunShared.ShootServerRpc(targetLocation);
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
    }
}