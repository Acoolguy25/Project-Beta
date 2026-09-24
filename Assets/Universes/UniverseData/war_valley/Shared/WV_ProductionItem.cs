namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// One thing a production building can be asked to build: either a vehicle
    /// (<see cref="WV_UnitKind"/>) or a foot soldier (<see cref="WV_TroopKind"/>).
    /// <para>
    /// A barracks queue holds both, so the queue needs a single value that can name either. The two
    /// enums overlap numerically - <c>Infantry</c> and <c>Gunner</c> are both 1 - so the kind alone
    /// is ambiguous and the item is encoded into one byte with a flag bit instead.
    /// </para>
    /// <para>
    /// The encoding is what travels on the wire and what the queue's <c>SyncList&lt;byte&gt;</c>
    /// stores, so <see cref="TroopFlag"/> is part of the protocol: it widens the value domain of an
    /// existing byte field rather than adding one, which keeps every payload's field order and types
    /// unchanged.
    /// </para>
    /// </summary>
    public readonly struct WV_ProductionItem {
        /// <summary>
        /// Set on an encoded byte whose low bits are a <see cref="WV_TroopKind"/> rather than a
        /// <see cref="WV_UnitKind"/>. Unit kinds never reach 128, so the two spaces cannot collide.
        /// </summary>
        public const byte TroopFlag = 128;

        public bool IsTroop { get; }
        public WV_UnitKind UnitKind { get; }
        public WV_TroopKind TroopKind { get; }

        WV_ProductionItem(bool isTroop, WV_UnitKind unitKind, WV_TroopKind troopKind) {
            IsTroop = isTroop;
            UnitKind = unitKind;
            TroopKind = troopKind;
        }

        public static WV_ProductionItem Unit(WV_UnitKind kind) =>
            new(false, kind, default);

        public static WV_ProductionItem Troop(WV_TroopKind kind) =>
            new(true, WV_UnitKind.None, kind);

        /// <summary>True for an item that names nothing buildable, which every caller rejects.</summary>
        public bool IsNone => !IsTroop && UnitKind == WV_UnitKind.None;

        /// <summary>The single byte that represents this item in a queue entry or a request.</summary>
        public byte Encoded => IsTroop ? (byte)(TroopFlag | (byte)TroopKind) : (byte)UnitKind;

        /// <summary>
        /// Reads an item back out of a queue entry or a client request. An unrecognised troop kind
        /// decodes to <see cref="IsNone"/> rather than to an arbitrary one, so a malformed request
        /// is rejected instead of silently building something.
        /// </summary>
        public static WV_ProductionItem Decode(byte encoded) {
            if ((encoded & TroopFlag) == 0)
                return Unit((WV_UnitKind)encoded);

            var kind = (WV_TroopKind)(encoded & ~TroopFlag);
            return kind is WV_TroopKind.Knife or WV_TroopKind.Gunner
                ? Troop(kind)
                : Unit(WV_UnitKind.None);
        }

        public string DisplayName => IsTroop
            ? WV_Rules.GetTroopDisplayName(TroopKind)
            : WV_Rules.GetUnitDisplayName(UnitKind);

        public bool Equals(WV_ProductionItem other) => Encoded == other.Encoded;

        public override bool Equals(object obj) => obj is WV_ProductionItem other && Equals(other);

        public override int GetHashCode() => Encoded;
    }
}
