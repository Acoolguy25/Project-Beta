using UnityEngine;
using RyanAssets.PromptService;
using RyanAssets.Shared.Broadcasts;
using FishNet;
using FishNet.Transporting;

namespace RyanAssets.Client.ClientCore {
    public class ClientBroadcasts : MonoBehaviour {
        public static ClientBroadcasts Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            InstanceFinder.ClientManager.RegisterBroadcast<PromptBroadcast>(OnPromptBroadcast);
        }

        private void OnDestroy() {
            if (Instance != this)
                return;

            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<PromptBroadcast>(OnPromptBroadcast);

            Instance = null;
        }

        private void OnPromptBroadcast(PromptBroadcast msg, Channel channel) {
            Debug.Log($"Received prompt broadcast: {msg.title}");
            PromptManager.Instance.PromptLocalUser(msg.title, msg.description, PromptId.ServerPromptBroadcast, PromptManager.ButtonPreset_OkOnly);
        }
    }
}
