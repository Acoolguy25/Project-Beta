using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Universes;

public class SceneSwitcherWindow : EditorWindow {
    private const string AllUniversesLabel = "All Universes";
    private const string UniverseScenesRoot = "Assets/Universes/UniverseData";

    private readonly List<SceneEntry> scenes = new();
    private string[] universeLabels = { AllUniversesLabel };
    private string[] universeIds = { string.Empty };
    private int selectedUniverse;

    [MenuItem("Window/Scene Switcher")]
    private static void ShowWindow() {
        GetWindow<SceneSwitcherWindow>("Scene Switcher");
    }

    private void OnEnable() {
        Refresh();
    }

    private void OnGUI() {
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope()) {
            EditorGUI.BeginChangeCheck();
            selectedUniverse = EditorGUILayout.Popup("Universe", selectedUniverse, universeLabels);
            if (EditorGUI.EndChangeCheck())
                RefreshScenes();

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                Refresh();
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Revert to Default Scene", GUILayout.Height(28)))
            RevertToDefaultScene();

        EditorGUILayout.Space(8);
        if (scenes.Count == 0) {
            EditorGUILayout.HelpBox("No scenes found for this selection.", MessageType.Info);
            return;
        }

        foreach (SceneEntry scene in scenes) {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField(scene.displayName, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Open", GUILayout.Width(55)))
                    OpenScene(scene.path);
            }
        }
    }

    private void Refresh() {
        UniverseStruct[] configuredUniverses = UniverseCfg.ActiveUniverses;
        universeLabels = new[] { AllUniversesLabel }.Concat(configuredUniverses.Select(universe => universe.title)).ToArray();
        universeIds = new[] { string.Empty }.Concat(configuredUniverses.Select(universe => universe.id)).ToArray();
        selectedUniverse = Mathf.Clamp(selectedUniverse, 0, universeLabels.Length - 1);
        RefreshScenes();
    }

    private void RefreshScenes() {
        scenes.Clear();
        string selectedId = universeIds.Length > selectedUniverse ? universeIds[selectedUniverse] : string.Empty;
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });

        foreach (string guid in sceneGuids) {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (!string.IsNullOrEmpty(selectedId) && !IsSceneInUniverse(path, selectedId))
                continue;

            scenes.Add(new SceneEntry(path));
        }

        scenes.Sort((left, right) => string.Compare(left.displayName, right.displayName, System.StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSceneInUniverse(string scenePath, string universeId) {
        string universeRoot = $"{UniverseScenesRoot}/{universeId}/";
        return scenePath.StartsWith(universeRoot, System.StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenScene(string path) {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        if (scene.IsValid())
            EditorSceneManager.SetActiveScene(scene);
    }

    private static void RevertToDefaultScene() {
#if UNITY_SERVER
        const string defaultScene = "Assets/Scenes/ServerInit.unity";
#else
        const string defaultScene = "Assets/Scenes/MainMenu.unity";
#endif
        if (File.Exists(defaultScene))
            OpenScene(defaultScene);
        else
            Debug.LogError($"Default scene was not found: {defaultScene}");
    }

    private readonly struct SceneEntry {
        public readonly string path;
        public readonly string displayName;

        public SceneEntry(string path) {
            this.path = path;
            displayName = Path.GetFileNameWithoutExtension(path);
        }
    }
}
