#if UNITY_SERVER
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;
using UnityEditor.Build.Profile;

namespace RyanAssets.Editor {
    public static class BuildLocalServer {
        const string LinuxServerBuildProfilePath = "Assets/Settings/Build Profiles/Linux Server.asset";
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
        public class ApplicationData {
            public string ApplicationVersion;
        }
        public static void WriteBuildFiles() {
            string buildDirectory = LinuxServerDirectory;
            string versionFile = Path.Combine(buildDirectory, "application.json");

            File.WriteAllText(
                versionFile,
                JsonUtility.ToJson(new ApplicationData
                {
                    ApplicationVersion = PlayerSettings.bundleVersion
                }, true)
            );

        }

        //[MenuItem("Build/Local Linux Server")]
        public static void BuildLinuxServer() {
            BuildReport report = BuildLinuxServer(null);

            if (report.summary.result == BuildResult.Succeeded) {
                UnityDebug.Log($"Linux server build complete: {LinuxServerExecutablePath}");
            } else {
                UnityDebug.LogError("Linux server build failed.");
            }
        }

        //[MenuItem("Build/Run Linux Server")]
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

            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(LinuxServerBuildProfilePath);

            if (profile == null) {
                throw new FileNotFoundException("Linux server build profile not found.", LinuxServerBuildProfilePath);
            }

            BuildPlayerWithProfileOptions options = new() {
                buildProfile = profile,
                locationPathName = LinuxServerExecutablePath,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            reportProgress?.Invoke(0.08f, "Building Linux server");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            // build first, then write build files so that it doesn't get deleted
            WriteBuildFiles();
            return report;
        }
    }
}

#endif