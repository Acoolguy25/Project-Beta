using UnityEditor;
using UnityEngine;

namespace RyanAssets.Editor {
    /// <summary>Local server selection without changing or committing bootstrap code.</summary>
    public sealed class ServerUniverseSettings : EditorWindow {
        const string Preference = "RyanAssets.Server.EditorUniverse";
        string universe;
        [MenuItem("Ryan/Server/Editor Universe")]
        static void Open() => GetWindow<ServerUniverseSettings>("Server Universe");
        void OnEnable() => universe = EditorPrefs.GetString(Preference, "war_valley");
        void OnGUI() {
            EditorGUILayout.LabelField("Universe for the next server Play session", EditorStyles.boldLabel);
            universe = EditorGUILayout.TextField("Universe ID", universe);
            EditorGUILayout.HelpBox("Use the universe folder name, such as classic_horror. This setting applies to server Editors on this computer.", MessageType.Info);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(universe)))
                if (GUILayout.Button("Save server selection")) EditorPrefs.SetString(Preference, universe.Trim());
        }
    }
}
