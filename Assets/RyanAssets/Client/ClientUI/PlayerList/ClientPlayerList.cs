using UnityEngine;
using UnityEngine.UI;
using RyanAssets.Shared.Player;
using RyanAssets.DataService;
using FishNet;
using FishNet.Transporting;
using FishNet.Connection;
using RyanAssets.UI.ListGrid;
using System.Collections.Generic;

namespace RyanAssets.Client.ClientUI.PlayerList {
    public class ClientPlayerList : ListGridUI<(NetworkConnection conn, ServerPlayerStats data)> {
        protected override void Start() {
            base.Start();
            // SharedGlobalEvents.OnPlayerAdded += OnPlayerAdded;
            SharedGlobalEvents.RegisterLatePlayerAdd(OnPlayerAdded);
            SharedGlobalEvents.OnPlayerRemoved += OnPlayerRemoved;
            OnCreatePrefab += OnAddPrefab;
        }
        private void OnAddPrefab(GameObject prefab, (NetworkConnection conn, ServerPlayerStats data) player) {
            if (!SharedGlobalEvents.Instance.Players.Contains(new KeyValuePair<NetworkConnection, ServerPlayerStats>(player.conn, player.data))) { // incase bro left while loading
                Destroy(prefab);
                return;
            }
            string username = player.data.data.username;
            prefab.name = player.data.player_id;
            prefab.transform.GetChild(1).GetComponent<Text>().text = username;
        }
        private void OnPlayerAdded(NetworkConnection conn, ServerPlayerStats data) {
            AddPrefab((conn, data));
        }
        private void OnPlayerRemoved(NetworkConnection conn, ServerPlayerStats data) {
            Transform item = contentTarget.Find(data.player_id);
            if (item) { // item might not be inserted yet!
                RemovePrefab(item);
                UpdateLayout();
            }
        }
    }
}