using System.Collections;
using UnityEngine;
using System;
using FishNet.Managing.Server;
using FishNet;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Shared;

namespace RyanAssets.Characters.Server { 
    public static class ServerNPC {
        public static LocalNPC SpawnNPC(GameObject original, Vector3? location = null) {
            GameObject clone = GameObject.Instantiate(original);
            if (location == null)
                location = ServerPathfinding.GetRandomPosition();
            clone.transform.position = location.Value;
            clone.GetComponent<GameCharacter>().Init(100);
            InstanceFinder.ServerManager.Spawn(clone, null); // spawn with server ownership
            return clone.AddComponent<LocalNPC>();
        }
    }
}