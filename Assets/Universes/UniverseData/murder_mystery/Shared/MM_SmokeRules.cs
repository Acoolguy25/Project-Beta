namespace Universes.UniverseData.murder_mystery {
    /// <summary>Why a smoke bomb was refused (or <see cref="Ok"/>).</summary>
    public enum MM_SmokeCheck : byte {
        Ok,
        /// <summary>Only during the hunt.</summary>
        NotHunting,
        /// <summary>Only living innocents carry smoke bombs (not the murderer, the sheriff or the hero).</summary>
        WrongRole,
        /// <summary>The bag holds fewer coins than a bomb costs.</summary>
        NotEnoughCoins,
        /// <summary>The last bomb was thrown too recently.</summary>
        Cooling,
    }

    /// <summary>
    /// The innocent's smoke bomb: press the action key to spend coins from this round's bag on a cloud of smoke
    /// and a burst of speed. NPC murderers lose sight of whoever is inside the cloud. Pure rules, shared by the
    /// server (who decides) and the client (who shows the prompt), so both agree and the tests cover them.
    /// </summary>
    public static class MM_SmokeRules {
        /// <summary>Whether a character may throw a smoke bomb right now.</summary>
        public static MM_SmokeCheck Check(MM_Phase phase, MM_Role role, bool hero, bool alive, int bagCoins, int cost, float secondsUntilReady) {
            if (phase != MM_Phase.Hunt)
                return MM_SmokeCheck.NotHunting;
            if (!alive || role != MM_Role.Innocent || hero)
                return MM_SmokeCheck.WrongRole;
            if (secondsUntilReady > 0f)
                return MM_SmokeCheck.Cooling;
            return bagCoins < cost ? MM_SmokeCheck.NotEnoughCoins : MM_SmokeCheck.Ok;
        }

        /// <summary>
        /// Lifetime coins after spending <paramref name="cost"/>: the coins come off the total that unlocks knife
        /// skins, so a bomb slows the way to the next skin. A skin already unlocked is never taken back: the total
        /// does not drop below what that skin needed.
        /// </summary>
        public static int LifetimeAfterSpending(int lifetimeCoins, int cost) {
            if (cost <= 0)
                return lifetimeCoins;
            int floor = MM_KnifeSkins.Get(MM_KnifeSkins.Unlocked(lifetimeCoins)).coins;
            int after = lifetimeCoins - cost;
            return after < floor ? floor : after;
        }
    }
}
