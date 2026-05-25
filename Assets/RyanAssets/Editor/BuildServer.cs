// Assets/RyanAssets/Editor/BuildServer.cs
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using RyanAssets.NetworkService;
using System.Diagnostics;
using UnityDebug = UnityEngine.Debug;

namespace RyanAssets.Editor {
    public static class BuildServer
    {
        [MenuItem("Build/Linux Server")]
        public static void BuildLinuxServer()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Select(x => x.path)
                .ToArray();

            scenes[0] = "Assets/Scenes/ServerInit.unity";

            BuildPlayerOptions options = new()
            {
                scenes = scenes,
                locationPathName = "Builds/LinuxServer/GameServer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                UnityDebug.LogError("Linux server build failed.");
                return;
            }

            string keyPath = System.Environment.ExpandEnvironmentVariables(
                @"%USERPROFILE%\.ssh\id_hetzner"
            );

            string keyPathUnix = keyPath.Replace("\\", "/");

            if (!RunCommand(
                @"E:\msys64\usr\bin\bash.exe",
                $"-lc \"rsync -az --delete " +
                $"-e '/usr/bin/ssh -i {keyPathUnix} -o BatchMode=yes -o StrictHostKeyChecking=accept-new' " +
                $"Builds/LinuxServer/ root@{NetworkSettings.DEPLOY_SERVER_IP}:/root/UnityBackend/LinuxServer/\""
            ))
            {
                return;
            }

            RunCommand(
                "ssh",
                $"-i \"{keyPath}\" root@{NetworkSettings.DEPLOY_SERVER_IP} chmod +x /root/UnityBackend/LinuxServer/GameServer.x86_64"
            );
        }

        [MenuItem("Build/Windows Client")]
        public static void BuildWindowsClient()
        {
            BuildPlayerOptions options = new()
            {
                scenes = EditorBuildSettings.scenes
                    .Select(x => x.path)
                    .ToArray(),

                locationPathName = "Builds/WindowsClient/Project Beta.exe",
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None
            };

            BuildPipeline.BuildPlayer(options);
        }

        static bool RunCommand(string fileName, string args)
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(psi);

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                UnityDebug.LogError(
                    $"{fileName} failed with exit code {process.ExitCode}\n{stderr}"
                );

                return false;
            }

            if (!string.IsNullOrWhiteSpace(stdout))
                UnityDebug.Log(stdout);

            return true;
        }
    }
}