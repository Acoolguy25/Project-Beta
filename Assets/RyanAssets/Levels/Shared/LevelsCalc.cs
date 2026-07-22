using System;
namespace RyanAssets.Levels.Shared {
    public static class LevelsCalc {
        private static readonly ulong[] EarlyRanks = {
            0,
            165,
            335,
            660,
            990,
            1440,
            2100,
            2850,
            3700,
            4800
        };

        public static int GetRank(ulong xp) {
            if (xp < 5730) {
                for (int rank = 9; rank >= 0; rank--) {
                    if (xp >= EarlyRanks[rank])
                        return rank;
                }
                return 0;
            }

            return Math.Min(60, (int)((xp - 5730UL) / 1080UL) + 10);
        }

        public static float GetRankProgress(ulong xp) {
            int rank = GetRank(xp);

            if (rank >= 60)
                return 1f;

            ulong currentXP = GetRankXP(rank);
            ulong nextXP = GetRankXP(rank + 1);

            return (float)(xp - currentXP) / (nextXP - currentXP);
        }

        public static ulong GetXPToNextLevel(ulong xp) {
            int rank = GetRank(xp);

            if (rank >= 60)
                return 0;

            return GetRankXP(rank + 1) - xp;
        }

        public static ulong GetXPRemaining(ulong xp) {
            int rank = GetRank(xp);

            return xp - GetRankXP(rank);
        }

        private static ulong GetRankXP(int rank) {
            if (rank <= 9)
                return EarlyRanks[rank];

            return 5730UL + (ulong)(rank - 10) * 1080UL;
        }
    }
}