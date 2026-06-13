using System;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

using FishNet;
using FishNet.Transporting;
using RyanAssets.NetworkService;
using RyanAssets.Shared.Broadcasts;
using FishNet.Managing;
using FishNet.Managing.Scened;
using Newtonsoft.Json;
using System.Data;
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
        public static event Func<Task> StopServerAsyncEvent;
        public static ServerInfo serverInfo = new();
        static bool isStopping;
        public static ushort MaxPlayers { get; private set; }
        public static ushort ServerIdleTimeoutSeconds { get; private set; }
        public static ushort ServerHeartbeatIntvSeconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ConfigureStackTraces() {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.Full);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad() {
            #if UNITY_EDITOR
                EditorApplication.isPlaying = false;
            #endif
            Debug.Log("============ ServerBootStrap ============");
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

            ValidateStartupArguments();
            BackendNetwork.default_body = JsonConvert.SerializeObject(serverInfo);
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
#if DEVELOPMENT_BUILD
                Debug.Log("Hosting at 0.0.0.0");
                transport.SetServerBindAddress("0.0.0.0", IPAddressType.IPv4);
#else
            Debug.Log($"Hosting at {NetworkSettings.activeConfig.backend_server_ip}");
            transport.SetServerBindAddress(NetworkSettings.activeConfig.backend_server_ip, IPAddressType.IPv4);
#endif
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
            StartServerEvent?.Invoke();
        }
        static void OnServerConnectionState(ServerConnectionStateArgs args) {
            if (args.ConnectionState == LocalConnectionState.Started) {
                StartServer();
            }
            // else if (args.ConnectionState == LocalConnectionState.Stopping){
            //     StopServer();
            // }
        }
        static bool ValidateStartupArguments() {
            if (string.IsNullOrWhiteSpace(serverInfo.universe_id)) {
                Debug.LogError("Server startup failed: missing -universe_id=<id> argument.");
                Application.Quit(64);
                return false;
            }

            if (string.IsNullOrWhiteSpace(serverInfo.server_id)) {
                Debug.LogError("Server startup failed: missing -server_id=<id> argument.");
                Application.Quit(64);
                return false;
            }

            if (serverInfo.server_port == 0) {
                Debug.LogError("Server startup failed: missing or invalid -server_port=<port> argument.");
                Application.Quit(64);
                return false;
            }

            if (MaxPlayers == 0) {
                Debug.LogError("Server startup failed: missing or invalid -max_players=<count> argument.");
                Application.Quit(64);
                return false;
            }

            return true;
        }
        public static void StopServer(string reason) {
            _ = StopServerAsync(reason);
        }

        static async Task StopServerAsync(string reason) {
            if (isStopping || !InstanceFinder.IsServerStarted)
                return;
            isStopping = true;

            InstanceFinder.ServerManager.Broadcast(new PromptBroadcast {
                title = "Server Closed",
                description = $"The server was closed because {reason}"
            });

            Debug.Log($"Stopping server because {reason}");
            StopServerEvent?.Invoke();
            await InvokeStopServerAsyncEvent();


            InstanceFinder.ServerManager.StopConnection(true);
        }
        static void OnQuit() {
            StopServer("the backend server experienced a shutdown");
        }

        static async Task InvokeStopServerAsyncEvent() {
            if (StopServerAsyncEvent == null)
                return;

            Delegate[] handlers = StopServerAsyncEvent.GetInvocationList();
            Task[] tasks = new Task[handlers.Length];

            for (int i = 0; i < handlers.Length; i++)
                tasks[i] = ((Func<Task>)handlers[i]).Invoke();

            await Task.WhenAll(tasks);
        }
    }
}
