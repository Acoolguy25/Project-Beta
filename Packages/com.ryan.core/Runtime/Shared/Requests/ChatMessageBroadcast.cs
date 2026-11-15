using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Requests {
    public struct ChatMessageBroadcast : IBroadcast {
        public NetworkConnection player;
        public string message;
    }
}
