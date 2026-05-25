using FishNet.Broadcast;

namespace RyanAssets.Shared.Broadcasts {
    public struct PromptBroadcast : IBroadcast
    {
        public string title, description;
    }
}