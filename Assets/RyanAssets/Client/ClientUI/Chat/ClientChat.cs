using UnityEngine;
using FishNet;
using RyanAssets.Shared.Broadcasts;
using RyanAssets.Shared.Requests;
using RyanAssets.Shared.Player;
using RyanAssets.DataService;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.UI.ListGrid;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI {
    public class ClientChat : ListGridUI<MessageBroadcast> {
        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnCreateMessage;
            InstanceFinder.ClientManager.RegisterBroadcast<MessageBroadcast>(OnReceiveMessage);
            ClearPrefabs();
        }
        private void OnReceiveMessage(MessageBroadcast message, Channel channel) {
            AddPrefab(message);
        }
        private void OnCreateMessage(GameObject prefab, MessageBroadcast message) {
            Text usernameText = prefab.transform.GetChild(0).GetComponent<Text>();
            Text messageText = prefab.transform.GetChild(1).GetComponent<Text>();
            usernameText.text = SharedGlobalEvents.Instance.Players[message.player].data.username;
            messageText.text = message.message;
        }
    }
}