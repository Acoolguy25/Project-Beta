using FishNet.Broadcast;

namespace RyanAssets.Shared.Requests {
    public struct CommandBroadcast : IBroadcast {
        public string command;
        public string[] args;
    }
}
