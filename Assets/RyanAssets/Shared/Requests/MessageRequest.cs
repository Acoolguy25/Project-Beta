using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Requests {
    public struct MessageRequest : IBroadcast
    {
        public string message;
    }
}