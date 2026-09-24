using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Commands a client-selected group. Field names, order, and types are part of the wire
    /// contract: append new fields rather than reordering these.
    /// </summary>
    public struct WV_UnitOrderRequest : IBroadcast {
        /// <summary>NetworkObject ids of the units to command. The server drops any the sender does not own.</summary>
        public int[] unitObjectIds;
        public byte orderType;
        /// <summary>Ground destination for Move and AttackMove. Ignored by Stop and HoldPosition.</summary>
        public Vector3 position;
        /// <summary>NetworkObject id of an attack target, or 0 when the order has no target.</summary>
        public int targetObjectId;
    }

    /// <summary>Queues or cancels an item at a production building the sender owns.</summary>
    public struct WV_ProductionRequest : IBroadcast {
        public int buildingObjectId;
        /// <summary>
        /// A <see cref="WV_ProductionItem.Encoded"/> byte: a <see cref="WV_UnitKind"/> on its own, or
        /// a <see cref="WV_TroopKind"/> carrying <see cref="WV_ProductionItem.TroopFlag"/>. The field
        /// keeps its name and type from when it could only ever name a vehicle.
        /// </summary>
        public byte unitKind;
        /// <summary>True cancels the most recent matching queue entry and refunds it.</summary>
        public bool cancel;
    }

    /// <summary>
    /// The server's answer to a <see cref="WV_ProductionRequest"/>.
    /// <para>
    /// Queueing can be refused for several ordinary reasons - no funds, a full queue, a squad
    /// already at its cap - and a request that vanishes silently is indistinguishable from a broken
    /// button. The reason comes back so the HUD can say which one it was.
    /// </para>
    /// </summary>
    public struct WV_ProductionResult : IBroadcast {
        public bool queued;
        /// <summary>A <see cref="WV_TroopRefusal"/> value. Meaningless when <see cref="queued"/> is true.</summary>
        public byte refusal;
        /// <summary>The <see cref="WV_ProductionItem.Encoded"/> item asked for, so the message can name it.</summary>
        public byte item;
    }

    /// <summary>Moves the gather point newly produced units walk to after they leave their building.</summary>
    public struct WV_RallyPointRequest : IBroadcast {
        public int buildingObjectId;
        public Vector3 position;
    }
}

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Cancels one specific queue entry at a production building. Only the building's owner may
    /// cancel, and the refund goes to whoever paid for the entry.
    /// <para>
    /// The item is sent alongside the index so the server can refuse a cancel that no longer lines
    /// up: the queue moves on under a click, and an index alone could cancel the wrong unit.
    /// </para>
    /// </summary>
    public struct WV_QueueCancelRequest : IBroadcast {
        public int buildingObjectId;
        public int queueIndex;
        /// <summary>The <see cref="WV_ProductionItem.Encoded"/> byte the sender saw at that index.</summary>
        public byte item;
    }

    /// <summary>Demolishes a structure the sender owns, refunding part of its price.</summary>
    public struct WV_DemolishRequest : IBroadcast {
        public int buildingObjectId;
    }

    /// <summary>Starts or cancels research, sent from a research station the sender may use.</summary>
    public struct WV_ResearchRequest : IBroadcast {
        public int stationObjectId;
        /// <summary>A <see cref="WV_Tech"/> value.</summary>
        public byte tech;
        /// <summary>True cancels the sender's own project on this technology and refunds it.</summary>
        public bool cancel;
    }

    /// <summary>The server's answer to a <see cref="WV_ResearchRequest"/>.</summary>
    public struct WV_ResearchResult : IBroadcast {
        /// <summary>A <see cref="WV_Tech"/> value.</summary>
        public byte tech;
        /// <summary>A <see cref="WV_ResearchRefusal"/> value; None means the request went through.</summary>
        public byte refusal;
        /// <summary>Echoes the request, so the HUD can say "started" or "cancelled".</summary>
        public bool cancel;
    }

    /// <summary>
    /// A one-line notice for a single commander's HUD: a refused placement, a demolition refund, the
    /// funds lost on death. Things the server decided that the player would otherwise never see.
    /// </summary>
    public struct WV_Notice : IBroadcast {
        public string message;
    }

    /// <summary>
    /// Sells units and troops the sender commands, refunding part of what each cost to produce
    /// (see <see cref="WV_Rules.GetSellRefund(long, float)"/>). The server drops any id the sender
    /// does not command.
    /// </summary>
    public struct WV_SellRequest : IBroadcast {
        public int[] objectIds;
    }

    /// <summary>Sets a gate the sender owns to open by itself, stay open, or stay locked.</summary>
    public struct WV_GateModeRequest : IBroadcast {
        public int gateObjectId;
        /// <summary>A <see cref="WV_GateMode"/> value.</summary>
        public byte mode;
    }

    /// <summary>Gives some of the sender's funds to an allied commander.</summary>
    public struct WV_DonateRequest : IBroadcast {
        public int recipientClientId;
        public long amount;
    }
}
