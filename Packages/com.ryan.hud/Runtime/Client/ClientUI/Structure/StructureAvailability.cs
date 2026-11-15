namespace RyanAssets.Client.ClientUI.Build {
    /// <summary>
    /// Whether the local player may place a structure right now, and what to tell them when not.
    /// <para>
    /// The structure menu has no rules of its own about who may build what. A game mode that gates
    /// structures - behind research, a build limit, a territory - answers through
    /// <see cref="StructureMenu.AvailabilityProvider"/>, and the menu shows the answer on the card
    /// instead of offering a placement the server is going to refuse.
    /// </para>
    /// </summary>
    public readonly struct StructureAvailability {
        public static readonly StructureAvailability Available = new(true, null);

        public bool IsAvailable { get; }

        /// <summary>Short explanation shown on a locked card, such as the research it waits on.</summary>
        public string LockedReason { get; }

        StructureAvailability(bool isAvailable, string lockedReason) {
            IsAvailable = isAvailable;
            LockedReason = lockedReason;
        }

        public static StructureAvailability Locked(string reason) => new(false, reason);
    }
}
