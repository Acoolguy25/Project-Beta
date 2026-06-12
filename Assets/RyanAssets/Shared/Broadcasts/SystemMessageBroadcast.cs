using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Broadcasts {
    public enum SystemMessageSource {
        // Client-side
        LocalPlayerJoinMessage,
        PlayerAdd,
        PlayerRemove,
        // Server-side
        CustomMessage
    }
    public struct SystemMessageBroadcast : IBroadcast
    {
        public string message;
        public SystemMessageSource type;
        public SystemMessageBroadcast(string message, SystemMessageSource type)
                => (this.message, this.type) = (message, type);
    }
}