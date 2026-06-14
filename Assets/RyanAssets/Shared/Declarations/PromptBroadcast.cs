using FishNet.Broadcast;

namespace RyanAssets.Shared.Declarations {
    public struct PromptBroadcast : IBroadcast {
        public string title, description;
    }
}