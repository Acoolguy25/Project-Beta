using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Universes;

public class SceneSwitcherWindow : EditorWindow {
    private const string AllUniversesLabel = "All Universes";
    private const string UniverseScenesRoot = "Assets/Universes/UniverseData";
    private const string ClientDebugPlayGameSourcePath = "Assets/EasyDebug/Client/DebugPlayGame.cs";
    private const string ServerBootStrapSourcePath = "Assets/RyanAssets/Server/ServerCore/ServerBootStrap.cs";
    private const string PreviousScenePathSessionKey = "SceneSwitcherWindow.PreviousScenePath";

    private readonly List<SceneEntry> scenes = new();
    private string[] universeLabels = { AllUniversesLabel };
    private string[] universeIds = { string.Empty };
    private int selectedUniverse;

    [InitializeOnLoadMethod]
    private static void InitializePlayModeHandler() {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state) {
        switch (state) {
            case PlayModeStateChange.ExitingEditMode:
                // A domain reload clears static fields before EnteredEditMode fires.
                SessionState.SetString(PreviousScenePathSessionKey, SceneManager.GetActiveScene().path);
                RevertToDefaultScene();
                break;

            case PlayModeStateChange.EnteredEditMode:
                RestorePreviousScene();
                break;
        }
    }

    [MenuItem("Tools/Scene Switcher")]
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

            if (GUILayout.Button("Update", GUILayout.Width(70)))
                UpdateUniverseIds();
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

        universeLabels = new[] { AllUniversesLabel }
            .Concat(configuredUniverses.Select(universe => universe.title))
            .ToArray();

        universeIds = new[] { string.Empty }
            .Concat(configuredUniverses.Select(universe => universe.id))
            .ToArray();

        SelectConfiguredDefaultUniverse();

