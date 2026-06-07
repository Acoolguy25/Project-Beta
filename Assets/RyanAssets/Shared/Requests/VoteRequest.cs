using FishNet.Broadcast;

namespace RyanAssets.Shared.Requests {
    public struct VoteRequest : IBroadcast {
        public int voteId;
        public int optionId;
    }
}
