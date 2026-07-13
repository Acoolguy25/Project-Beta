using System.Collections;
using UnityEngine;
using System;
using FishNet.Managing.Server;
using FishNet;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;

namespace RyanAssets.Characters.Server { 
    public static class ServerNPC {
        public static int ClearObjectsWithTag(string tagName) {
            int count = 0;
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag(tagName)) {
                InstanceFinder.ServerManager.Despawn(obj);
                count++;
            }
            return count;
        }
        public static LocalNPC SpawnNPC(GameObject original, Vector3? location = null) {
            GameObject clone = GameObject.Instantiate(original);
            if (location == null)
                location = ServerPathfinding.GetRandomPosition();
            clone.transform.position = location.Value;
            GameCharacter gameCharacter = clone.GetComponent<GameCharacter>();
            gameCharacter.Init(100);
            InstanceFinder.ServerManager.Spawn(clone, null); // spawn with server ownership
            LocalNPC npc = clone.AddComponent<LocalNPC>();
            return npc;
        }
        public static int ClearDeadNPC() {
            return ClearObjectsWithTag("DeadNPC");
        }
        public static int ClearAliveNPC() {
            return ClearObjectsWithTag("NPC");
        }
        public static int ClearAllNPC() {
            return ClearAliveNPC() + ClearDeadNPC();
        }
    }
}