using RyanAssets.DataService;
using RyanAssets.Shared.Global;

namespace RyanAssets.Rounds {
    /// <summary>Writes named player-list columns that a ServerRunner enabled with SetLeaderboardEnabled.</summary>
    public static class RoundLeaderboard {
        public static void Set(PlayerData player, string column, int value) {
            if (player == null) return;
            int index = SharedGlobalEvents.GetLeaderboardIndex(column);
            if (index >= 0 && index < player.leaderboard.Count && player.leaderboard[index] != value)
                player.leaderboard[index] = value;
        }

        public static void Add(PlayerData player, string column, int amount) {
            if (player == null) return;
            int index = SharedGlobalEvents.GetLeaderboardIndex(column);
            if (index >= 0 && index < player.leaderboard.Count) player.leaderboard[index] += amount;
        }

        public static int Get(PlayerData player, string column) {
            int index = player == null ? -1 : SharedGlobalEvents.GetLeaderboardIndex(column);
            return index >= 0 && index < player.leaderboard.Count ? player.leaderboard[index] : 0;
        }
    }
}
