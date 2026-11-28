namespace Universes.UniverseData.obby {
    /// <summary>
    /// Which mechanic hints a player still needs. A mechanic's how-to hint is shown until the player has cleared a
    /// stage with it once (saved by the server as a bit mask, one bit per <see cref="OB_StageKind"/>), and at most
    /// once per session on the client, so the HUD teaches each new thing once and then stays out of the way.
    /// </summary>
    public static class OB_StageHints {
        /// <summary>The mask bit of <paramref name="kind"/> (kinds past the 64 a mask holds have none).</summary>
        public static ulong Bit(OB_StageKind kind) => (int)kind < 64 ? 1UL << (int)kind : 0UL;

        /// <summary>Whether <paramref name="mask"/> holds <paramref name="kind"/>.</summary>
        public static bool Has(ulong mask, OB_StageKind kind) => (mask & Bit(kind)) != 0;
    }
}
