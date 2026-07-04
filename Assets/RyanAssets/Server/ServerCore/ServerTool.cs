using System.Collections;
using UnityEngine;
using RyanAssets.Shared.Declarations;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Connection;
using RyanAssets.Tools.Shared;
using FishNet;
using RyanAssets.Characters.Shared;

namespace RyanAssets.Server.ServerCore
{
    public class ServerTool : MonoBehaviour {
        [SerializeField]
        List<ToolBaseShared> toolPrefab;
        public static ServerTool Instance { get; private set; }
        void Awake() {
            Instance = this;
        }
        public void AddTool(NetworkObject networkObject, ToolEnum tool) {
            int toolEnumIndex = (int)tool - 1;
            if (toolPrefab.Count <= toolEnumIndex) {
                Debug.LogWarning($"Tried to add tool {tool} but no prefab exists for it");
                return;
            }
            GameObject toolClone = Instantiate(toolPrefab[toolEnumIndex].gameObject);
            ToolBaseShared toolBase = toolClone.GetComponent<ToolBaseShared>();
            toolBase.transform.localPosition = Vector3.zero;
            toolBase.transform.localRotation = Quaternion.identity;
            toolBase.connectedCharacter.Value = networkObject.GetComponent<NetworkObject>();
            InstanceFinder.ServerManager.Spawn(toolClone, ownerConnection: networkObject.Owner);
        }
        public void AddTool(NetworkConnection player, ToolEnum tool) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character)) {
                AddTool(character.NetworkObject, tool);
            }
            else {
                Debug.LogWarning($"Tried to add tool {tool} for player {player} but no character exists for them");
            }
        }
    }
}