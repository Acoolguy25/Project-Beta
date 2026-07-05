using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Client {
    public class ToolMeleeClient : ToolBaseClient {
        protected override void Start() {
            base.Start();
            hitCollider.gameObject.AddComponent<ToolHitDetection>().CollisionEntered += OnHit;
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
            await Awaitable.WaitForSecondsAsync(0.5f, token.Token);
            SetAttacking(false);
        }
        protected override void SetAttacking(bool attack) {
            base.SetAttacking(attack);
            animator.SetBool("MeleeAttack", attack);
            hitCollider.enabled = attack;
        }
        protected override void OnHit(Collision collision) {
            base.OnHit(collision);
            if (collision.transform.TryGetComponent(out GameCharacter character)) {
                ((ToolMeleeShared)toolBaseShared).OnHit(character);
            }
        }
    }
}