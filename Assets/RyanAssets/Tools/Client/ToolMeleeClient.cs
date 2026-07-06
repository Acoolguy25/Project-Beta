using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using System.Collections;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RyanAssets.Tools.Client {
    public class ToolMeleeClient : ToolBaseClient {
        protected override void Start() {
            base.Start();
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
        }
        protected override void OnUnequip(ToolBaseShared _) {
            base.OnUnequip(_);
        }
        protected override async void OnActivate() {
            base.OnActivate();
            var token = AddCancellationToken();
            SetAttacking(true);
            await UniTask.WaitForSeconds(0.5f, cancellationToken: token.Token).SuppressCancellationThrow();
            SetAttacking(false);
        }
        protected override void SetAttacking(bool attack) {
            base.SetAttacking(attack);
            animator.SetBool("MeleeAttack", attack);
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
