using FishNet;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    public class ServerCoin: MonoBehaviour {
        public static ServerCoin Instance { get; private set; }
        public List<NetworkObject> coinPrefabs = new();
        void Awake() {
            Instance = this;
        }
        public static GameObject SpawnCoin(Vector3? spawnLoc) {
            return SpawnCoin(0, spawnLoc);
        }
        public static GameObject SpawnCoin(int coinIdx, Vector3? spawnLoc) {
            if (Instance == null) return null;

            NetworkObject spawnPrefab = Instance.coinPrefabs[coinIdx];
            Vector3 realTargetLoc = spawnLoc ?? ServerPathfinding.GetRandomPosition();
            realTargetLoc += Vector3.up * spawnPrefab.GetComponentInChildren<Renderer>().bounds.extents.y;

            NetworkObject coin = InstanceFinder.NetworkManager.GetPooledInstantiated(
                spawnPrefab,
                realTargetLoc,
                Quaternion.identity,
                asServer: true
            );

            InstanceFinder.ServerManager.Spawn(coin);
            return coin.gameObject;
        }
        public static void ClearAllCoins() {
            if (Instance == null) return;

            foreach (GameObject coin in GameObject.FindGameObjectsWithTag("CoinItem")) {
                NetworkObject nob = coin.GetComponent<NetworkObject>();

                if (nob != null && nob.IsSpawned)
                    nob.Despawn(DespawnType.Pool);
            }
        }
    }
}