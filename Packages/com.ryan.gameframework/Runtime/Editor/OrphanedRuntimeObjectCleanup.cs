using UnityEditor;
using UnityEngine;

namespace RyanAssets.Editor {
    /// <summary>
    /// Removes the hidden helper objects NativeWebSocket leaves behind in the Editor.
    /// <para>
    /// The vendor package creates a <c>MainThreadUtil</c> object at the start of every play session
    /// and never destroys it. The object is hidden and survives leaving Play Mode, so each session adds
    /// one more, and on the domain reload that starts the next session every leftover copy loses its
    /// script and logs "The referenced script (Unknown) on this Behaviour is missing!". The package
    /// only needs its copy while the game runs, so outside Play Mode every copy is a leftover and is
    /// removed: when a session ends, and before the next one starts.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    static class OrphanedRuntimeObjectCleanup {
        const string OrphanName = "MainThreadUtil";

        static OrphanedRuntimeObjectCleanup() {
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            EditorApplication.delayCall += RemoveOrphans;
        }

        static void HandlePlayModeChanged(PlayModeStateChange change) {
            if (change is PlayModeStateChange.EnteredEditMode or PlayModeStateChange.ExitingEditMode)
                RemoveOrphans();
        }

        static void RemoveOrphans() {
            if (EditorApplication.isPlaying)
                return;
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>()) {
                if (candidate == null || candidate.name != OrphanName || candidate.scene.IsValid()
                    || EditorUtility.IsPersistent(candidate))
                    continue;
                Object.DestroyImmediate(candidate, true);
            }
        }
    }
}
