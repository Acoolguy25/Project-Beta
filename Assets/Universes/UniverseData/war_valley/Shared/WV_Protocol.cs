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
