using System.Collections;
using UnityEngine;
using System;
using FishNet.Managing.Server;
using FishNet;
using RyanAssets.Server.ServerFeatures;

namespace RyanAssets.Characters.Server { 
    public static class ServerNPC {
        public static GameObject SpawnNPC(GameObject original, Vector3? location = null) {
            GameObject clone = GameObject.Instantiate(original);
            if (location == null)
                location = ServerPathfinding.GetRandomPosition();
            clone.transform.position = location.Value;
            InstanceFinder.ServerManager.Spawn(clone, null); // spawn with server ownership
            clone.AddComponent<LocalNPC>();
            return clone;
        }
    }
}