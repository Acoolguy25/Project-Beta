// ItchBuildDeployer.cs
//
// Unity Editor utility that:
//   1. Builds using an existing "Windows Build" Build Profile asset (Unity 6+ Build Profiles).
//   2. Zips the build output.
//   3. Uploads the zip to itch.io using Butler, targeting the "Windows Build" channel.
//
// SETUP REQUIRED:
//   - Requires Unity 6000.0+ (Build Profiles API).
//   - Place this file in an "Editor" folder anywhere under Assets/ (e.g. Assets/Editor/).
//   - You must already have a Build Profile asset named "Windows Build" (File > Build Profiles).
//   - Install Butler (https://itch.io/docs/butler/installing.html) and run `butler login` once
//     from a terminal so credentials are cached locally.
//   - Update the constants in the "Configuration" region below (itch.io user/game slug, etc).
//
// USAGE:
//   Unity menu bar -> Build -> Build and Deploy to itch.io (Windows)
//
// This calls Butler's "push" command, which itch.io recommends over manually zipping/uploading
// through the web UI because Butler does binary diffing for faster, smaller updates.
// See: https://itch.io/docs/butler/pushing.html

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace EasyDebug.Client {
    public static class ItchBuildDeployer {
        #region Configuration

        // Your itch.io username (or org) and the game's URL slug, e.g. itch.io/<ITCH_USER>/<ITCH_GAME>
        private const string ItchUser = "acoolguy24";
        private const string ItchGame = "project-beta";

        // The itch.io channel name this build should be pushed to.
        private const string ItchChannel = "Windows Build";

        // Name of the Build Profile asset (as shown in File > Build Profiles) to build from.
        // This asset must already exist in the project.
        private const string BuildProfileName = "Windows";

        // Name of the main executable Unity will produce.
        private const string ProductExeName = "ProjectBeta.exe";

        // Where the raw build gets placed before zipping (relative to project root).
        private const string BuildFolder = "Builds/WindowsBuild";

        // Where the final zip is written (relative to project root).
        private const string ZipOutputPath = "Builds/WindowsBuild.zip";

        // Path to butler(.exe). If Butler is on your system PATH, "butler" alone is enough.
        // On Windows you can also point this at a full path, e.g. @"C:\tools\butler\butler.exe".
        private const string ButlerPath = "butler";

        // How often to print a "still working" heartbeat while butler is running (seconds).
        private const int HeartbeatIntervalSeconds = 5;

        #endregion

        // NOTE: this is deliberately "async void". Unity's Editor pumps a SynchronizationContext
        // that lets async continuations resume on the main thread, which is what lets us safely
        // call EditorUtility/Debug APIs after each "await" below. BuildFromProfile/CreateZip still
        // run synchronously on the main thread (Unity's build APIs require that), but the Butler
        // push itself is awaited so it no longer blocks the Editor while it runs.
        [MenuItem("Build/Build and Deploy to itch.io (Windows)")]
        public static async void BuildAndDeploy() {
            try {
                string exePath = BuildFromProfile();
                string zipPath = CreateZip(exePath);
                await PushToItch(zipPath);

                UnityEngine.Debug.Log("[ItchBuildDeployer] Done. Build deployed to itch.io channel: " + ItchChannel);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("itch.io Deploy", "Build uploaded successfully to channel: " + ItchChannel, "OK");
            } catch (Exception e) {
                EditorUtility.ClearProgressBar();
                UnityEngine.Debug.LogError("[ItchBuildDeployer] Failed: " + e.Message);
                EditorUtility.DisplayDialog("itch.io Deploy Failed", e.Message, "OK");
            }
        }

        private static string BuildFromProfile() {
            BuildProfile profile = FindBuildProfileByName(BuildProfileName);
            if (profile == null) {
                throw new Exception($"No Build Profile named \"{BuildProfileName}\" was found. " +
                                     "Create one via File > Build Profiles, or update BuildProfileName.");
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string buildDir = Path.Combine(projectRoot, BuildFolder);

            if (Directory.Exists(buildDir))
                Directory.Delete(buildDir, true);
            Directory.CreateDirectory(buildDir);

            string exePath = Path.Combine(buildDir, ProductExeName);

            var options = new BuildPlayerWithProfileOptions {
                buildProfile = profile,
                locationPathName = exePath,
                options = BuildOptions.None
            };

            UnityEngine.Debug.Log($"[ItchBuildDeployer] Building profile \"{BuildProfileName}\" -> {exePath}");
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) {
                throw new Exception($"Build failed with result: {report.summary.result} " +
                                     $"({report.summary.totalErrors} error(s))");
            }

            UnityEngine.Debug.Log("[ItchBuildDeployer] Build succeeded: " + report.summary.totalSize + " bytes");
            return exePath;
        }

        private static BuildProfile FindBuildProfileByName(string profileName) {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(BuildProfile)}");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile != null && profile.name == profileName)
                    return profile;
            }
            return null;
        }

        private static string CreateZip(string exePath) {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string buildDir = Path.GetDirectoryName(exePath);
            string zipPath = Path.Combine(projectRoot, ZipOutputPath);

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            UnityEngine.Debug.Log("[ItchBuildDeployer] Zipping " + buildDir + " -> " + zipPath);
            ZipFile.CreateFromDirectory(buildDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);

            long zipSizeMb = new FileInfo(zipPath).Length / (1024 * 1024);
            UnityEngine.Debug.Log($"[ItchBuildDeployer] Zip created: {zipSizeMb} MB");

            return zipPath;
        }

        private static async Task PushToItch(string zipPath) {
            string target = $"{ItchUser}/{ItchGame}:{ItchChannel}";

            // --json gives one line of structured progress per update instead of a redrawing
            // progress bar (which doesn't render sensibly once stdout is redirected).
            string arguments = $"push \"{zipPath}\" \"{target}\" --json";

            UnityEngine.Debug.Log($"[ItchBuildDeployer] Running: {ButlerPath} {arguments}");

            var psi = new ProcessStartInfo {
                FileName = ButlerPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var stopwatch = Stopwatch.StartNew();
            string lastLine = "starting...";

            using (var process = new Process { StartInfo = psi }) {
                process.OutputDataReceived += (s, e) => {
                    if (e.Data == null) return;
                    lastLine = e.Data;
                    UnityEngine.Debug.Log("[butler] " + e.Data);
                };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) UnityEngine.Debug.LogWarning("[butler] " + e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Heartbeat: since butler's real progress lines can be sparse (or all on stderr
                // depending on version), print elapsed time regularly so it's obvious the editor
                // hasn't frozen — check Task Manager for butler.exe CPU/network activity too.
                using (var heartbeat = new Timer(_ => {
                    UnityEngine.Debug.Log($"[ItchBuildDeployer] Still pushing... {stopwatch.Elapsed:mm\\:ss} elapsed. Last line: {lastLine}");
                }, null, TimeSpan.FromSeconds(HeartbeatIntervalSeconds), TimeSpan.FromSeconds(HeartbeatIntervalSeconds))) {
                    EditorUtility.DisplayProgressBar("Deploying to itch.io", "Pushing build via Butler...", 0f);

                    // CRITICAL: process.WaitForExit() is a blocking call. If we called it directly
                    // here, it would freeze Unity's main thread (and the console with it) for the
                    // entire push. Task.Run moves that blocking wait onto a background thread, and
                    // "await" hands control back to the Editor immediately, so the UI and console
                    // keep updating live while the push runs.
                    await Task.Run(() => process.WaitForExit());

                    EditorUtility.ClearProgressBar();
                }

                UnityEngine.Debug.Log($"[ItchBuildDeployer] Butler finished after {stopwatch.Elapsed:mm\\:ss}, exit code {process.ExitCode}");

                if (process.ExitCode != 0) {
                    throw new Exception($"Butler exited with code {process.ExitCode}. " +
                                         "Make sure Butler is installed, on PATH (or ButlerPath is correct), " +
                                         "and you've run 'butler login' at least once.");
                }
            }
        }
    }
}