        RefreshScenes();
    }

    private void SelectConfiguredDefaultUniverse() {
        string configuredUniverseId = GetConfiguredDefaultUniverseId();
        int configuredUniverseIndex = System.Array.IndexOf(
            universeIds,
            configuredUniverseId);

        selectedUniverse = configuredUniverseIndex >= 0
            ? configuredUniverseIndex
            : Mathf.Clamp(selectedUniverse, 0, universeLabels.Length - 1);
    }

    private static string GetConfiguredDefaultUniverseId() {
        // Debug Play is the active client-side selection. Fall back to the server
        // bootstrap value so the window remains useful in server-only setups.
        return ReadUniverseIdSource(
                   ClientDebugPlayGameSourcePath,
                   "PlayGameUniverseId")
               ?? ReadUniverseIdSource(
                   ServerBootStrapSourcePath,
                   "universe_id")
               ?? string.Empty;
    }

    private static string ReadUniverseIdSource(string sourcePath, string fieldName) {
        if (!File.Exists(sourcePath))
            return null;

        string source = File.ReadAllText(sourcePath);
        Match match = Regex.Match(
            source,
            $@"\b{Regex.Escape(fieldName)}\s*=\s*""(?<universeId>[^""]+)""");

        return match.Success ? match.Groups["universeId"].Value : null;
    }

    private void RefreshScenes() {
        scenes.Clear();

        string selectedId = universeIds.Length > selectedUniverse
            ? universeIds[selectedUniverse]
            : string.Empty;

        string[] sceneGuids = AssetDatabase.FindAssets(
            "t:Scene",
            new[] { "Assets" });

        foreach (string guid in sceneGuids) {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');

            if (!string.IsNullOrEmpty(selectedId) &&
                !IsSceneInUniverse(path, selectedId))
                continue;

            scenes.Add(new SceneEntry(path));
        }

        scenes.Sort((left, right) =>
            string.Compare(
                left.displayName,
                right.displayName,
                System.StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateUniverseIds() {
        if (selectedUniverse <= 0 || selectedUniverse >= universeIds.Length) {
            EditorUtility.DisplayDialog(
                "Update Universe",
                "Select a specific universe before updating the client and server.",
                "OK");
            return;
        }

        string universeId = universeIds[selectedUniverse];
        string universeLabel = universeLabels[selectedUniverse];

        bool confirmed = EditorUtility.DisplayDialog(
            "Update Universe",
            $"Set both the client and server universe source values to:\n\n{universeLabel} ({universeId})?\n\nThe source changes will persist after restart.",
            "Update",
            "Cancel");

        if (!confirmed)
            return;

        if (!UpdateUniverseIdSource(
                ClientDebugPlayGameSourcePath,
                "PlayGameUniverseId",
                universeId) ||
            !UpdateUniverseIdSource(
                ServerBootStrapSourcePath,
                "universe_id",
                universeId) ||
            !UpdateLocalNetworkConfigUniverseId(universeId))
            return;

#if UNITY_SERVER
        RyanAssets.Server.ServerCore.ServerBootStrap.serverInfo.universe_id = universeId;

        if (Application.isPlaying) {
            RyanAssets.Server.ServerCore.ServerBootStrap.ApplyServerInfo();
        }
#endif

        AssetDatabase.Refresh();

        ShowNotification(new GUIContent($"Updated client and server to {universeId}"));
    }

    private static bool UpdateUniverseIdSource(
        string sourcePath,
        string fieldName,
        string universeId) {
        if (!File.Exists(sourcePath)) {
            EditorUtility.DisplayDialog(
                "Update Universe",
                $"The source file was not found: {sourcePath}",
                "OK");
            return false;
        }

        string source = File.ReadAllText(sourcePath);
        // Matches `field = "id"` and the ternary form `field = cond ? x : "id"`.
        string pattern = $@"(?<prefix>\b{Regex.Escape(fieldName)}\s*=\s*(?:[^;""\r\n]*?:\s*)?"")(?<id>[^""]*)(?<suffix>"")";
        Match match = Regex.Match(source, pattern);

        if (!match.Success) {
            EditorUtility.DisplayDialog(
                "Update Universe",
                $"Could not find a '{fieldName}' universe assignment in {sourcePath}.",
                "OK");
            return false;
        }

        if (match.Groups["id"].Value == universeId)
            return true;

        string updatedSource = source.Substring(0, match.Groups["id"].Index)
                               + universeId
                               + source.Substring(match.Groups["id"].Index + match.Groups["id"].Length);

        File.WriteAllText(sourcePath, updatedSource);
        return true;
    }

    // In the Editor the server reads this asset when editor_direct_connection is on.
    private static bool UpdateLocalNetworkConfigUniverseId(string universeId) {
        const string configPath = "Assets/Resources/NetworkSettings/LocalNetworkConfig.asset";
        Object config = AssetDatabase.LoadAssetAtPath<Object>(configPath);
        var serialized = config != null ? new SerializedObject(config) : null;
        SerializedProperty property = serialized?.FindProperty("editor_universe_id");

        if (property == null) {
            EditorUtility.DisplayDialog(
                "Update Universe",
                $"Could not find 'editor_universe_id' on {configPath}.",
                "OK");
            return false;
        }

        if (property.stringValue == universeId)
            return true;

        property.stringValue = universeId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        return true;
    }

    private static bool IsSceneInUniverse(string scenePath, string universeId) {
        string universeRoot = $"{UniverseScenesRoot}/{universeId}/";

        return scenePath.StartsWith(
            universeRoot,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenScene(string path) {
        if (SceneManager.GetActiveScene().path == path)
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(
            path,
            OpenSceneMode.Single);

        if (scene.IsValid())
            EditorSceneManager.SetActiveScene(scene);
    }

    private static void RevertToDefaultScene() {
#if UNITY_SERVER
        const string defaultScene = "Assets/Scenes/ServerInit.unity";
#else
        const string defaultScene = "Assets/Scenes/MainMenu.unity";
#endif

        if (!File.Exists(defaultScene)) {
            Debug.LogError($"Default scene was not found: {defaultScene}");
            return;
        }

        if (SceneManager.GetActiveScene().path == defaultScene)
            return;

        OpenScene(defaultScene);
    }

    private static void RestorePreviousScene() {
        string previousScenePath = SessionState.GetString(PreviousScenePathSessionKey, string.Empty);

        if (string.IsNullOrEmpty(previousScenePath))
            return;

        if (!File.Exists(previousScenePath)) {
            Debug.LogWarning(
                $"Previous scene no longer exists: {previousScenePath}");

            SessionState.SetString(PreviousScenePathSessionKey, string.Empty);
            return;
        }

        if (SceneManager.GetActiveScene().path != previousScenePath)
            OpenScene(previousScenePath);

        SessionState.SetString(PreviousScenePathSessionKey, string.Empty);
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
