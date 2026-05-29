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
using UnityEngine.EventSystems;
using RyanAssets.Client.ClientUI.Topbar;
using RyanAssets.UI;

namespace RyanAssets.Client.ClientUI.Chat {
    public class ClientChat : ListGridUI<MessageBroadcast>, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler {
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
            ClientTopbar.Instance.EnsureCanvasVisibility(GetComponent<CanvasGroupController>());
            chatBox.Select();
        }
        public void OnMessageSend_ButtonPressed(string text) {
            chatBox.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null);
            if (text.Length > 0)
                InstanceFinder.ClientManager.Broadcast<MessageRequest>(new() { message = text });
            chatBox.text = string.Empty;
        }
        public void OnChatSelected() {
            SharedInputController.Instance.LockControls();
        }
        public void OnChatUnselected() {
            SharedInputController.Instance.UnlockControls();
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
    }
}
