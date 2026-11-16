using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    // Append only: values travel over the wire.
    public enum TY_EventKind : byte { Claimed, Bought, TooPoor, Collected, Rebirth, Released, Info, GoldenBrick, Perk, CollectorFull, RushHour, RushHourOver }

    public struct TY_PlotState {
        public byte plot;
        /// <summary>FishNet client id of the owner, or -1 when the plot is free.</summary>
        public int ownerId;
        public string ownerName;
        public ulong purchased;
        public long bank;
        public long capacity;
        public int rebirths;
        public float income;
        /// <summary>Whether a gold coin is waiting on the plot, and where (plot space).</summary>
        public bool golden;
        public Vector3 goldenPosition;
    }

    /// <summary>Every plot, sent to everyone on change and about once a second for collector totals.</summary>
    public struct TY_PlotsBroadcast : IBroadcast {
        public TY_PlotState[] plots;
    }

    /// <summary>A player's own wallet and progression.</summary>
    public struct TY_WalletBroadcast : IBroadcast {
        public long cash;
        public int plot;
        public int rebirths;
        public float income;
        /// <summary>Everything on top of dropper income (upgraders, buildings, rebirths, perks).</summary>
        public float multiplier;
        public int tokens;
        public TY_PerkLevels perks;
        /// <summary>Total cash earned this session, for the stats line.</summary>
        public long lifetime;
        /// <summary>Seconds left of the server's rush hour, 0 when none runs (appended field).</summary>
        public float rushSeconds;
        /// <summary>The rush hour's income multiplier (already part of <see cref="multiplier"/> and <see cref="income"/>).</summary>
        public float rushMultiplier;
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

    /// <summary>A client asks to level up a rebirth perk with its tokens.</summary>
    public struct TY_PerkRequest : IBroadcast {
        public TY_PerkId perk;
    }
}
