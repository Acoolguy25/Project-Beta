using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;

namespace RyanAssets.Editor {
    public static class BuildLocalServer {
        const string ClientDefine = "UNITY_CLIENT";
        const string BuildClientDefine = "BUILD_CLIENT";
        const string ServerBuildDefine = "SERVER_BUILD";
        const string ServerInitScene = "Assets/Scenes/ServerInit.unity";
        const string ServerExecutableName = "GameServer.x86_64";

        public static bool LastBuildRestoredClientDefine { get; private set; }

        public static string LinuxServerDirectory {
            get {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                string projectParentPath = Directory.GetParent(projectPath).FullName;
                return Path.Combine(projectParentPath, "UnityBackend", "LinuxServer");
            }
        }

        public static string LinuxServerExecutablePath => Path.Combine(LinuxServerDirectory, ServerExecutableName);
        public static string LinuxServerUploadDirectory => LinuxServerDirectory.Replace('\\', '/');

        [MenuItem("Build/Local Linux Server")]
        public static void BuildLinuxServer() {
            BuildReport report = BuildLinuxServer(null);

            if (report.summary.result == BuildResult.Succeeded) {
                UnityDebug.Log($"Linux server build complete: {LinuxServerExecutablePath}");
            } else {
                UnityDebug.LogError("Linux server build failed.");
            }
        }

        public static BuildReport BuildLinuxServer(Action<float, string> reportProgress) {
            LastBuildRestoredClientDefine = false;
            reportProgress?.Invoke(0.02f, "Preparing Linux server build");

            Directory.CreateDirectory(LinuxServerDirectory);

            bool restoreClientDefine = HasDefine(NamedBuildTarget.Server, ClientDefine);
            if (restoreClientDefine) {
                RemoveDefine(NamedBuildTarget.Server, ClientDefine);
            }

            string[] scenes = EditorBuildSettings.scenes
                .Select(x => x.path)
                .ToArray();

            scenes[0] = ServerInitScene;

            BuildPlayerOptions options = new() {
                scenes = scenes,
                locationPathName = LinuxServerExecutablePath,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
                // Scoped to this BuildPlayer call; it is not left in PlayerSettings after the build.
                extraScriptingDefines = new[] { BuildClientDefine, ServerBuildDefine }
            };

            try {
                reportProgress?.Invoke(0.08f, "Building Linux server");
                return BuildPipeline.BuildPlayer(options);
            } finally {
                if (restoreClientDefine) {
                    AddDefine(NamedBuildTarget.Server, ClientDefine);
                    LastBuildRestoredClientDefine = true;
                }
            }
        }

        static bool HasDefine(NamedBuildTarget target, string define) {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            return defines.Split(';').Contains(define);
        }

        static void AddDefine(NamedBuildTarget target, string define) {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);

            if (defines.Split(';').Contains(define)) {
                return;
            }

            string newDefines = string.IsNullOrWhiteSpace(defines)
                ? define
                : $"{defines};{define}";

            PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
        }

        static void RemoveDefine(NamedBuildTarget target, string define) {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);

            string newDefines = string.Join(";",
                defines
                    .Split(';')
                    .Where(x => x != define)
            );

            PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
        }
    }
}
