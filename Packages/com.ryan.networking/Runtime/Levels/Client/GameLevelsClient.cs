using RyanAssets.DataService;

namespace RyanAssets.Levels.Client {
    /// <summary>Level badge in a game, fed by the local player's replicated XP.</summary>
    public class GameLevelsClient : LevelsClient {
        void Start() {
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved += OnMyPlayerRemoved;
        }

        void OnDestroy() {
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved -= OnMyPlayerRemoved;
        }

        void OnMyPlayerAdded(PlayerData stats) {
            stats.xp.OnChange += OnXpChange;
            UpdateLevel(stats.xp.Value, instant: true);
        }

        void OnMyPlayerRemoved(PlayerData stats) => stats.xp.OnChange -= OnXpChange;

        void OnXpChange(ulong oldValue, ulong newValue, bool asServer) => UpdateLevel(newValue);
    }
}
