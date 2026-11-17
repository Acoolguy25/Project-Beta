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
            System.DateTime before = System.IO.File.GetLastWriteTimeUtc(RunnerPath);
            GameObject prefab = PrefabUtility.LoadPrefabContents(RunnerPath);
            try {
                if (!prefab.TryGetComponent(out Runner runner))
                    throw new System.InvalidOperationException(RunnerPath + " has no CH_ServerRunner.");
                EditorUtility.SetDirty(runner);
                PrefabUtility.SaveAsPrefabAsset(prefab, RunnerPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
            // A ParrelSync clone with asset saving blocked drops the write silently, so check the file itself.
            if (System.IO.File.GetLastWriteTimeUtc(RunnerPath) == before)
                Debug.LogWarning("Classic Horror: " + RunnerPath + " was not written; this Editor blocks asset saves (for example a ParrelSync clone).");
            else Debug.Log("Classic Horror: saved the runner tuning into " + RunnerPath);
        }
    }
}
