#if UNITY_SERVER && UNITY_EDITOR
using ParrelSync;
using UnityEditor;
using UnityEngine;


    [InitializeOnLoad]
    public static class EditorStartup {
        static EditorStartup() {
            UnityEngine.Debug.Log("Editor loaded or scripts recompiled");
            if (ClonesManager.IsClone()) {
                if (ClonesManager.GetArgument() == "server") {
                    EditorApplication.EnterPlaymode();
                }
            }
        }
    }
#endif