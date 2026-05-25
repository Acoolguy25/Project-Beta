using UnityEngine;
using RyanAssets.PromptService;
using RyanAssets.Shared.Broadcasts;
using FishNet;
using FishNet.Transporting;

namespace RyanAssets.Client.ClientCore {
    public class ClientBroadcasts : MonoBehaviour
    {
        private void Awake(){
            InstanceFinder.ClientManager.RegisterBroadcast<PromptBroadcast>(OnPromptBroadcast);
        }
        private void OnPromptBroadcast(PromptBroadcast msg, Channel channel){
            PromptManager.Instance.PromptLocalUser(msg.title, msg.description, PromptId.ServerPromptBroadcast, PromptManager.ButtonPreset_OkOnly);
        }
    }
}