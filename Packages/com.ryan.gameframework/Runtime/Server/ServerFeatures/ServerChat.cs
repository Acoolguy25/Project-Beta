using UnityEngine;
using FishNet;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using System;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerChat {
        public static Action<ChatMessageBroadcast> OnPlayerChatMessage;
        public static Func<NetworkConnection, ChatMessageRequest, bool> ValidatePlayerChatMessageFunc;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.RegisterBroadcast<ChatMessageRequest>(PlayerSendMessage, true);
        }
        static void PlayerSendMessage(NetworkConnection conn, ChatMessageRequest message_request, Channel channel) {
            if (!IsChatMessageValid(message_request.message)) {
                conn.Kick(FishNet.Managing.Server.KickReason.ExploitAttempt);
                return;
            }
            if (ValidatePlayerChatMessageFunc != null && !ValidatePlayerChatMessageFunc.Invoke(conn, message_request)) {
                return;
            }
            ChatMessageBroadcast message_broadcast = new() {
                message = message_request.message,
                player = conn
            };
            OnPlayerChatMessage?.Invoke(message_broadcast);
            InstanceFinder.ServerManager.Broadcast<ChatMessageBroadcast>(message_broadcast);
        }
        public static bool IsChatMessageValid(string s) {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            for (int i = 1; i < s.Length; i++) {
                if (s[i] == ' ' && s[i - 1] == ' ')
                    return false;
            }

            return true;
        }
        public static void SendSystemMessage(SystemMessageBroadcast message) {
            InstanceFinder.ServerManager.Broadcast<SystemMessageBroadcast>(message);
        }
        public static void SendSystemMessage(NetworkConnection conn, SystemMessageBroadcast message) {
            InstanceFinder.ServerManager.Broadcast<SystemMessageBroadcast>(conn, message);
        }
        public static void SendSystemMessageExcept(NetworkConnection conn, SystemMessageBroadcast message) {
            InstanceFinder.ServerManager.BroadcastExcept<SystemMessageBroadcast>(conn, message);
        }
    }
}
