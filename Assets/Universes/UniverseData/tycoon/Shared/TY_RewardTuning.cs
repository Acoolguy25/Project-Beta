using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// Game XP and account gold the tycoon pays through the retention kit (which also pays account XP).
    /// Serialized on the server runner; shared so the pacing test measures the numbers the server pays.
    /// </summary>
    [Serializable]
    public sealed class TY_RewardTuning {
        [Tooltip("Game XP of the first rebirth...")]
        [Min(0)] public int rebirthXp = 100;
        [Tooltip("...plus this much for every rebirth already done, so later (longer) runs pay more.")]
        [Min(0)] public int rebirthXpPerRebirth = 5;
        [Tooltip("Account gold per rebirth.")]
        [Min(0)] public int rebirthGold = 75;
        [Tooltip("Game XP per gold coin grabbed.")]
        [Min(0)] public int coinXp = 3;
        [Tooltip("Game XP per minute on a claimed plot: steady pay for playing, when rebirths get far apart.")]
        [Min(0)] public int minuteXp = 15;

        public void Validate() {
            rebirthXp = Math.Max(0, rebirthXp);
            rebirthXpPerRebirth = Math.Max(0, rebirthXpPerRebirth);
            rebirthGold = Math.Max(0, rebirthGold);
            coinXp = Math.Max(0, coinXp);
            minuteXp = Math.Max(0, minuteXp);
        }

        /// <summary>Game XP of rebirth number <paramref name="rebirth"/> (1 = the first).</summary>
        public int RebirthXp(int rebirth) => rebirthXp + rebirthXpPerRebirth * Math.Max(0, rebirth - 1);

        /// <summary>
        /// What rebirth number <c>rebirthsBefore + 1</c> does to a plot built as <paramref name="purchased"/>:
        /// the build it keeps, the tokens and XP it pays and the card's short detail.
        /// </summary>
        public TY_RebirthResult Rebirth(int rebirthsBefore, ulong purchased, bool headOffice, int extraTokens) {
            int rebirths = Math.Max(0, rebirthsBefore) + 1;
            int landmark = TY_Catalog.LandmarkOpenedBy(rebirths);
            return new TY_RebirthResult {
                rebirths = rebirths,
                kept = TY_Catalog.RebirthKeeps(purchased, headOffice),
                tokens = TY_Perks.TokensPerRebirth + Math.Max(0, extraTokens),
                xp = RebirthXp(rebirths),
                gold = rebirthGold,
                landmark = landmark,
                // Short: the card's line is narrow. The new landmark is the news; otherwise the income step.
                detail = landmark >= 0 ? $"Opens {TY_Catalog.Items[landmark].name}" : $"Income x{TY_Catalog.RebirthMultiplier(rebirths):0.#}",
            };
        }
    }

    /// <summary>The outcome of one rebirth (see <see cref="TY_RewardTuning.Rebirth"/>).</summary>
    public struct TY_RebirthResult {
        /// <summary>Rebirths done after it.</summary>
        public int rebirths;
        /// <summary>The build the plot keeps: the starter, every landmark and, with Head Office, the collector lane.</summary>
        public ulong kept;
        public int tokens, xp, gold;
        /// <summary>The landmark this rebirth puts up for sale, or -1.</summary>
        public int landmark;
        public string detail;

        /// <summary>The summary card's headline line ("Rebirth 3").</summary>
        public readonly string Label => $"Rebirth {rebirths}";
    }
}
