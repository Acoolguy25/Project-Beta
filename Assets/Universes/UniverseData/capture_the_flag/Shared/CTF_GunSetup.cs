using RyanAssets.Tools.Shared;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>
    /// Turns the shared pistol tool into a Flag Wars gun (<see cref="CTF_GunStats"/>). Two halves,
    /// because the tool keeps them in two places: damage, clip and reload are replicated values the
    /// server writes; the firing pattern (rate, rounds per pull, spread, range) lives on whoever fires
    /// the gun: the owner's client for a player, the server for a bot.
    /// </summary>
    public static class CTF_GunSetup {
        /// <summary>Server only: the values the tool replicates to every client (damage, clip, reload).</summary>
        public static void ApplyReplicated(ToolBaseShared tool, in CTF_GunStats stats) {
            if (tool == null) return;
            CTF_GunStats s = stats.Validated();
            tool.hitDamageSync.Value = s.damage;
            tool.maxClipAmmoSync.Value = s.ClipRounds;
            tool.reloadDurationSync.Value = s.reloadSeconds;
        }

        /// <summary>
        /// Where the gun is fired: rate, rounds per pull, spread and range. With <paramref name="refill"/>
        /// the clip is loaded full of the new size (a fresh life or a loadout change).
        /// </summary>
        public static void ApplyFiringPattern(ToolGunShared gun, in CTF_GunStats stats, bool refill) {
            if (gun == null) return;
            CTF_GunStats s = stats.Validated();
            gun.FireRate = s.fireInterval;
            gun.BurstCount = s.roundsPerShot;
            gun.BurstDelay = s.roundInterval;
            gun.WorstAccuracy = s.worstAccuracy;
            gun.BestAccuracy = s.bestAccuracy;
            gun.MaxRange = s.range;
            if (!refill) return;
            gun.currentAmmo = s.ClipRounds;
            gun.currentAmmoEvent?.Invoke(gun.currentAmmo);
        }
    }
}
