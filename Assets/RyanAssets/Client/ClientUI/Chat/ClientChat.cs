using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Client.ClientCore;
using RyanAssets.Client.ClientUI.Topbar;
using RyanAssets.DataService;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Requests;
using RyanAssets.UI;
using RyanAssets.UI.Textbox;
using RyanAssets.UI.ListGrid;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;

namespace RyanAssets.Client.ClientUI.Chat {
    public struct LocalChatMessage : IBroadcast {
        public NetworkConnection player;
        public string message;
        public SystemMessageSource type;
    }
    public class ClientChat : ListGridUI<LocalChatMessage>, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler {
        [SerializeField]
        CustomInputField chatBox;
        public static List<Func<string, bool>> cancelSendMessageFuncs = new();
        public static ClientChat Instance;
        private void Awake() {
            Instance = this;
        }
        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnCreateMessage;
            InstanceFinder.ClientManager.RegisterBroadcast<ChatMessageBroadcast>(OnReceiveChatMessage);
            InstanceFinder.ClientManager.RegisterBroadcast<SystemMessageBroadcast>(OnReceiveSystemMessage);
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            chatBox.onSubmit.AddListener(OnMessageSend_ButtonPressed);
            ClientConnector.OnDisconnected += OnDisconnected;
            ClientConnector.OnConnected += OnConnected;
            TopbarControls.chatActivateEvent += OnChatToggle;
            ClearPrefabs();
            OnConnected();
        }
        private void OnReceiveChatMessage(ChatMessageBroadcast message, Channel channel) {
            AddPrefab(new() { message = message.message, player = message.player });
        }
        private void OnReceiveSystemMessage(SystemMessageBroadcast message, Channel channel) {
            CreateSystemMessage(message);
        }
        private void OnCreateMessage(GameObject prefab, LocalChatMessage message) {
            TextMeshProUGUI usernameText = prefab.GetComponent<TextMeshProUGUI>();
            string displayText = message.message;
            if (message.player == null) { // System / Custom Message
                switch (message.type) {
                    case SystemMessageSource.LocalPlayerJoinMessage:
                        displayText = "{System}: " + message.message;
                        break;
                    case SystemMessageSource.PlayerAdd:
                        usernameText.color = Color.teal;
                        break;
                    case SystemMessageSource.PlayerRemove:
                        usernameText.color = Color.teal;
                        break;
                    default:
                        break;
                }
            } else { // Player message
                string username = PlayerData.Players[message.player].username.Value;
                displayText = $"{ClientChatHelper.ColorNameRichText(username)}: {ClientChatHelper.EscapeRichText(message.message)}";
            }
            usernameText.text = displayText;
        }
        private void OnChatToggle() {
            ClientTopbar.Instance.EnsureCanvasVisibility(GetComponent<CanvasGroupController>(), true, true);
            chatBox.Select();
        }
        public void OnMessageSend_ButtonPressed(string text) {
            if (text.Length > 0) {
                //if (commandController != null && commandController.TrySubmit(text, out bool clearInput)) {
                //    if (clearInput)
                //        chatBox.text = string.Empty;
                //    return;
                //}
                foreach (var func in cancelSendMessageFuncs) {
                    if (func(text)) {
                        chatBox.text = string.Empty;
                        return;
                    }
                }
                InstanceFinder.ClientManager.Broadcast<MessageRequest>(new() { message = text });
            }
            chatBox.text = string.Empty;
        }
        public void OnScroll(PointerEventData eventData) {
            scrollRect.OnScroll(eventData);
        }
        public void OnBeginDrag(PointerEventData eventData) {
            scrollRect.OnBeginDrag(eventData);
        }
        public void OnDrag(PointerEventData eventData) {
            scrollRect.OnDrag(eventData);
        }
        public void OnEndDrag(PointerEventData eventData) {
            scrollRect.OnEndDrag(eventData);
        }
        private void OnDisconnected() {
            ClearPrefabs();
        }
        private void OnConnected() {
            CreateSystemMessage(new("Chat messages will appear here.", SystemMessageSource.LocalPlayerJoinMessage));
        }

        private void OnPlayerAdded(NetworkConnection conn, PlayerData stats) {
            //if (!synced)
            //    return;
            CreateSystemMessage(new($"{stats.username.Value} has joined the game!", SystemMessageSource.PlayerAdd));
        }
        private void OnPlayerRemoved(NetworkConnection conn, PlayerData stats) {
            if (conn.IsLocalClient)
                return;
            CreateSystemMessage(new($"{stats.username.Value} has left the game!", SystemMessageSource.PlayerRemove));
        }
        public void CreateSystemMessage(SystemMessageBroadcast message) {
            AddPrefab(new() { message = message.message, type = message.type });
        }
    }
}
