using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Requests {
    public struct SystemMessageBroadcast : IBroadcast {
        public string message;
        public RyanAssets.Shared.Declarations.SystemMessageSource type;
        public SystemMessageBroadcast(string message, RyanAssets.Shared.Declarations.SystemMessageSource type)
                => (this.message, this.type) = (message, type);
    }
}
