using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Broadcasts {
    public struct MessageBroadcast : IBroadcast
    {
        public NetworkConnection player;
        public string message;
    }
}