using System;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

using FishNet;
using FishNet.Transporting;
using RyanAssets.NetworkService;
using RyanAssets.Shared.Declarations;
using FishNet.Managing;
using FishNet.Managing.Scened;
using Newtonsoft.Json;
using System.Data;
using FishNet.Connection;
using RyanAssets.Authentication;
using System.Threading;
using RyanAssets.Core;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RyanAssets.Server.ServerCore {
    public class ServerBootStrap {
        public class ServerInfo {
            public string universe_id { get; set; }
            public string server_id { get; set; }
            public ushort server_port { get; set; }
            public JObject ToJObject() {
                return JObject.FromObject(this);
            }
        };
        public static Action StartServerEvent, StopServerEvent;
        public static event Func<UniTask> StopServerAsyncEvent;
        public static ServerInfo serverInfo = new();
        static bool isStopping;
        public static ushort MaxPlayers { get; private set; }
        public static ushort ServerIdleTimeoutSeconds { get; private set; }
        public static ushort ServerHeartbeatIntvSeconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ConfigureStackTraces() {
#if !UNITY_EDITOR
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.Full);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad() {
            Debug.Log("============ ServerBootStrap ============");
#if UNITY_EDITOR
            serverInfo = new(){
                universe_id = "murder_mystery",
                server_id = "unity-test-server",
                server_port = 20000
            };
            MaxPlayers = 10;
            ServerIdleTimeoutSeconds = ushort.MaxValue;
            ServerHeartbeatIntvSeconds = ushort.MaxValue;
#else
            foreach (string arg in Environment.GetCommandLineArgs()) {
                string[] split = arg.Split('=', 2);

                if (split.Length != 2)
                    continue;

                switch (split[0]) {
                    case "-universe_id":
                        serverInfo.universe_id = split[1];
                        Debug.Log($"Universe ID: {serverInfo.universe_id}");
                        break;

                    case "-server_id":
                        serverInfo.server_id = split[1];
                        Debug.Log($"Server ID: {serverInfo.server_id}");
                        break;

                    case "-server_port":
                        if (!ushort.TryParse(split[1], out ushort serverPort)) {
                            Debug.LogError($"Invalid server port: {split[1]}");
                            break;
                        }
                        serverInfo.server_port = serverPort;
                        Debug.Log($"Server Port: {serverInfo.server_port}");
                        break;

                    case "-max_players":
                        if (!ushort.TryParse(split[1], out ushort maxPlayers)) {
                            Debug.LogError($"Invalid max players: {split[1]}");
                            break;
                        }
                        MaxPlayers = maxPlayers;
                        break;
                    case "-server_idle_timeout":
                        ushort.TryParse(split[1], out ushort idleTimeout);
                        ServerIdleTimeoutSeconds = idleTimeout;
                        break;
                    case "-heartbeat_interval":
                        ushort.TryParse(split[1], out ushort intvl);
                        ServerHeartbeatIntvSeconds = intvl;
                        break;
                    case "-server_folder":
                        // case "-backend_local":
                        break;

                    default:
                        Debug.LogWarning($"Unable to find {split[0]}");
                        break;
                }
            }
#endif
            // BackendNetwork.default_body = JsonConvert.SerializeObject(serverInfo);
            BackendNetwork.SetServerHeader("universe-id", serverInfo.universe_id);
            BackendNetwork.SetServerHeader("server-id", serverInfo.server_id);
            BackendNetwork.SetServerHeader("server-port", serverInfo.server_port.ToString());

            BackendSocket.SetServerHeader("universe-id", serverInfo.universe_id);
            BackendSocket.SetServerHeader("server-id", serverInfo.server_id);
            BackendSocket.SetServerHeader("server-port", serverInfo.server_port.ToString());
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad() {
            if (InstanceFinder.NetworkManager == null || InstanceFinder.TransportManager == null) {
                Debug.LogError("Server startup failed: FishNet NetworkManager or TransportManager is not available in the loaded scene.");
                Application.Quit(65);
                return;
            }

            Transport transport = InstanceFinder.TransportManager.Transport;
            if (transport == null) {
                Debug.LogError("Server startup failed: FishNet transport is not assigned.");
                Application.Quit(65);
                return;
            }

            transport.SetMaximumClients(MaxPlayers);
            //#if DEVELOPMENT_BUILD
            Debug.Log("Hosting at 0.0.0.0");
            transport.SetServerBindAddress("0.0.0.0", IPAddressType.IPv4);
            //#else
            //            Debug.Log($"Hosting at {NetworkSettings.activeConfig.backend_server_ip}");
            //            transport.SetServerBindAddress(NetworkSettings.activeConfig.backend_server_ip.Replace($":{serverInfo.server_port}", ""), IPAddressType.IPv4);
            //#endif
            transport.SetPort(serverInfo.server_port);
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;

            bool start_status = InstanceFinder.ServerManager.StartConnection();
            Debug.Assert(start_status, "ServerManager Failed To Start!");
            Application.quitting += OnQuit;
        }
        static void StartServer() {
            SceneLoadData sceneLoadData = new(serverInfo.universe_id + "_start") {
                ReplaceScenes = ReplaceOption.All
            };
            InstanceFinder.NetworkManager.SceneManager.LoadGlobalScenes(sceneLoadData);
            UnityEngine.SceneManagement.SceneManager.LoadScene(serverInfo.universe_id + "_server", LoadSceneMode.Additive);
            StartServerEvent?.Invoke();
        }
        static void OnServerConnectionState(ServerConnectionStateArgs args) {
            if (args.ConnectionState == LocalConnectionState.Started) {
                StartServer();
            }
        }
        public static void StopServer(string reason) {
            StopServerAsync(reason).Forget();
        }

        static async UniTask StopServerAsync(string reason) {
            if (isStopping || !InstanceFinder.IsServerStarted)
                return;
            isStopping = true;
            UnityTokenAuthenticator.IsShuttingDown = true; // prevent future connections

            InstanceFinder.ServerManager.Broadcast(new PromptBroadcast {
                title = "Server Closed",
                description = $"The server was closed because {reason}"
            });

            Debug.Log($"Stopping server because {reason}");
            StopServerEvent?.Invoke();
            bool passed = await TaskHelper.AwaitTaskTimeout(UniTask.WhenAll(WaitForNetworkFlush(2), InvokeStopServerAsyncEvent()));
            if (!passed)
            {
                Debug.LogError("StopServerAsyncEvent did not complete in time, proceeding with shutdown.");
            }

            foreach (NetworkConnection conn in InstanceFinder.ServerManager.Clients.Values) {
                conn.Disconnect(false);
            }

            InstanceFinder.ServerManager.StopConnection(true);
            Application.Quit(0);
        }
        private static UniTask WaitForNetworkFlush(int postTicks = 3)
        {
            UniTaskCompletionSource<bool> tcs = new UniTaskCompletionSource<bool>();
            int remaining = postTicks;

            void OnPostTick()
            {
                if (--remaining > 0) return;

                InstanceFinder.TimeManager.OnPostTick -= OnPostTick;

                // Wait for the next pre-tick to confirm we're at a clean cycle boundary
                void OnPreTick()
                {
                    InstanceFinder.TimeManager.OnPreTick -= OnPreTick;
                    tcs.TrySetResult(true);
                }

                InstanceFinder.TimeManager.OnPreTick += OnPreTick;
            }

            InstanceFinder.TimeManager.OnPostTick += OnPostTick;
            return tcs.Task;
        }
        static void OnQuit() {
            StopServer("the backend server experienced a shutdown");
        }

        static async UniTask InvokeStopServerAsyncEvent() {
            if (StopServerAsyncEvent == null)
                return;

            Delegate[] handlers = StopServerAsyncEvent.GetInvocationList();
            UniTask[] tasks = new UniTask[handlers.Length];

            for (int i = 0; i < handlers.Length; i++)
                tasks[i] = ((Func<UniTask>)handlers[i]).Invoke();

            await UniTask.WhenAll(tasks);
        }
    }
}
