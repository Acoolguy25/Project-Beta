using System.Collections.Generic;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Editor {
    /// <summary>
    /// Authors the match economy's networked singleton and the serialized wiring that points the
    /// dedicated-server runner at everything it spawns.
    /// <para>
    /// Deliberately free of any reference to the client assembly, because this half has to compile and
    /// run inside the dedicated-server Editor - the only context where the runner's own script exists.
    /// </para>
    /// </summary>
    public static class WV_AuthoringServer {
        const string Root = WV_Authoring.Root;
        const string EconomyPath = Root + "/Server/WV_Economy.prefab";
        const string RunnerPath = Root + "/Server/WV_ServerRunner.prefab";
        const string ServerScenePath = Root + "/war_valley_server.unity";


        [MenuItem("Ryan/War Valley/Rebuild Economy Prefab")]
        public static void RebuildEconomyPrefab() {
            BuildEconomyPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("War Valley: rebuilt the match economy prefab.");
        }

        /// <summary>
        /// Points the runner at everything it spawns. Must be run from the dedicated-server Editor:
        /// the runner's script lives in a UNITY_SERVER-only assembly and is a missing script anywhere
        /// else, where saving the prefab would discard its data.
        /// </summary>
        [MenuItem("Ryan/War Valley/Wire Server Runner")]
        public static void WireServerRunner() {
            var runner = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPath);
            if (runner == null) {
                Debug.LogError($"War Valley: no runner prefab at {RunnerPath}.");
                return;
            }

            MonoBehaviour runnerComponent = null;
            foreach (MonoBehaviour behaviour in runner.GetComponents<MonoBehaviour>()) {
                if (behaviour != null && behaviour.GetType().Name == "WV_ServerRunner") {
                    runnerComponent = behaviour;
                    break;
                }
            }

            if (runnerComponent == null) {
                Debug.LogError(
                    "War Valley: WV_ServerRunner resolved to a missing script. Run this from the " +
                    "dedicated-server Editor (Standalone target with the Server subtarget), where the " +
                    "war_valley Server assembly is compiled.");
                return;
            }

            var economy = AssetDatabase.LoadAssetAtPath<WV_Economy>(EconomyPath);
            List<Object> structures = LoadBuildableStructures();

            var serialized = new SerializedObject(runnerComponent);
            SerializedProperty builds = serialized.FindProperty("_buildableStructures");
            builds.arraySize = structures.Count;
            for (int i = 0; i < structures.Count; i++)
                builds.GetArrayElementAtIndex(i).objectReferenceValue = structures[i];

            serialized.FindProperty("_economyPrefab").objectReferenceValue = economy;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(runner);
            AssetDatabase.SaveAssets();
            Debug.Log($"War Valley: wired {structures.Count} buildable structures and the economy prefab onto the runner.");
        }

        /// <summary>
        /// Appends the generated structures to the runner instance that actually ships in
        /// war_valley_server.unity.
        /// <para>
        /// That instance overrides <c>_buildableStructures</c>, so a value written onto the prefab
        /// alone is shadowed and never reaches the running server. Existing entries and every other
        /// override on the instance - wave data, debug timers - are left exactly as authored.
        /// </para>
        /// </summary>
        [MenuItem("Ryan/War Valley/Wire Server Scene")]
        public static void WireServerScene() {
            if (Application.isPlaying) {
                Debug.LogError("War Valley: leave Play Mode before editing the server scene.");
                return;
            }

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ServerScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try {
                MonoBehaviour runner = FindRunnerInScene(scene);
                if (runner == null) {
                    Debug.LogError(
                        "War Valley: no usable WV_ServerRunner in the server scene. Run this from the " +
                        "dedicated-server Editor, where the war_valley Server assembly is compiled.");
                    return;
                }

                var serialized = new SerializedObject(runner);
                SerializedProperty builds = serialized.FindProperty("_buildableStructures");

                var existing = new HashSet<Object>();
                for (int i = 0; i < builds.arraySize; i++) {
                    Object value = builds.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (value != null)
                        existing.Add(value);
                }

                int added = 0;
                foreach (Object structure in LoadBuildableStructures()) {
                    if (!existing.Add(structure))
                        continue;
                    builds.arraySize++;
                    builds.GetArrayElementAtIndex(builds.arraySize - 1).objectReferenceValue = structure;
                    added++;
                }

                // Only set when the instance has no override of its own, so an intentional
                // per-scene economy prefab would survive a re-run.
                SerializedProperty economy = serialized.FindProperty("_economyPrefab");
                if (economy.objectReferenceValue == null)
                    economy.objectReferenceValue = AssetDatabase.LoadAssetAtPath<WV_Economy>(EconomyPath);

                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                Debug.Log($"War Valley: server scene runner now offers {builds.arraySize} structures ({added} added).");
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        static MonoBehaviour FindRunnerInScene(UnityEngine.SceneManagement.Scene scene) {
            foreach (GameObject rootObject in scene.GetRootGameObjects()) {
                foreach (MonoBehaviour behaviour in rootObject.GetComponentsInChildren<MonoBehaviour>(true)) {
                    if (behaviour != null && behaviour.GetType().Name == "WV_ServerRunner")
                        return behaviour;
                }
            }
            return null;
        }

        static List<Object> LoadBuildableStructures() {
            var results = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Structures" })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                // Only the generated War Valley structures carry a constructable. The runner also
                // offers these on its own at startup (WV_ServerRunner.OfferGeneratedStructures), so
                // wiring them here is for the Inspector's record rather than required.
                if (prefab != null
                    && prefab.GetComponent<WV_Constructable>() != null
                    && prefab.TryGetComponent(out RyanAssets.Shared.Declarations.StructureComponent structure))
                    results.Add(structure);
            }
            results.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return results;
        }

        static GameObject BuildEconomyPrefab() {
            var root = new GameObject("WV_Economy");
            try {
                root.AddComponent<NetworkObject>();
                root.AddComponent<WV_Economy>();
                return PrefabUtility.SaveAsPrefabAsset(root, EconomyPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

    }
}
