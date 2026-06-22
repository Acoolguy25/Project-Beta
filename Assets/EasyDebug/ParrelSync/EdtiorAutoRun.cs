#if UNITY_SERVER && UNITY_EDITOR
using ParrelSync;
using UnityEditor;
using UnityEngine;


    [InitializeOnLoad]
    public static class EditorStartup {
        static EditorStartup() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (ClonesManager.IsClone()) {
                if (ClonesManager.GetArgument() == "server") {
                    EditorApplication.EnterPlaymode();
                    EditorWindow.FocusWindowIfItsOpen<SceneView>();
                }
            }
        }
        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
                return;

            EditorWindow.GetWindow<SceneView>().Focus();
        }

    }
#endif