using System.Collections;
using UnityEngine;
using System;
using FishNet.Managing.Server;
using FishNet;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Shared.Global;

namespace RyanAssets.Characters.Server { 
    public static class ServerNPC {
        /// <summary>Spawn a catalog character. Existing calls default to the original robot.</summary>
        public static LocalNPC SpawnNPC(NPCCharacter character = NPCCharacter.Robot, Vector3? location = null,
            UnityEngine.SceneManagement.Scene scene = default) {
            var catalog = Resources.Load<NPCCharacterCatalog>("NPCCharacters");
            var prefab = catalog != null ? catalog.GetPrefab(character) : null;
            if (prefab == null) throw new InvalidOperationException($"NPCCharacters catalog has no {character} prefab.");
            return SpawnNPC(prefab, location, scene);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            ServerRunner.OnResetEvent += Reset;
        }
        public static int ClearObjectsWithTag(string tagName) {
            int count = 0;
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag(tagName)) {
                InstanceFinder.ServerManager.Despawn(obj);
                count++;
            }
            return count;
        }
        public static LocalNPC SpawnNPC(GameObject original, Vector3? location = null, UnityEngine.SceneManagement.Scene scene = default) {
            GameObject clone = GameObject.Instantiate(original);
            if (location == null)
                location = ServerPathfinding.GetRandomPosition();
            clone.transform.position = location.Value;
            GameCharacter gameCharacter = clone.GetComponent<GameCharacter>();
            InstanceFinder.ServerManager.Spawn(clone, null, scene); // spawn with server ownership in the observing scene
            // Init writes SyncVars and therefore must happen after FishNet initializes the object.
            gameCharacter.Init(100);
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
        public static void Reset() {
            ClearAllNPC();
        }
    }
}
