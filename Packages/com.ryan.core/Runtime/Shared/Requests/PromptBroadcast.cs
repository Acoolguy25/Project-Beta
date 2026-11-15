using FishNet.Broadcast;

namespace RyanAssets.Shared.Requests {
    public struct PromptBroadcast : IBroadcast {
        public string title, description;
    }
}
