using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.tycoon.Server {
    /// <summary>
    /// What Brick Tycoon remembers about a player between sessions: their wallet, rebirth progress and
    /// the build of their plot, restored when they claim a plot again. Written only by the server.
    /// </summary>
    public sealed class TY_SaveData : ProgressData {
        public const string Universe = "tycoon";
        public override int LatestVersion => 1;

        public double cash;
        /// <summary>Cash earned in total, across rebirths.</summary>
        public double lifetime;
        public int rebirths;
        public int tokens;
        public TY_PerkLevels perks;
        /// <summary>Purchased items as a <see cref="TY_Catalog"/> bit mask; 0 when nothing is built yet.</summary>
        public ulong built;

        /// <summary>True for a player who has never earned or built anything (nothing to restore).</summary>
        public bool IsEmpty => cash <= 0 && lifetime <= 0 && rebirths == 0 && tokens == 0 && built == 0 && perks.Equals(default(TY_PerkLevels));

        public override void Validate() {
            cash = Finite(cash);
            lifetime = Math.Max(Finite(lifetime), cash);
            rebirths = Math.Max(0, rebirths);
            tokens = Math.Max(0, tokens);
            foreach (TY_Perk perk in TY_Perks.All)
                perks = perks.WithLevel(perk.id, Math.Clamp(perks.Level(perk.id), 0, perk.maxLevel));
            // Unknown items (a catalog that shrank) are dropped; the rebirth is never "built".
            if (built != 0) built = (built & TY_Catalog.AllButRebirth) | TY_Catalog.StarterMask;
        }

        static double Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, value);
    }
}
