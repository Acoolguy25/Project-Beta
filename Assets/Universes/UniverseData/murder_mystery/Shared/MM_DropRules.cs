namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// The dropped gun: a sheriff's gun falls where they died, and the first living, empty-handed
    /// innocent player to reach it takes it and becomes the hero (a sheriff in all but name).
    /// </summary>
    public static class MM_DropRules {
        /// <summary>
        /// Whether a character in this state may pick up the dropped gun: a living innocent player with
        /// empty hands. NPCs never take it, whatever the pickup prefab's trigger settings are.
        /// </summary>
        public static bool CanPickUp(MM_Role role, bool alive, int toolsHeld, bool isPlayer) =>
            isPlayer && alive && role == MM_Role.Innocent && toolsHeld == 0;

        /// <summary>Whether a character with this role leaves the gun behind when they die.</summary>
        public static bool DropsGun(MM_Role role) => role == MM_Role.Sheriff;

        /// <summary>
        /// Whether a sheriff's shot that killed <paramref name="victimRole"/> was a mistake (anyone but a
        /// murderer), which the wrong-shot penalty punishes.
        /// </summary>
        public static bool IsWrongShot(MM_Role shooterRole, MM_Role victimRole) =>
            shooterRole == MM_Role.Sheriff && victimRole != MM_Role.Murderer;
    }
}
