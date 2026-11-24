using System.IO;
using RyanAssets.Characters.Shared;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// Bakes the arena's NavMesh for the bots. The surface sits on the map root and uses the shared robot
    /// NPC's own agent type (read from its prefab through the NPCCharacters catalog, so a change there is
    /// picked up on the next build), collects physics colliders inside <see cref="CTF_ArenaAuthoring.NavMeshVolume"/>,
    /// and saves the data as an asset next to the scene so the server clone loads the same mesh.
    /// </summary>
    internal static class CTF_NavMeshAuthoring {
        const string CatalogResource = "NPCCharacters";
        const string AssetFolder = CTF_Authoring.Root + "/Navigation";
        const string AssetPath = AssetFolder + "/FlagWarsNavMesh.asset";

        public static NavMeshSurface Bake(GameObject mapRoot, Bounds volume) {
            if (!mapRoot.TryGetComponent(out NavMeshSurface surface)) surface = mapRoot.AddComponent<NavMeshSurface>();
            surface.agentTypeID = RobotAgentType();
            surface.collectObjects = CollectObjects.Volume;
            // The map root sits at the origin unrotated, so the world volume is its local volume.
            surface.center = mapRoot.transform.InverseTransformPoint(volume.center);
            surface.size = volume.size;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new System.InvalidOperationException("[FlagWars] The NavMesh bake produced no data: is the arena built with colliders?");
            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder(CTF_Authoring.Root, Path.GetFileName(AssetFolder));
            if (AssetDatabase.LoadAssetAtPath<Object>(AssetPath) != null) AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, AssetPath);
            EditorUtility.SetDirty(surface);
            Debug.Log($"[FlagWars] NavMesh baked for agent type {surface.agentTypeID} into {AssetPath}.");
            return surface;
        }

        /// <summary>The agent type of the robot the bots are spawned as.</summary>
        static int RobotAgentType() {
            var catalog = Resources.Load<NPCCharacterCatalog>(CatalogResource);
            var robot = catalog != null ? catalog.GetPrefab(NPCCharacter.Robot) : null;
            var agent = robot != null ? robot.GetComponent<NavMeshAgent>() : null;
            if (agent == null) throw new System.InvalidOperationException($"[FlagWars] Resources/{CatalogResource} has no robot prefab with a NavMeshAgent to bake for.");
            return agent.agentTypeID;
        }
    }
}
