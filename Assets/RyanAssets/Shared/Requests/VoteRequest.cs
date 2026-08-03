using FishNet.Broadcast;

namespace RyanAssets.Shared.Requests {
    /// <summary>Requests a selection in the vote currently advertised by the server.</summary>
    public struct VoteRequest : IBroadcast {
        /// <summary>Zero-based option index; -1 removes the player's vote.</summary>
        public int optionId;
    }
}
