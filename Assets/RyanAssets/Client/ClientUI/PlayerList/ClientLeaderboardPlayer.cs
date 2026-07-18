using FishNet.Object.Synchronizing;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.PlayerList {
    public class ClientLeaderboardPlayer : ClientLeaderboardBase {
        PlayerData player;
        bool rebuildRequired;
        public void Init(PlayerData player) {
            this.player = player;
            OnCreatePrefab += (prefab, idx) => {
                SetLeaderboardItem(prefab, player.leaderboard[idx]);
                if (!IsBuilding)
                    rebuildRequired = false;
            };
            if (enabled)
                OnEnable();
        }
        void OnEnable() {
            if (player == null)
                return;
            player.leaderboard.OnChange += Leaderboard_OnChange;
            player.username.OnChange += Username_OnChange;
            player.team.OnChange += Team_OnChange;
            UpdatePlayerDisplay();
            Rebuild();
        }
        void OnDisable() {
            player.leaderboard.OnChange -= Leaderboard_OnChange;
            player.username.OnChange -= Username_OnChange;
            player.team.OnChange -= Team_OnChange;
        }
        private void Rebuild() {
            rebuildRequired = true;
            BuildLeaderboard(player.leaderboard.Count);
            //for (int i = 0; i < player.leaderboard.Count; i++) {
            //    SetLeaderboardItem(i, player.leaderboard[i]);
            //}
            
        }
        private void Leaderboard_OnChange(SyncListOperation op, int index, int oldItem, int newItem, bool asServer) {
            if (op == SyncListOperation.RemoveAt || op == SyncListOperation.Clear || op == SyncListOperation.Insert) {
                rebuildRequired = true;
            }
            else if (op == SyncListOperation.Set) {
                if (rebuildRequired)
                    ClearPrefabs();
                else
                    SetLeaderboardItem(contentTarget.transform.GetChild(index).gameObject, newItem);
            } else if ((rebuildRequired || IsBuilding) && op == SyncListOperation.Complete) {
                Rebuild();
            }
        }
        private void Username_OnChange(string _, string __, bool ___) {
            UpdatePlayerDisplay();
        }
        private void Team_OnChange(TeamConfig _, TeamConfig __, bool ___) {
            UpdatePlayerDisplay();
        }
        public void UpdatePlayerDisplay() {
            TeamColor team = player.IsOwner ? player.team.Value.team : player.team.Value.displayTeam;
            string playerName = player.username.Value;

            if (team == TeamColor.None)
                playerNameText.text = playerName;
            else if (team == TeamColor.Ghost || team == TeamColor.Lobby)
                playerNameText.text = $"<color=grey>{playerName}</color>";
            else if (team == TeamColor.Blue)
                playerNameText.text = $"<color=blue>{playerName}</color>";
            else if (team == TeamColor.Red)
                playerNameText.text = $"<color=red>{playerName}</color>";
            else if (team == TeamColor.Green)
                playerNameText.text = $"<color=green>{playerName}</color>";
            else
                Debug.LogError($"Unknown team color: {team} for player: {playerName}");
        }
    }
}