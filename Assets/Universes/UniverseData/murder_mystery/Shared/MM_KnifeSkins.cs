using UnityEngine;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// Knife skins unlocked by collecting coins over many rounds. A murderer's knife (in the hand and in
    /// flight) wears the best skin they have unlocked, so everyone sees it: the long-term goal coins feed.
    /// Pure data shared by the server (who decides) and the client (who paints).
    /// </summary>
    public static class MM_KnifeSkins {
        public readonly struct Skin {
            public readonly string name;
            /// <summary>Lifetime coins needed to unlock it.</summary>
            public readonly int coins;
            /// <summary>Blade tint, and how strongly it glows (0 = not at all).</summary>
            public readonly Color tint;
            public readonly float glow;

            public Skin(string name, int coins, Color tint, float glow) {
                this.name = name;
                this.coins = coins;
                this.tint = tint;
                this.glow = glow;
            }
        }

        /// <summary>In unlock order; index 0 is the knife everyone starts with.</summary>
        public static readonly Skin[] All = {
            new("Rusty", 0, Color.white, 0f),
            new("Crimson", 60, new Color(0.85f, 0.12f, 0.12f), 0f),
            new("Jade", 180, new Color(0.2f, 0.8f, 0.45f), 0.25f),
            new("Gilded", 400, new Color(1f, 0.78f, 0.2f), 0.35f),
            new("Frost", 750, new Color(0.55f, 0.85f, 1f), 0.6f),
            new("Nightmare", 1200, new Color(0.62f, 0.25f, 1f), 1f),
            new("Inferno", 2000, new Color(1f, 0.42f, 0.08f), 1.4f),
        };

        /// <summary>The best skin <paramref name="lifetimeCoins"/> unlocks.</summary>
        public static byte Unlocked(int lifetimeCoins) {
            byte best = 0;
            for (int i = 1; i < All.Length; i++)
                if (lifetimeCoins >= All[i].coins)
                    best = (byte)i;
            return best;
        }

        /// <summary>The next skin to unlock, or -1 once every skin is unlocked.</summary>
        public static int Next(int lifetimeCoins) {
            int unlocked = Unlocked(lifetimeCoins);
            return unlocked + 1 < All.Length ? unlocked + 1 : -1;
        }

        public static Skin Get(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];
    }
}
