using FishNet.Broadcast;

namespace RyanAssets.Declarations {
    public struct CommandBroadcast : IBroadcast {
        public string command;
        public string[] args;
    }
}
