using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using System.Collections;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RyanAssets.Tools.Client {
    public class ToolMeleeClient : ToolBaseClient {
        protected override void Awake() {
            base.Awake();
            if (hitCollider == null) {
                Debug.LogError($"No hit collider found under {toolBaseShared.weaponRoot.name}.", toolBaseShared.weaponRoot);
                return;
            }
            ToolHitDetection hitDetection = hitCollider.gameObject.AddComponent<ToolHitDetection>();
            hitDetection.Init(toolBaseShared.connectedCharacter.transform);
            hitDetection.CollisionEntered += OnHit;
        }
        protected override void OnEquip(ToolBaseShared _) {
            base.OnEquip(_);
            animator.SetBool("KnifeHold", true);
        }
        protected override void OnUnequip(ToolBaseShared _) {
            base.OnUnequip(_);
            animator.SetBool("KnifeHold", false);
        }
        protected override async void OnActivate() {
            base.OnActivate();
            var token = AddCancellationToken();
            SetAttacking(true);
            SetCooldown(toolBaseShared.primaryCooldown);
            bool isCancelled = await UniTask.WaitForSeconds(0.717f, cancellationToken: token.Token).SuppressCancellationThrow();
            if (isCancelled)
                return;
            SetAttacking(false);
        }
        protected override void SetAttacking(bool attack) {
            base.SetAttacking(attack);
            animator.SetBool("KnifeAttack", attack);
        }
        protected override void SetLethalAttack(bool attack) {
            base.SetLethalAttack(attack);
            if (hitCollider != null)
                hitCollider.enabled = attack;
        }
        protected override void OnHit(Collider collider) {
            base.OnHit(collider);
            GameCharacter character = collider.transform.GetComponentInParent<GameCharacter>();
            if (character != null) {
                ((ToolMeleeShared)toolBaseShared).OnHit(character);
            }
        }
    }
}
