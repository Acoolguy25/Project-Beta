namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// The dropped gun: a sheriff's gun falls where they died, and the first living, empty-handed innocent
    /// to reach it takes it and becomes the hero (a sheriff in all but name). Players get a head start; then
    /// an NPC townsperson goes for it, so the hero moment happens in rounds full of NPCs too.
    /// </summary>
    public static class MM_DropRules {
        /// <summary>
        /// Whether a character in this state may pick up the dropped gun: a living innocent with empty hands.
        /// Players take it by walking over it; an NPC only once it was sent for it (<see cref="NpcMayGoForGun"/>).
        /// </summary>
        public static bool CanPickUp(MM_Role role, bool alive, int toolsHeld) =>
            alive && role == MM_Role.Innocent && toolsHeld == 0;

        /// <summary>
        /// Whether an NPC townsperson may set off for a gun that has lain on the floor for
        /// <paramref name="secondsOnFloor"/>: players get <paramref name="playerHeadStart"/> seconds to reach it
        /// first. A negative head start means NPCs never take it.
        /// </summary>
        public static bool NpcMayGoForGun(float secondsOnFloor, float playerHeadStart) =>
            playerHeadStart >= 0f && secondsOnFloor >= playerHeadStart;

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
