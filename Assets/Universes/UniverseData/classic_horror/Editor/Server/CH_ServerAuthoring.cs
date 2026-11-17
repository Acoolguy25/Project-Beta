using UnityEditor;
using UnityEngine;
using Runner = Universes.UniverseData.classic_horror.Server.CH_ServerRunner;

namespace Universes.UniverseData.classic_horror.Editor.Server {
    /// <summary>
    /// Server-side authoring for the horror case. CH_ServerRunner only compiles under the dedicated-server
    /// profile, so its prefab can only be saved from the server Editor; this assembly exists there alone.
    /// </summary>
    public static class CH_ServerAuthoring {
        const string RunnerPath = "Assets/Universes/UniverseData/classic_horror/Server/CH_ServerRunner.prefab";

        /// <summary>
        /// Re-saves the runner prefab so every serialized field (the Presence's tuning block, the scare,
        /// grace, enrage and repel timings) is written into the asset with its current value. Fields that
        /// were added in code since the last save take their code defaults; values already set are kept.
        /// </summary>
        [MenuItem("Ryan/Classic Horror/Server/Save Runner Tuning")]
        public static void SaveRunnerTuning() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Leave Play Mode before saving the runner prefab.");
            if (IsParrelSyncClone)
                throw new System.InvalidOperationException("This Editor is a ParrelSync clone, which does not save assets. Open the main "
                    + "project with the server build profile to save the runner prefab.");
            GameObject prefab = PrefabUtility.LoadPrefabContents(RunnerPath);
            try {
                if (!prefab.TryGetComponent(out Runner runner))
                    throw new System.InvalidOperationException(RunnerPath + " has no CH_ServerRunner.");
                EditorUtility.SetDirty(runner);
                PrefabUtility.SaveAsPrefabAsset(prefab, RunnerPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
            Debug.Log("Classic Horror: saved the runner tuning into " + RunnerPath);
        }

        /// <summary>ParrelSync marks a clone project with an empty ".clone" file in its root folder.</summary>
        static bool IsParrelSyncClone =>
            System.IO.File.Exists(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, ".clone"));
    }
}
