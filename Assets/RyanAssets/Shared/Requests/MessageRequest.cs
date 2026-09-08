using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Requests {
    public struct ChatMessageRequest : IBroadcast {
        public string message;
    }
}