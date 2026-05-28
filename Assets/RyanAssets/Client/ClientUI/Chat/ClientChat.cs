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
using RyanAssets.Characters;
using TMPro;

namespace RyanAssets.Client.ClientUI.Chat {
    public class ClientChat : ListGridUI<MessageBroadcast> {
        [SerializeField]
        TMP_InputField chatBox;
        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnCreateMessage;
            InstanceFinder.ClientManager.RegisterBroadcast<MessageBroadcast>(OnReceiveMessage);
            chatBox.onSubmit.AddListener(OnMessageSend_ButtonPressed);
            ClearPrefabs();
        }
        private void OnReceiveMessage(MessageBroadcast message, Channel channel) {
            AddPrefab(message);
        }
        private void OnCreateMessage(GameObject prefab, MessageBroadcast message) {
            TextMeshProUGUI usernameText = prefab.GetComponent<TextMeshProUGUI>();
            string username = SharedGlobalEvents.Instance.Players[message.player].data.username;
            usernameText.text = $"{ClientChatHelper.ColorNameRichText(username)}: {ClientChatHelper.EscapeRichText(message.message)}";
        }
        private void OnEnable() {
            SharedInputController.chatActivateEvent += OnChatToggle;
        }
        private void OnDisable() {
            SharedInputController.chatActivateEvent -= OnChatToggle;
        }
        private void OnChatToggle() {
            chatBox.Select();
        }
        public void OnMessageSend_ButtonPressed(string text) {
            if (text.Length > 0)
                InstanceFinder.ClientManager.Broadcast<MessageRequest>(new() { message = text });
        }
        public void OnChatSelected() {
            SharedInputController.Instance.SetControlsEnabled("Player", false);
        }
        public void OnChatUnselected() {
            SharedInputController.Instance.SetControlsEnabled("Player", true);
        }
    }
}