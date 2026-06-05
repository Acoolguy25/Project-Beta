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
using RyanAssets.Input;
using TMPro;
using UnityEngine.EventSystems;
using RyanAssets.Client.ClientUI.Topbar;
using RyanAssets.UI;
using RyanAssets.Client.ClientCore;

namespace RyanAssets.Client.ClientUI.Chat {
    public class ClientChat : ListGridUI<MessageBroadcast>, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler {
        [SerializeField]
        TMP_InputField chatBox;
        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnCreateMessage;
            InstanceFinder.ClientManager.RegisterBroadcast<MessageBroadcast>(OnReceiveMessage);
            chatBox.onSubmit.AddListener(OnMessageSend_ButtonPressed);
            ClientConnector.OnDisconnected += OnDisconnected;
            ClientConnector.OnConnected += OnConnected;
            TopbarControls.chatActivateEvent += OnChatToggle;
            ClearPrefabs();
            OnConnected();
        }
        private void OnReceiveMessage(MessageBroadcast message, Channel channel) {
            AddPrefab(message);
        }
        private void OnCreateMessage(GameObject prefab, MessageBroadcast message) {
            TextMeshProUGUI usernameText = prefab.GetComponent<TextMeshProUGUI>();
            if (message.player == null){
                usernameText.text = message.message;
            }
            else {
                string username = SharedGlobalEvents.Instance.Players[message.player].data.username;
                usernameText.text = $"{ClientChatHelper.ColorNameRichText(username)}: {ClientChatHelper.EscapeRichText(message.message)}";
            }
        }
        private void OnChatToggle() {
            ClientTopbar.Instance.EnsureCanvasVisibility(GetComponent<CanvasGroupController>(), true, true);
            chatBox.Select();
        }
        public void OnMessageSend_ButtonPressed(string text) {
            chatBox.DeactivateInputField();
            if (text.Length > 0)
                InstanceFinder.ClientManager.Broadcast<MessageRequest>(new() { message = text });
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
        private void OnDisconnected(){
            ClearPrefabs();
        }
        private void OnConnected(){
            AddPrefab(new MessageBroadcast(){
                // player = InstanceFinder.ClientManager.Connection,
                message = "{System} Chat messages will appear here."
            });
        }
    }
}
