using UnityEngine;
using FishNet;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using RyanAssets.Shared.Requests;
using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerChat {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.RegisterBroadcast<MessageRequest>(PlayerSendMessage, true);
        }
        static void PlayerSendMessage(NetworkConnection conn, MessageRequest message, Channel channel) {
            if (!IsChatMessageValid(message.message)) {
                conn.Kick(FishNet.Managing.Server.KickReason.ExploitAttempt);
                return;
            }
            ChatMessageBroadcast message_broadcast = new() {
                message = message.message,
                player = conn
            };
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
    }
}
