namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// Classic rules for weapons dropped by the dead: the sheriff's gun (and, if enabled, a murderer's
    /// dagger) falls where they died, and the first living, empty-handed innocent player to reach it
    /// takes it over together with the role that goes with it.
    /// </summary>
    public static class MM_DropRules {
        /// <summary>Whether a character in this state may pick up a dropped weapon.</summary>
        public static bool CanPickUp(MM_Role role, bool alive, int toolsHeld) =>
            alive && role == MM_Role.Innocent && toolsHeld == 0;

        /// <summary>The role that comes with a dropped weapon: the gun makes a sheriff, the dagger a murderer.</summary>
        public static MM_Role RoleFor(MM_DroppedWeapon weapon) => weapon switch {
            MM_DroppedWeapon.Gun => MM_Role.Sheriff,
            MM_DroppedWeapon.Dagger => MM_Role.Murderer,
            _ => MM_Role.None,
        };

        /// <summary>Whether a character with this role leaves a weapon behind when they die.</summary>
        public static MM_DroppedWeapon WeaponLeftBy(MM_Role role, bool dropDaggers) => role switch {
            MM_Role.Sheriff => MM_DroppedWeapon.Gun,
            MM_Role.Murderer when dropDaggers => MM_DroppedWeapon.Dagger,
            _ => MM_DroppedWeapon.None,
        };
    }

    public enum MM_DroppedWeapon : byte { None, Gun, Dagger }
}
