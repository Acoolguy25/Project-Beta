using FishNet;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using RyanAssets.Tools.Client;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Server {
    public class ToolMeleeServer : ToolBaseServer {
        protected override void Start() {
            base.Start();
            ((ToolMeleeShared)toolBaseShared).hitEvent += (gameCharacter) => {
                OnHit(gameCharacter.gameObject.GetComponent<GameCharacter>());
            };
            if (toolBaseShared.IsController)
                toolBaseShared.connectedCharacter.GetComponent<GameCharacter>().SwitchTool(toolBaseShared);
        }
        protected override void OnEquip(ToolBaseShared _) {

        }
        protected override void OnUnequip(ToolBaseShared _) {

        }
        protected void OnHit(GameCharacter character) {
            character.TakeDamage(toolBaseShared.hitDamage, DamageSource.Melee, toolBaseShared.NetworkObject);
            //Destroy(character.gameObject);
            //InstanceFinder.ServerManager.Despawn(character.gameObject);
        }
        void Update() {
            if (toolBaseShared.IsController) {
                GetComponent<ToolBaseClient>().TryActivate();
            }
        }
    }
}