using FishNet;
using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Item.FloatingTool;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

namespace RyanAssets.Server.ServerCore
{
    public class ServerTool : MonoBehaviour {
        [SerializeField]
        List<ToolBaseShared> toolPrefab;
        // Tools are parented to the character only after their network spawn has
        // completed. Keep an authoritative server-side record so they can still
        // be found (and cleaned up) during that window or while a player leaves.
        readonly Dictionary<NetworkObject, Dictionary<ToolEnum, ToolBaseShared>> characterTools = new();
        [SerializeField]
        FloatingToolShared floatingToolPrefab;
        public static ServerTool Instance { get; private set; }
        void Awake() {
            Instance = this;
        }
        void InitTool(ToolBaseShared toolBase) {
            toolBase.staminaCostSync.Value = toolBase.staminaCostInit;
            toolBase.hitDamageSync.Value = toolBase.hitDamageInit;
            toolBase.attackCooldownSync.Value = toolBase.attackCooldownInit;
            toolBase.reloadDurationSync.Value = toolBase.reloadDurationInit;
            toolBase.maxClipAmmoSync.Value = toolBase.maxClipAmmoInit;
        }
        bool TryGetTrackedTool(NetworkObject character, ToolEnum tool, out ToolBaseShared toolBase) {
            toolBase = null;
            if (!characterTools.TryGetValue(character, out Dictionary<ToolEnum, ToolBaseShared> tools) ||
                !tools.TryGetValue(tool, out toolBase))
                return false;

            if (toolBase != null && toolBase.IsSpawned)
                return true;

            tools.Remove(tool);
            if (tools.Count == 0)
                characterTools.Remove(character);
            toolBase = null;
            return false;
        }
        void TrackTool(NetworkObject character, ToolBaseShared toolBase) {
            if (!characterTools.TryGetValue(character, out Dictionary<ToolEnum, ToolBaseShared> tools)) {
                tools = new();
                characterTools[character] = tools;
            }
            tools[toolBase.toolEnum] = toolBase;
        }
        public ToolBaseShared SpawnTool(NetworkObject networkObject, ToolEnum tool, Action<ToolBaseShared> onSpawned = null) {
            if (networkObject == null || !networkObject.IsSpawned) {
                Debug.LogWarning($"Tried to add tool {tool} to an unspawned character");
                return null;
            }
            if (TryGetTrackedTool(networkObject, tool, out ToolBaseShared existingTool))
                return existingTool;

            int toolEnumIndex = (int)tool - 1;
            if (toolEnumIndex < 0 || toolPrefab.Count <= toolEnumIndex) {
                Debug.LogWarning($"Tried to add tool {tool} but no prefab exists for it");
                return null;
            }
            GameObject toolClone = Instantiate(toolPrefab[toolEnumIndex].gameObject);
            ToolBaseShared toolBase = toolClone.GetComponent<ToolBaseShared>();
            toolBase.transform.localPosition = Vector3.zero;
            toolBase.transform.localRotation = Quaternion.identity;
            toolBase.connectedCharacter = networkObject.GetComponent<GameCharacter>();
            InitTool(toolBase);
            onSpawned?.Invoke(toolBase);
            InstanceFinder.ServerManager.Spawn(toolClone, ownerConnection: networkObject.Owner);
            TrackTool(networkObject, toolBase);
            return toolBase;
        }
        public ToolBaseShared SpawnTool(NetworkConnection player, ToolEnum tool) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                return SpawnTool(character.NetworkObject, tool);
            }
            else {
                Debug.LogWarning($"Tried to add tool {tool} for player {player} but no character exists for them");
                return null;
            }
        }
        public bool DespawnTool(NetworkConnection player, ToolEnum tool) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                return DespawnTool(character.NetworkObject, tool);
            }
            return false;
        }
        public bool DespawnTool(NetworkObject character, ToolEnum tool) {
            if (character == null)
                return false;

            if (TryGetTrackedTool(character, tool, out ToolBaseShared toolBase)) {
                characterTools[character].Remove(tool);
                if (characterTools[character].Count == 0)
                    characterTools.Remove(character);
                InstanceFinder.ServerManager.Despawn(toolBase.gameObject);
                return true;
            }

            // Compatibility cleanup for tools created before they were tracked.
            foreach (ToolBaseShared attachedTool in character.GetComponentsInChildren<ToolBaseShared>(true)) {
                if (attachedTool.toolEnum == tool && attachedTool.IsSpawned)
                    InstanceFinder.ServerManager.Despawn(attachedTool.gameObject);
            }
            return false;
        }
        public void DespawnTools(NetworkObject character) {
            if (character == null)
                return;

            HashSet<ToolBaseShared> toolsToDespawn = new();
            if (characterTools.TryGetValue(character, out Dictionary<ToolEnum, ToolBaseShared> trackedTools)) {
                foreach (ToolBaseShared tool in trackedTools.Values)
                    toolsToDespawn.Add(tool);
                characterTools.Remove(character);
            }
            // Also remove legacy/untracked duplicates which have already been
            // parented beneath the character.
            foreach (ToolBaseShared tool in character.GetComponentsInChildren<ToolBaseShared>(true))
                toolsToDespawn.Add(tool);

            foreach (ToolBaseShared tool in toolsToDespawn) {
                if (tool != null && tool.IsSpawned && tool.gameObject.scene.IsValid() && InstanceFinder.IsServerStarted)
                    InstanceFinder.ServerManager.Despawn(tool.gameObject);
            }
        }

        public FloatingToolShared SpawnFloatingTool(ToolEnum tool, Vector3 position,
            bool playerCharacterTrigger = true,
            bool npcCharacterTrigger = false) {

            NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
                floatingToolPrefab.NetworkObject,
                position,
                Quaternion.identity,
                true
            );

            FloatingToolShared floatingTool = nob.GetComponent<FloatingToolShared>();

            floatingTool.TargetToolSync.Value = tool;
            floatingTool.playerCharacterTrigger.Value = playerCharacterTrigger;
            floatingTool.npcCharacterTrigger.Value = npcCharacterTrigger;
            floatingTool.OnToolCollectedFunc = OnFloatingToolCollected;

            InstanceFinder.ServerManager.Spawn(nob);

            return floatingTool;
        }

        bool OnFloatingToolCollected(NetworkBehaviour collectObject, ToolEnum tool) {
            return SpawnTool(collectObject, tool) != null;
        }

        public ToolBaseShared GetTool(NetworkConnection player, ToolEnum tool) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                return GetTool(character.NetworkObject, tool);
            }
            return null;
        }

        public ToolBaseShared GetTool(NetworkObject networkObject, ToolEnum tool) {
            if (TryGetTrackedTool(networkObject, tool, out ToolBaseShared trackedTool))
                return trackedTool;

            GameCharacter character = networkObject.GetComponent<GameCharacter>();
            if (character != null) {
                foreach (var toolBase in character.GetComponentsInChildren<ToolBaseShared>(true)) {
                    if (toolBase.toolEnum == tool && toolBase.IsSpawned) {
                        TrackTool(networkObject, toolBase);
                        return toolBase;
                    }
                }
            }
            return null;
        }

        public ToolBaseShared AddTool(NetworkConnection player, ToolEnum tool) {
            if (PlayerData.Players.TryGetValue(player, out PlayerData stats)) {
                stats.tools.Add(tool);
                return SpawnTool(player, tool);
            }
            return null;
        }
        public bool RemoveTool(NetworkConnection player, ToolEnum tool) {
            if (PlayerData.Players.TryGetValue(player, out PlayerData stats)) {
                stats.tools.Remove(tool);
                return DespawnTool(player, tool);
            }
            return false;
        }

        void _clearTools(GameCharacter character) {
            foreach (ToolBaseShared tool in character.Tools) {
                DespawnTool(character, tool.toolEnum);
            }
        }
        public void ClearTools(GameCharacter character) {
            if (character != null) {
                ClearTools(character.Owner);
            }
            else {
                _clearTools(character);
            }
        }

        public void ClearTools(NetworkConnection player) {
            if (PlayerData.Players.TryGetValue(player, out PlayerData stats)) {
                if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                    _clearTools(character);
                }
                stats.tools.Clear();
            }
        }

        public static void ClearFloatingTools() {
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("FloatingTool")) {
                NetworkBehaviour floatingTool = obj.GetComponent<NetworkBehaviour>();
                if (floatingTool != null && floatingTool.IsSpawned)
                    InstanceFinder.ServerManager.Despawn(floatingTool.gameObject);
            }
        }
    }
}
