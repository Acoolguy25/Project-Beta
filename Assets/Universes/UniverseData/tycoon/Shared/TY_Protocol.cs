using FishNet.Broadcast;

namespace Universes.UniverseData.tycoon {
    public enum TY_EventKind : byte { Claimed, Bought, TooPoor, Collected, Rebirth, Released, Info }

    public struct TY_PlotState {
        public byte plot;
        /// <summary>FishNet client id of the owner, or -1 when the plot is free.</summary>
        public int ownerId;
        public string ownerName;
        public uint purchased;
        public long bank;
        public int rebirths;
        public float income;
    }

    /// <summary>Every plot, sent to everyone on change and about once a second for collector totals.</summary>
    public struct TY_PlotsBroadcast : IBroadcast {
        public TY_PlotState[] plots;
    }

    /// <summary>A player's own wallet.</summary>
    public struct TY_WalletBroadcast : IBroadcast {
        public long cash;
        public int plot;
        public int rebirths;
        public float income;
    }

    /// <summary>Something happened to this player (purchase, collection, refusal).</summary>
    public struct TY_EventBroadcast : IBroadcast {
        public TY_EventKind kind;
        public int plot;
        public int item;
        public long amount;
        public string text;
    }

    /// <summary>A client that has just loaded asks for the plots and its wallet.</summary>
    public struct TY_StateRequest : IBroadcast {
        public int known;
    }
}
