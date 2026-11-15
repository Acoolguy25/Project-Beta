#if UNITY_SERVER && UNITY_EDITOR
using UnityEditor;
using UnityEngine;


    [InitializeOnLoad]
    public static class EditorStartup {
        static EditorStartup() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                // if (ClonesManager.GetArgument() == "server") {
#if UNITY_SERVER
                    //EditorApplication.EnterPlaymode();
                    //EditorWindow.FocusWindowIfItsOpen<SceneView>();
#endif
        }
        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
                return;

            //EditorWindow.GetWindow<SceneView>().Focus();
        }

    }
#endif