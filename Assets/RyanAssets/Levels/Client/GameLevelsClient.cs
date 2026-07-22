using RyanAssets.DataService;
using System.Collections;
using TMPro;
using UnityEngine;

namespace RyanAssets.Levels.Client {
    public class GameLevelsClient : LevelsClient {
        private void Start() {
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved += OnMyPlayerRemoved;
        }
        private void OnDestroy() {
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
        }
        void OnMyPlayerAdded(PlayerData stats) {
            stats.xp.OnChange += OnXPChange;
            UpdateLevel();
        }
        void OnMyPlayerRemoved(PlayerData stats) {
            stats.xp.OnChange -= OnXPChange;
        }
        void OnXPChange(ulong oldValue, ulong newValue, bool asServer) {
            UpdateLevel();
        }
    }
}