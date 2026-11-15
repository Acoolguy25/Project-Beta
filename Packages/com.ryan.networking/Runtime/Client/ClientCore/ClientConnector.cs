using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using RyanAssets.Client.ClientModules;
using RyanAssets.Input;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace RyanAssets.Client.ClientCore {
    public class ClientConnector : MonoBehaviour {
        public static ClientConnector Instance;
        public static Action OnConnected, OnDisconnected;
        public static bool IsConnected, IsLoadingScenes;
        [SerializeField]
        GameObject[] gameOnlyObjects;
        public static bool wasAuthenticated, isConnecting, hasCanceled;
        public static string joinServerId, joinUniverseId;
        FishNet.Managing.Client.ClientManager clientManager;
        FishNet.Managing.Scened.SceneManager networkSceneManager;
        void Start() {
            if (NetworkSettings.EditorDirectConnection)
                JoinLocalEditorServer();
        }

        public void JoinLocalEditorServer() {
            if (!NetworkSettings.EditorDirectConnection || isConnecting || IsConnected)
                return;
            joinServerId = "local-editor";
            ConnectToServer(NetworkSettings.EditorUniverseId, "127.0.0.1", NetworkSettings.EditorGamePort);
        }
        void OnEnable() {
            Instance = this;
            clientManager = InstanceFinder.ClientManager;
            networkSceneManager = InstanceFinder.SceneManager;
            clientManager.OnClientConnectionState += OnClientState;
            clientManager.OnClientTimeOut += OnClientTimeOut;
            clientManager.OnAuthenticated += OnClientAuthenticated;
            networkSceneManager.OnLoadStart += OnSceneLoadStart;
            networkSceneManager.OnLoadEnd += OnSceneLoadEnd;
            SetGameActive(false);
        }
        void SetGameActive(bool active) {
            // if (!active){
            //     InputService.ResetAction(InputControl.Character);
            //     InputService.ResetAction(InputControl.Client);
            // }
            foreach (GameObject gameObj in gameOnlyObjects) {
                gameObj.SetActive(active);
            }
        }
        void SetJoiningMessage(string reason, string title = "Joining") {
            PromptManager.PromptDelete(PromptId.JoinGameAwait);
            if (reason != null)
                PromptManager.PromptCancelableWait(title + " Server", reason, PromptId.JoinGameAwait).ContinueWith(button => {
                    if (button == PromptButton.Cancel)
                        CancelJoinGameServer();
                }).Forget();
        }
        void SetJoinResult(string reason, string title = "Join Failed") {
            SetJoiningMessage(null);
            PromptManager.PromptDelete(PromptId.JoinGameResponse);
            if (reason != null)
                PromptManager.PromptOk(title, reason, PromptId.JoinGameResponse);
        }
        async UniTask<(string, JObject)> WaitForServerLoad() {
            return await BackendNetwork.GetRequest($"/api/servers/v1/{joinServerId}/wait");
        }
        public async void JoinGameServer(string universe_id, JObject json) {
            // await StopActiveClientConnection();

            string status = json["data"]["status"].ToString();
            joinServerId = json["data"]["server_id"].ToString();
            if (status == "starting") {
                (string response, JObject obj) = await BackendClient.RequestAsync(WaitForServerLoad, "Waiting For Server", promptWaiting: PromptId.PlayGameAwait, promptResult: PromptId.PlayGameConfirm, retryPolicy: RetryPolicy.RetryOrCancel, desc: "Server Is Starting Up, Please Wait");
                if (response != null)
                    return;
            } else if (status != "ready") {
                SetJoinResult($"Unknown Join Status: {status}");
                return;
            }
            ConnectToServer(universe_id, (string)json["data"]["server_ip"], (ushort)json["data"]["server_port"]);
        }
        void ConnectToServer(string universe_id, string address, ushort port) {
            SetJoiningMessage("Initializing...");
            joinUniverseId = universe_id;
            Transport transport = InstanceFinder.TransportManager.Transport;
            transport.SetClientAddress(address);
            transport.SetPort(port);
            isConnecting = true;
            hasCanceled = false;
            Debug.Log($"Connecting To {transport.GetClientAddress()}:{transport.GetPort()}");
            bool connectionStatus = InstanceFinder.ClientManager.StartConnection();
            if (!connectionStatus) {
                isConnecting = false;
                SetJoinResult("Initialization Failed");
            } else
                SetJoiningMessage("Connecting To Game Server...");
        }
        public void CancelJoinGameServer() {
            if (isConnecting) {
                hasCanceled = true;
                InstanceFinder.ClientManager.StopConnection();
            }
        }
        private void OnClientTimeOut() {
            SetJoinResult("Client Timed Out");
        }
        private void OnClientState(ClientConnectionStateArgs args) {
            switch (args.ConnectionState) {
                case LocalConnectionState.Starting:
                    SetJoiningMessage("Connecting...");
                    break;

                case LocalConnectionState.Started:
                    SetJoiningMessage("Authenticating...");
                    break;

                case LocalConnectionState.Stopping:
                    // SetJoiningMessage("Disconnecting from server...", "Disconnecting");
                    break;

                case LocalConnectionState.Stopped:
                    isConnecting = false;
                    OnDisconnected?.Invoke();
                    IsConnected = false;
                    SetJoinResult(null); // remove any other prompts!
                    if (!PromptManager.PromptDelete(PromptId.LeaveGameAwait) && !hasCanceled) {
                        if (wasAuthenticated) {
                            wasAuthenticated = false;
                            SetJoinResult("You were unexpectedly disconnected from game server", "Disconnected");
                        } else if (!hasCanceled && !PromptManager.HasPrompt(PromptId.AuthenticationFail)) { // make sure not handled by UnityTokenAuthenticator
                            SetJoinResult("Join Game Failed!");
                        }
                    }
                    if (!UnityEngine.SceneManagement.SceneManager.GetSceneByName("MainMenu").isLoaded)
                        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
                    hasCanceled = false;
                    SetGameActive(false);
                    break;
            }
        }
        private void OnClientAuthenticated() {
            // SetJoinResult("Authenticated!", "Join Success");
            SetJoinResult(null);
            wasAuthenticated = true;
            SetGameActive(true);
            //var MainMenu = SceneManager.GetSceneByName("MainMenu");
            //if (MainMenu.isLoaded)
            //    SceneManager.UnloadSceneAsync(MainMenu);
            OnConnected?.Invoke();
            IsConnected = true;
        }
        void OnSceneLoadStart(SceneLoadStartEventArgs args) {
            IsLoadingScenes = true;
        }
        void OnSceneLoadEnd(SceneLoadEndEventArgs args) {
            IsLoadingScenes = false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            OnConnected = null;
            OnDisconnected = null;
            IsConnected = false;
            IsLoadingScenes = false;
            wasAuthenticated = isConnecting = hasCanceled = false;
            joinServerId = joinUniverseId = null;
            Instance = null;
        }
        void OnDisable() {
            // FishNet may already have removed its singleton while the persistent scene tears down.
            if (clientManager != null) {
                clientManager.OnClientConnectionState -= OnClientState;
                clientManager.OnClientTimeOut -= OnClientTimeOut;
                clientManager.OnAuthenticated -= OnClientAuthenticated;
            }
            if (networkSceneManager != null) {
                networkSceneManager.OnLoadStart -= OnSceneLoadStart;
                networkSceneManager.OnLoadEnd -= OnSceneLoadEnd;
            }
            if (Instance == this)
                Instance = null;
        }
    }
}
