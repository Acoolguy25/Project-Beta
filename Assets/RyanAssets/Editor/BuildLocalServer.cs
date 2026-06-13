using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;
using UnityEditor.Build.Profile;
using System.Collections.Generic;

namespace RyanAssets.Editor {
    public static class BuildLocalServer {
        const string ServerBuildDefine = "SERVER_BUILD";
        const string ServerInitScene = "Assets/Scenes/ServerInit.unity";
        static readonly string[] RemoveScenes = {"Assets/Scenes/MainMenu.unity"};
        const string ServerExecutableName = "GameServer.x86_64";

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

        [MenuItem("Build/Run Linux Server")]
        public static void RunLinuxServer(){
            BuildReport report = BuildLinuxServer(null);

            if (report.summary.result == BuildResult.Succeeded) {
                UnityDebug.Log($"Linux server build complete");
                BuildProfile active = BuildProfile.GetActiveBuildProfile();
                if (active.name.Contains("Server")){
                    BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(
                        "Assets/Settings/Build Profiles/Windows.asset"
                    );
                    BuildProfile.SetActiveBuildProfile(profile);
                }
            }else {
                UnityDebug.LogError("Linux server build failed.");
            }
        }

        public static BuildReport BuildLinuxServer(Action<float, string> reportProgress) {
            reportProgress?.Invoke(0.02f, "Preparing Linux server build");

            Directory.CreateDirectory(LinuxServerDirectory);

            List<string> scenes = new(EditorBuildSettings.scenes
                .Select(x => x.path));

            scenes.Add(ServerInitScene);
            foreach (string scene in RemoveScenes)
                scenes.Remove(scene); // remove all scenes in list

            BuildPlayerOptions options = new() {
                scenes = scenes.ToArray(),
                locationPathName = LinuxServerExecutablePath,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
                // Scoped to this BuildPlayer call; it is not left in PlayerSettings after the build.
                extraScriptingDefines = new[] { ServerBuildDefine }
            };

            reportProgress?.Invoke(0.08f, "Building Linux server");
            return BuildPipeline.BuildPlayer(options);
        }
    }
}
