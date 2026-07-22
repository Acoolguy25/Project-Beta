using RyanAssets.Characters.Client;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Stamina {
    public static class StaminaToolHelper {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            ToolBaseShared.createStaticEvent += AddTool;
        }
        static void AddTool(ToolBaseShared toolBaseShared) {
            if (!toolBaseShared.IsOwner)
                return;
            var toolBaseClient = toolBaseShared.GetComponent<ToolBaseClient>();
            toolBaseClient.CanActivateEvent = () => {
                return (LocalPlayer.Character.ConsumeStamina(toolBaseShared.staminaCost));
            };
        }
    }
}