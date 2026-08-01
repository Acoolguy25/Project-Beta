using FishNet;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using RyanAssets.Tools.Client;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Server {
    public class ToolGunServer : ToolBaseServer {
        protected override void Start() {
            base.Start();
        }
        protected override void OnEquip(ToolBaseShared _) {

        }
        protected override void OnUnequip(ToolBaseShared _) {

        }
        protected override void OnHit(GameCharacter character) {
            character.TakeDamage(toolBaseShared.hitDamage, DamageSource.Gun, toolBaseShared.NetworkObject);
        }
    }
}