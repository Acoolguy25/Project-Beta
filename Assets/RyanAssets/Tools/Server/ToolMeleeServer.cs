using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Server {
    public class ToolMeleeServer : ToolBaseServer {
        protected override void Start() {
            base.Start();
            ((ToolMeleeShared)toolBaseShared).hitEvent += (gameCharacter) => {
                OnHit(gameCharacter.gameObject.GetComponent<GameCharacter>());
            };
        }
        protected override void OnEquip(ToolBaseShared _) {

        }
        protected override void OnUnequip(ToolBaseShared _) {

        }
        protected void OnHit(GameCharacter character) {
            character.TakeDamage(toolBaseShared.hitDamage, DamageSource.Firearm);
        }
    }
}