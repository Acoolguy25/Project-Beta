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

        public static int GetRank(ulong xp){
            if (xp < 5730){
                for (int rank = 9; rank >= 0; rank--){
                    if (xp >= EarlyRanks[rank])
                        return rank;
                }
                return 0;
            }
            return Math.Min(60, (int)((xp - 5730UL) / 1080UL) + 10);
        }
    }
}