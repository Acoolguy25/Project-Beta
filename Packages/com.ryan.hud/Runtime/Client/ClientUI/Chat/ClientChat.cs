using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Client.ClientCore;
using RyanAssets.DataService;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using RyanAssets.UI;
using RyanAssets.UI.ListGrid;
using RyanAssets.UI.Textbox;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RyanAssets.Client.ClientUI.Chat {
    public struct LocalChatMessage : IBroadcast {
        public NetworkConnection player;
        public string message;
        public SystemMessageSource type;
    }

    /// <summary>
    /// The shared chat window: message history, the input box and its slash shortcut.
    /// <para>
    /// To stay out of the way, the window's frame (background and input box) fades down while the
    /// player isn't using it, leaving only the messages over the game. Hovering, typing or a new
    /// message brings the frame back.
    /// </para>
    /// </summary>
    public class ClientChat : ListGridUI<LocalChatMessage>, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler {
        /// <summary>Checks run on a submitted line before it is sent; return true to consume it (commands).</summary>
        public static readonly List<Func<string, bool>> cancelSendMessageFuncs = new();
        public static ClientChat Instance { get; private set; }

        [Header("Input")]
        [SerializeField] CustomInputField chatBox;
        [SerializeField] string welcomeMessage = "Chat messages will appear here.";

        [Header("Idle fade")]
        [Tooltip("Background and input box; faded while the chat is idle.")]
        [SerializeField] CanvasGroup frame;
        [Tooltip("Message list; faded (less) while the chat is idle.")]
        [SerializeField] CanvasGroup messages;
        [SerializeField, Range(0f, 1f)] float idleFrameAlpha = 0f;
        [SerializeField, Range(0f, 1f)] float idleMessagesAlpha = 1f;
        [Tooltip("Seconds the frame stays up after the last hover, keystroke or message.")]
        [SerializeField, Min(0f)] float idleDelay = 4f;
        [SerializeField, Min(0.01f)] float fadeSpeed = 6f;

        bool pointerInside;
        float lastActivityTime = float.NegativeInfinity;

        bool IsActive => pointerInside || (chatBox != null && chatBox.isFocused) || Time.unscaledTime - lastActivityTime < idleDelay;

        void Awake() => Instance = this;

        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnCreateMessage;
            InstanceFinder.ClientManager.RegisterBroadcast<ChatMessageBroadcast>(OnReceiveChatMessage);
            InstanceFinder.ClientManager.RegisterBroadcast<SystemMessageBroadcast>(OnReceiveSystemMessage);
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            ClientConnector.OnDisconnected += OnDisconnected;
            ClientConnector.OnConnected += OnConnected;
            TopbarControls.chatActivateEvent += OnChatShortcut;
            HudPanel.VisibilityChanged += OnPanelVisibilityChanged;
            chatBox.onSubmit.AddListener(OnMessageSend_ButtonPressed);
            chatBox.onValueChanged.AddListener(OnTyping);

            ClearPrefabs();
            OnConnected();
            ApplyFade(instant: true);
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            if (Instance == this)
                Instance = null;
            if (InstanceFinder.ClientManager != null) {
                InstanceFinder.ClientManager.UnregisterBroadcast<ChatMessageBroadcast>(OnReceiveChatMessage);
                InstanceFinder.ClientManager.UnregisterBroadcast<SystemMessageBroadcast>(OnReceiveSystemMessage);
            }
            PlayerData.OnPlayerAdded -= OnPlayerAdded;
            PlayerData.OnPlayerRemoved -= OnPlayerRemoved;
            ClientConnector.OnDisconnected -= OnDisconnected;
            ClientConnector.OnConnected -= OnConnected;
            TopbarControls.chatActivateEvent -= OnChatShortcut;
            HudPanel.VisibilityChanged -= OnPanelVisibilityChanged;
        }

        // Messages measured while the panel was hidden have no size; measure again when it opens.
        void OnPanelVisibilityChanged(HudPanelId panel, bool visible) {
            if (panel == HudPanelId.Chat && visible) {
                MarkActivity();
                RefreshFilter();
            }
        }

        void Update() => ApplyFade(instant: false);

        void ApplyFade(bool instant) {
            bool active = IsActive;
            float step = instant ? 1f : fadeSpeed * Time.unscaledDeltaTime;
            if (frame != null)
                frame.alpha = Mathf.MoveTowards(frame.alpha, active ? 1f : idleFrameAlpha, step);
            if (messages != null)
                messages.alpha = Mathf.MoveTowards(messages.alpha, active ? 1f : idleMessagesAlpha, step);
        }

        void MarkActivity() => lastActivityTime = Time.unscaledTime;

        // --- Messages -------------------------------------------------------------

        void OnReceiveChatMessage(ChatMessageBroadcast message, Channel channel) =>
            AddPrefab(new LocalChatMessage { message = message.message, player = message.player });

        void OnReceiveSystemMessage(SystemMessageBroadcast message, Channel channel) => CreateSystemMessage(message);

        public void CreateSystemMessage(SystemMessageBroadcast message) =>
            AddPrefab(new LocalChatMessage { message = message.message, type = message.type });

        void OnCreateMessage(GameObject row, LocalChatMessage message) {
            var text = row.GetComponentInChildren<TMP_Text>(true);
            text.richText = true;
            if (message.player == null) {
                text.text = ClientChatHelper.FormatSystemMessage(message.message, message.type);
            } else {
                string username = PlayerData.TryGetPlayerData(message.player, out PlayerData sender)
                    ? sender.username.Value
                    : "Player";
                text.text = ClientChatHelper.FormatPlayerMessage(username, message.message);
            }
            MarkActivity();
        }

        // --- Input ------------------------------------------------------------------

        void OnChatShortcut() {
            HudPanel.Show(HudPanelId.Chat, instant: true);
            MarkActivity();
            chatBox.Select();
            chatBox.ActivateInputField();
        }

        void OnTyping(string _) => MarkActivity();

        public void OnMessageSend_ButtonPressed(string text) {
            MarkActivity();
            text = text?.Trim();
            if (!string.IsNullOrEmpty(text)) {
                bool consumed = false;
                foreach (Func<string, bool> handler in cancelSendMessageFuncs) {
                    if (handler(text)) {
                        consumed = true;
                        break;
                    }
                }
                if (!consumed)
                    InstanceFinder.ClientManager.Broadcast(new ChatMessageRequest { message = text });
            }
            chatBox.text = string.Empty;
        }

        // --- Pointer: scroll the history from anywhere on the panel, and wake the frame ---

        public void OnPointerEnter(PointerEventData eventData) {
            pointerInside = true;
            MarkActivity();
        }

        public void OnPointerExit(PointerEventData eventData) {
            pointerInside = false;
            MarkActivity();
        }

        public void OnScroll(PointerEventData eventData) => scrollRect.OnScroll(eventData);
        public void OnBeginDrag(PointerEventData eventData) => scrollRect.OnBeginDrag(eventData);
        public void OnDrag(PointerEventData eventData) => scrollRect.OnDrag(eventData);
        public void OnEndDrag(PointerEventData eventData) => scrollRect.OnEndDrag(eventData);

        // --- Session ------------------------------------------------------------------

        void OnDisconnected() => ClearPrefabs();

        void OnConnected() {
            if (!string.IsNullOrEmpty(welcomeMessage))
                CreateSystemMessage(new SystemMessageBroadcast(welcomeMessage, SystemMessageSource.LocalPlayerJoinMessage));
        }

        void OnPlayerAdded(PlayerData player) {
            if (player.Owner.IsLocalClient)
                return;
            // Players already in the server when we joined are not announced.
            if (PlayerData.localData == null || PlayerData.localData.JoinDateTime >= player.JoinDateTime)
                return;
            CreateSystemMessage(new SystemMessageBroadcast($"{player.username.Value} joined the game", SystemMessageSource.PlayerAdd));
        }

        void OnPlayerRemoved(PlayerData player) {
            if (player.Owner.IsLocalClient)
                return;
            CreateSystemMessage(new SystemMessageBroadcast($"{player.username.Value} left the game", SystemMessageSource.PlayerRemove));
        }
    }
}
