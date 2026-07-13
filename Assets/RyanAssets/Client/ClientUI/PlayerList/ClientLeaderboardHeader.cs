using FishNet.Object.Synchronizing;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using System.Collections;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RyanAssets.Client.ClientUI.PlayerList {
    public class ClientLeaderboardHeader : ClientLeaderboardBase {
        protected override void Start() {
            base.Start();
            OnCreatePrefab += (prefab, idx) => {
                SetLeaderboardItem(prefab, SharedGlobalEvents.Instance.LeaderboardHeaders[idx]);
            };
        }
        void OnEnable() {
            SharedGlobalEvents.BindInstanceReady(InstanceReady);
            playerNameText.text = "Players";
        }
        void OnDisable() {
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.LeaderboardHeaders.OnChange -= Leaderboard_OnChange;
        }
        void InstanceReady() {
            SharedGlobalEvents.Instance.LeaderboardHeaders.OnChange += Leaderboard_OnChange;
            BuildHeaders();         
        }
        private void BuildHeaders() {
            BuildLeaderboard(SharedGlobalEvents.Instance.LeaderboardHeaders.Count);
        }
        private void Leaderboard_OnChange(SyncListOperation op, int index, string oldItem, string newItem, bool asServer) {
            if (op == SyncListOperation.Complete) {
                BuildHeaders();
            }
        }
    }
}