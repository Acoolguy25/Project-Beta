using FishNet.Broadcast;

namespace RyanAssets.Shared.Requests {
    /// <summary>Requests a selection in the vote currently advertised by the server.</summary>
    public struct VoteRequest : IBroadcast {
        /// <summary>Special selection used to skip the remaining vote timer.</summary>
        public const int SkipVoteOptionId = -2;

        /// <summary>Zero-based option index; -1 removes the player's vote; -2 votes to skip.</summary>
        public int optionId;
    }
}
