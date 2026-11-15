using System;
using System.Diagnostics;
using System.IO;
using Cysharp.Threading.Tasks;
using RyanAssets.NetworkService;
using UnityEditor;
using UnityDebug = UnityEngine.Debug;
using EditorProgress = UnityEditor.Progress;

namespace RyanAssets.Editor
{
    public static class RestartRemoteServer
    {
        const string RemoteDirectory = "/root/UnityBackend";
        const string SessionName = "unity";
        static bool isRestarting;

        [MenuItem("Build/Restart Server")]
        public static void RestartServer()
        {
            if (isRestarting)
            {
                UnityDebug.LogWarning("A server restart is already running.");
                return;
            }

            isRestarting = true;
            int progressId = EditorProgress.Start("Build/Restart Server", "Connecting to backend server");

            UniTask.Create(async () =>
            {
                await UniTask.SwitchToThreadPool();
                string error = null;
                try
                {
                    RunRestartCommand();
                }
                catch (Exception e)
                {
                    error = e.Message;
                }

                EditorApplication.delayCall += () =>
                {
                    isRestarting = false;
                    if (error == null)
                    {
                        EditorProgress.Finish(progressId, EditorProgress.Status.Succeeded);
                        UnityDebug.Log($"Backend server restarted in tmux session '{SessionName}'.");
                    }
                    else
                    {
                        EditorProgress.Finish(progressId, EditorProgress.Status.Failed);
                        UnityDebug.LogError($"Server restart failed: {error}");
                    }
                };
            }).Forget();
        }

        static void RunRestartCommand()
        {
            string keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh", "id_hetzner"
            );
            string identityOption = File.Exists(keyPath) ? $"-i \"{keyPath}\" " : string.Empty;
            const string remoteCommand =
                "cd " + RemoteDirectory + " && " +
                "test -f .venv/bin/activate && test -f ./run.sh && " +
                "(tmux kill-session -t " + SessionName + " 2>/dev/null || true) && " +
                "tmux new-session -d -s " + SessionName +
                " 'cd " + RemoteDirectory + " && . .venv/bin/activate && exec ./run.sh' && " +
                "sleep 1 && tmux has-session -t " + SessionName;

            ProcessStartInfo startInfo = new()
            {
                FileName = "ssh",
                Arguments = $"-o BatchMode=yes -o ConnectTimeout=10 {identityOption}root@{NetworkSettings.DEPLOY_SERVER_IP} \"{remoteCommand}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start SSH.");
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException("SSH did not complete within 30 seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"SSH exited with code {process.ExitCode}: {process.StandardError.ReadToEnd()}"
                );
            }
        }
    }
}
