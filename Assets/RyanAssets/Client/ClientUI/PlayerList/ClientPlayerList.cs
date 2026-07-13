using UnityEngine;
using UnityEngine.UI;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;
using RyanAssets.DataService;
using FishNet;
using FishNet.Transporting;
using FishNet.Connection;
using RyanAssets.UI.ListGrid;
using System.Collections.Generic;
using RyanAssets.Client.ClientCore;

namespace RyanAssets.Client.ClientUI.PlayerList {
    public class ClientPlayerList : ListGridUI<(NetworkConnection conn, PlayerData data)> {
        protected override void Start() {
            base.Start();
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            ClientConnector.OnDisconnected += OnDisconnected;
            OnCreatePrefab += OnAddPrefab;
            PlayerData.RunEach(OnPlayerAdded);
        }
        protected override void OnDestroy() {
            base.OnDestroy();
            PlayerData.OnPlayerAdded -= OnPlayerAdded;
            PlayerData.OnPlayerRemoved -= OnPlayerRemoved;
            ClientConnector.OnDisconnected -= OnDisconnected;
        }
        private void OnAddPrefab(GameObject prefab, (NetworkConnection conn, PlayerData data) player) {
            if (!PlayerData.Players.ContainsValue(player.data)) { // incase bro left while loading
                RemovePrefab(prefab.transform);
                return;
            }
            string username = player.data.username.Value;
            prefab.name = player.data.player_id.Value;
            prefab.GetComponent<ClientLeaderboardPlayer>().Init(player.data);
            //prefab.transform.GetChild(1).GetComponent<Text>().text = username;
        }
        private void OnPlayerAdded(NetworkConnection conn, PlayerData data) {
            AddPrefab((conn, data));
        }
        private void OnPlayerRemoved(NetworkConnection conn, PlayerData data) {
            string player_id = data.player_id.Value;
            if (player_id != null) { // player_id can be null when disconnecting
                Transform item = contentTarget.Find(data.player_id.Value);
                if (item) { // item might not be inserted yet!
                    RemovePrefab(item);
                    UpdateLayout();
                }
            }
        }
        private void OnDisconnected(){
            ClearPrefabs();
        }
    }
}