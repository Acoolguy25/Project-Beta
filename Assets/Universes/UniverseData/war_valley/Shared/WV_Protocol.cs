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

    /// <summary>Queues or cancels a unit at a production building the sender owns.</summary>
    public struct WV_ProductionRequest : IBroadcast {
        public int buildingObjectId;
        /// <summary>A <see cref="WV_UnitKind"/> value.</summary>
        public byte unitKind;
        /// <summary>True cancels the most recent matching queue entry and refunds it.</summary>
        public bool cancel;
    }

    /// <summary>Moves the gather point newly produced units walk to after they leave their building.</summary>
    public struct WV_RallyPointRequest : IBroadcast {
        public int buildingObjectId;
        public Vector3 position;
    }
}
