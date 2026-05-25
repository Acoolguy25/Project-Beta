// Assets/RyanAssets/Editor/BuildServer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using RyanAssets.NetworkService;
using UnityDebug = UnityEngine.Debug;
using UnityEditor.Build;

namespace RyanAssets.Editor
{
    public static class BuildServer
    {
        const float ServerBuildProgress = 0.7f;
        static BuildTask currentTask;

        public static void RemoveDefine(NamedBuildTarget target, string define)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);

            string newDefines = string.Join(";",
                defines
                    .Split(';')
                    .Where(x => x != define)
            );

            PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
        }

        public static void AddDefine(NamedBuildTarget target, string define)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);

            if (defines.Split(';').Contains(define))
                return;

            string newDefines = string.IsNullOrWhiteSpace(defines)
                ? define
                : $"{defines};{define}";

            PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
        }

        [MenuItem("Build/Linux Server")]
        public static void BuildLinuxServer()
        {
            if (!TryStartTask("Build/Linux Server", out BuildTask task))
            {
                return;
            }

            task.Report(0.02f, "Preparing Linux server build");
            
            RemoveDefine(NamedBuildTarget.Server, "UNITY_CLIENT");

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

            task.Report(0.08f, "Building Linux server");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (task.IsCancellationRequested)
            {
                task.Finish(Progress.Status.Canceled, "Canceled after build");
                ClearCurrentTask(task);
                return;
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                UnityDebug.LogError("Linux server build failed.");
                task.Finish(Progress.Status.Failed, "Linux server build failed");
                ClearCurrentTask(task);
                return;
            }

            task.Report(ServerBuildProgress, "Build complete, starting upload");
            RunUploadInBackground(task);
        }

        static void RunUploadInBackground(BuildTask task)
        {
            UnityDebug.Log("Linux server upload started in background.");

            _ = Task.Run(() =>
            {
                try
                {
                    string keyPath = Environment.ExpandEnvironmentVariables(
                        @"%USERPROFILE%\.ssh\id_hetzner"
                    );

                    task.Report(0.72f, "Uploading Linux server files with scp compression");
                    RunCommand(
                        "scp",
                        $"-C -i \"{keyPath}\" -r \"Builds/LinuxServer\" root@{NetworkSettings.DEPLOY_SERVER_IP}:/root/UnityBackend",
                        task,
                        0.72f,
                        0.93f,
                        "Uploading Linux server files with scp compression"
                    );

                    task.ThrowIfCancellationRequested();
                    task.Report(0.95f, "Setting server executable permission");
                    RunCommand(
                        "ssh",
                        $"-i \"{keyPath}\" root@{NetworkSettings.DEPLOY_SERVER_IP} chmod +x /root/UnityBackend/LinuxServer/GameServer.x86_64",
                        task,
                        0.95f,
                        0.99f,
                        "Setting server executable permission"
                    );

                    task.Finish(Progress.Status.Succeeded, "Linux server uploaded");
                    LogOnMainThread("Linux server uploaded successfully.");
                }
                catch (OperationCanceledException)
                {
                    task.Finish(Progress.Status.Canceled, "Linux server upload canceled");
                    LogOnMainThread("Linux server upload canceled.");
                }
                catch (Exception e)
                {
                    task.Finish(Progress.Status.Failed, "Linux server upload failed");
                    LogErrorOnMainThread($"Linux server upload failed:\n{e.Message}");
                }
                finally
                {
                    ClearCurrentTask(task);
                }
            });
        }

        [MenuItem("Build/Windows Client")]
        public static void BuildWindowsClient()
        {
            if (!TryStartTask("Build/Windows Client", out BuildTask task))
            {
                return;
            }

            task.Report(0.02f, "Preparing Windows client build");

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

            task.Report(0.08f, "Building Windows client");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (task.IsCancellationRequested)
            {
                task.Finish(Progress.Status.Canceled, "Canceled after build");
            }
            else if (report.summary.result == BuildResult.Succeeded)
            {
                task.Finish(Progress.Status.Succeeded, "Windows client build complete");
            }
            else
            {
                UnityDebug.LogError("Windows client build failed.");
                task.Finish(Progress.Status.Failed, "Windows client build failed");
            }

            ClearCurrentTask(task);
        }

        static bool TryStartTask(string name, out BuildTask task)
        {
            if (currentTask != null)
            {
                UnityDebug.LogWarning("A build task is already running.");
                task = null;
                return false;
            }

            task = new BuildTask(name);
            currentTask = task;
            return true;
        }

        static void ClearCurrentTask(BuildTask task)
        {
            if (currentTask == task)
            {
                currentTask = null;
            }
        }

        static void RunCommand(string fileName, string args, BuildTask task, float startProgress, float endProgress, string phase)
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(psi);
            try
            {
                Stopwatch commandStopwatch = Stopwatch.StartNew();
                task.SetActiveProcess(process);

                while (!process.HasExited)
                {
                    task.ThrowIfCancellationRequested();
                    float processProgress = startProgress + ((endProgress - startProgress) * EstimateCommandProgress(commandStopwatch));
                    task.Report(processProgress, phase);
                    process.WaitForExit(250);
                }

                task.ThrowIfCancellationRequested();
                string stderr = process.StandardError.ReadToEnd();

                task.Report(endProgress, phase);

                if (process.ExitCode != 0)
                {
                    throw new Exception(
                        $"{fileName} failed with exit code {process.ExitCode}\n{stderr}"
                    );
                }
            }
            finally
            {
                task.ClearActiveProcess(process);
            }
        }

        static float EstimateCommandProgress(Stopwatch stopwatch)
        {
            return Math.Min(0.95f, (float)(stopwatch.Elapsed.TotalSeconds / 300d));
        }

        static void LogOnMainThread(string message)
        {
            EditorApplication.delayCall += () =>
            {
                UnityDebug.Log(message);
            };
        }

        static void LogErrorOnMainThread(string message)
        {
            EditorApplication.delayCall += () =>
            {
                UnityDebug.LogError(message);
            };
        }

        sealed class BuildTask
        {
            readonly object sync = new();
            readonly int progressId;
            readonly int mainThreadId;
            readonly Stopwatch stopwatch = Stopwatch.StartNew();
            readonly List<Action> mainThreadActions = new();
            volatile bool cancelRequested;
            Process activeProcess;

            public BuildTask(string name)
            {
                mainThreadId = Thread.CurrentThread.ManagedThreadId;
                progressId = Progress.Start(name, "0% - Preparing - 00:00");
                Progress.RegisterCancelCallback(progressId, RequestCancel);
            }

            public bool IsCancellationRequested => cancelRequested;

            public void Report(float progress, string phase)
            {
                float clampedProgress = Math.Max(0f, Math.Min(0.99f, progress));
                string description = FormatDescription(clampedProgress, phase);
                RunOnMainThread(() => Progress.Report(progressId, clampedProgress, description));
            }

            public void Finish(Progress.Status status, string phase)
            {
                float progress = status == Progress.Status.Succeeded ? 1f : Math.Min(0.99f, Math.Max(0f, ElapsedProgressGuess()));
                string description = FormatDescription(progress, phase);

                RunOnMainThread(() =>
                {
                    Progress.Report(progressId, progress, description);
                    Progress.Finish(progressId, status);
                });
            }

            public void ThrowIfCancellationRequested()
            {
                if (cancelRequested)
                {
                    throw new OperationCanceledException();
                }
            }

            public void SetActiveProcess(Process process)
            {
                lock (sync)
                {
                    activeProcess = process;
                    if (cancelRequested)
                    {
                        KillActiveProcess();
                    }
                }
            }

            public void ClearActiveProcess(Process process)
            {
                lock (sync)
                {
                    if (activeProcess == process)
                    {
                        activeProcess = null;
                    }
                }
            }

            bool RequestCancel()
            {
                cancelRequested = true;
                Report(ElapsedProgressGuess(), "Cancel requested");

                lock (sync)
                {
                    KillActiveProcess();
                }

                return true;
            }

            void KillActiveProcess()
            {
                if (activeProcess == null || activeProcess.HasExited)
                {
                    return;
                }

                try
                {
                    activeProcess.Kill();
                }
                catch (Exception e)
                {
                    UnityDebug.LogWarning($"Failed to stop active build process: {e.Message}");
                }
            }

            float ElapsedProgressGuess()
            {
                return (float)Math.Min(0.99d, stopwatch.Elapsed.TotalSeconds / 60d);
            }

            string FormatDescription(float progress, string phase)
            {
                int percent = (int)Math.Round(progress * 100f);
                return $"{percent}% - {phase} - {FormatElapsed()}";
            }

            string FormatElapsed()
            {
                TimeSpan elapsed = stopwatch.Elapsed;

                if (elapsed.TotalHours >= 1d)
                {
                    return elapsed.ToString(@"h\:mm\:ss");
                }

                return elapsed.ToString(@"mm\:ss");
            }

            void RunOnMainThread(Action action)
            {
                if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
                {
                    action();
                    return;
                }

                lock (mainThreadActions)
                {
                    mainThreadActions.Add(action);
                    if (mainThreadActions.Count > 1)
                    {
                        return;
                    }
                }

                EditorApplication.delayCall += FlushMainThreadActions;
            }

            void FlushMainThreadActions()
            {
                Action[] actions;

                lock (mainThreadActions)
                {
                    actions = mainThreadActions.ToArray();
                    mainThreadActions.Clear();
                }

                foreach (Action action in actions)
                {
                    action();
                }
            }
        }
    }
}
