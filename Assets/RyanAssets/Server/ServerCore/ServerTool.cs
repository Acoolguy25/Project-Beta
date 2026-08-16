using FishNet;
using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Server.ServerCore
{
    public class ServerTool : MonoBehaviour {
        [SerializeField]
        List<ToolBaseShared> toolPrefab;
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
        public ToolBaseShared SpawnTool(NetworkObject networkObject, ToolEnum tool) {
            int toolEnumIndex = (int)tool - 1;
            if (toolPrefab.Count <= toolEnumIndex) {
                Debug.LogWarning($"Tried to add tool {tool} but no prefab exists for it");
                return null;
            }
            GameObject toolClone = Instantiate(toolPrefab[toolEnumIndex].gameObject);
            ToolBaseShared toolBase = toolClone.GetComponent<ToolBaseShared>();
            toolBase.transform.localPosition = Vector3.zero;
            toolBase.transform.localRotation = Quaternion.identity;
            toolBase.connectedCharacter = networkObject.GetComponent<GameCharacter>();
            InitTool(toolBase);
            InstanceFinder.ServerManager.Spawn(toolClone, ownerConnection: networkObject.Owner);
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
        public void DespawnTool(NetworkConnection player, ToolEnum tool) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                foreach (var toolBase in character.GetComponentsInChildren<ToolBaseShared>(true)) {
                    if (toolBase.toolEnum == tool) {
                        InstanceFinder.ServerManager.Despawn(toolBase.gameObject);
                        break;
                    }
                }
            }
        }

        public ToolBaseShared GetTool(NetworkConnection player, ToolEnum tool) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                return GetTool(character.NetworkObject, tool);
            }
            return null;
        }

        public ToolBaseShared GetTool(NetworkObject networkObject, ToolEnum tool) {
            GameCharacter character = networkObject.GetComponent<GameCharacter>();
            if (character != null) {
                foreach (var toolBase in character.GetComponentsInChildren<ToolBaseShared>(true)) {
                    if (toolBase.toolEnum == tool) {
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
        public void RemoveTool(NetworkConnection player, ToolEnum tool) {
            if (PlayerData.Players.TryGetValue(player, out PlayerData stats)) {
                stats.tools.Remove(tool);
                DespawnTool(player, tool);
            }
        }
    }
}