using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Declarations {
    public enum SystemMessageSource {
        // Client-side
        LocalPlayerJoinMessage,
        PlayerAdd,
        PlayerRemove,
        ClientCommand,
        // Server-side
        CustomMessage,
        // Either-side
        CommandError
    }
    public struct SystemMessageBroadcast : IBroadcast {
        public string message;
        public SystemMessageSource type;
        public SystemMessageBroadcast(string message, SystemMessageSource type)
                => (this.message, this.type) = (message, type);
    }
}