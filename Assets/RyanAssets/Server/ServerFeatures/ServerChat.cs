using UnityEngine;
using FishNet;
using RyanAssets.Shared.Broadcasts;
using RyanAssets.Shared.Requests;
using FishNet.Connection;
using FishNet.Transporting;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerChat {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init(){
            InstanceFinder.ServerManager.RegisterBroadcast<MessageRequest>(PlayerSendMessage, true);
        }
        static void PlayerSendMessage(NetworkConnection conn, MessageRequest message, Channel channel){
            MessageBroadcast message_broadcast = new(){
                message = message.message,
                player = conn
            };
            InstanceFinder.ServerManager.Broadcast<MessageBroadcast>(message_broadcast);
        }
    }
}