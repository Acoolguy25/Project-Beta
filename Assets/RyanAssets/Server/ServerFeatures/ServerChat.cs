using UnityEngine;
using FishNet;
using RyanAssets.Shared.Broadcasts;
using RyanAssets.Shared.Requests;
using FishNet.Connection;
using FishNet.Transporting;

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
            MessageBroadcast message_broadcast = new() {
                message = message.message,
                player = conn
            };
            InstanceFinder.ServerManager.Broadcast<MessageBroadcast>(message_broadcast);
        }
        public static bool IsChatMessageValid(string s) {
            if (string.IsNullOrEmpty(s))
                return false;

            for (int i = 1; i < s.Length; i++) {
                if (s[i] == ' ' && s[i - 1] == ' ')
                    return true;
            }

            return false;
        }
    }
}