using FishNet.Connection;
using RyanAssets.Client.ClientCore;
using RyanAssets.DataService;
using RyanAssets.UI;
using RyanAssets.UI.ListGrid;
using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.PlayerList {
    /// <summary>
    /// The shared player list: one <see cref="PlayerListRow"/> per connected player, the local player
    /// first, with the game's leaderboard stats.
    /// </summary>
    public class ClientPlayerList : ListGridUI<(NetworkConnection conn, PlayerData data)> {
        [Tooltip("Optional label showing how many players are in the server.")]
        [SerializeField] TMP_Text countText;

        protected override string GetItemName((NetworkConnection conn, PlayerData data) player) =>
            player.data != null ? player.data.player_id.Value : "Disconnected player";

        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnRowCreated;
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            ClientConnector.OnDisconnected += OnDisconnected;
            HudPanel.VisibilityChanged += OnPanelVisibilityChanged;
            PlayerData.RunEach(OnPlayerAdded);
            RefreshCount();
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            PlayerData.OnPlayerAdded -= OnPlayerAdded;
            PlayerData.OnPlayerRemoved -= OnPlayerRemoved;
            ClientConnector.OnDisconnected -= OnDisconnected;
            HudPanel.VisibilityChanged -= OnPanelVisibilityChanged;
        }

        void OnRowCreated(GameObject row, (NetworkConnection conn, PlayerData data) player) {
            // The player may have left while their row was being instantiated.
            if (player.data == null || !PlayerData.Players.ContainsValue(player.data)) {
                RemovePrefab(row.transform);
                return;
            }
            row.name = player.data.player_id.Value;
            row.GetComponent<PlayerListRow>().Init(player.data);
        }

        void OnPlayerAdded(PlayerData data) {
            // The local player always sits at the top.
            if (data.IsOwner)
                AddPrefab((data.Owner, data), -10);
            else
                AddPrefab((data.Owner, data));
            RefreshCount();
        }

        void OnPlayerRemoved(PlayerData data) {
            RefreshCount();
            string playerId = data.player_id.Value;
            if (playerId == null) // null while disconnecting
                return;
            Transform row = contentTarget.Find(playerId);
            if (row != null) { // may not be instantiated yet
                RemovePrefab(row);
                UpdateLayout();
            }
        }

        void OnDisconnected() {
            ClearPrefabs();
            RefreshCount();
        }

        // Rows measured while the panel was hidden have no size; measure again when it opens.
        void OnPanelVisibilityChanged(HudPanelId panel, bool visible) {
            if (panel == HudPanelId.PlayerList && visible)
                RefreshFilter();
        }

        void RefreshCount() {
            if (countText != null)
                countText.text = (PlayerData.Players?.Count ?? 0).ToString();
        }
    }
}
