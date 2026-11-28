using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    // Append only: values travel over the wire.
    public enum TY_EventKind : byte {
        Claimed, Bought, TooPoor, Collected, Rebirth, Released, Info, GoldenBrick, Perk, CollectorFull, RushHour, RushHourOver,
        /// <summary>A tycoon goal was completed. item = goal index, amount = cash paid, extra = tokens paid.</summary>
        Goal,
        /// <summary>Earnings while away were paid on join. amount = cash, extra = seconds away (the text names the paid part).</summary>
        OfflineEarnings,
        /// <summary>The daily chest was opened. amount = cash, extra = seconds until the next one.</summary>
        DailyChest,
        /// <summary>A pet crate was opened. item = pet, amount = its level now, extra = <see cref="TY_Hatch.Flags"/>.</summary>
        PetHatched,
    }

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
        /// <summary>The owner's following pet (<see cref="TY_Pets.Best"/>), or -1; and its level (appended).</summary>
        public sbyte pet;
        public byte petLevel;
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
        /// <summary>The player's current goal (<see cref="TY_Goals.Get"/>), its progress and the cash it pays (appended).</summary>
        public int goalIndex;
        public long goalProgress;
        public long goalReward;
        /// <summary>Whether today's daily chest is still waiting on the player's plot, else seconds until the next one.</summary>
        public bool chestReady;
        public float chestSeconds;
        /// <summary>Streak day of the chest the sign names: today's while it is ready, else the next one's (appended).</summary>
        public int chestStreak;
        /// <summary>Investment levels bought (kept through rebirths; each adds income) (appended).</summary>
        public int invested;
        /// <summary>That chest's prize: minutes of income and perk tokens (appended).</summary>
        public int chestMinutes, chestTokens;
        /// <summary>Level of every pet (index of <see cref="TY_Pets.All"/>, 0 = not found), the crate's price and crates left until the pity guarantee (0 = none) (appended).</summary>
        public byte[] pets;
        public long cratePrice;
        public int crateUntilPity;
    }

    /// <summary>Something happened to this player (purchase, collection, refusal).</summary>
    public struct TY_EventBroadcast : IBroadcast {
        public TY_EventKind kind;
        public int plot;
        public int item;
        public long amount;
        public string text;
        /// <summary>A second number for some kinds (see <see cref="TY_EventKind"/>); appended.</summary>
        public long extra;
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
