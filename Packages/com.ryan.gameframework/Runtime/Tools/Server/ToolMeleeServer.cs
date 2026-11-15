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
            // if (toolBaseShared.IsController)
            //     toolBaseShared.connectedCharacter.GetComponent<GameCharacter>().SwitchTool(toolBaseShared);
        }
        protected override void OnEquip(ToolBaseShared _) {

        }
        protected override void OnUnequip(ToolBaseShared _) {

        }
        //protected override void OnHit(GameCharacter character) {
            //Destroy(character.gameObject);
            //InstanceFinder.ServerManager.Despawn(character.gameObject);
        //}
        //void Update() {
        //if (toolBaseShared.IsController) {
        //    GetComponent<ToolBaseClient>().TryActivate();
        //}
        //}
    }
}