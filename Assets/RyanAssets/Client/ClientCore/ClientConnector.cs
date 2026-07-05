using Newtonsoft.Json.Linq;
using UnityEngine;

using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using UnityEngine.SceneManagement;

using FishNet;
using FishNet.Transporting;
using Cysharp.Threading.Tasks;
using RyanAssets.Client.ClientModules;
using System;
using RyanAssets.Input;
namespace RyanAssets.Client.ClientCore {
    public class ClientConnector : MonoBehaviour {
        public static ClientConnector Instance;
        public static Action OnConnected, OnDisconnected;
        public static bool IsConnected;
        [SerializeField]
        GameObject[] gameOnlyObjects;
        static bool wasAuthenticated, isConnecting, hasCanceled;
        public static string joinServerId, joinUniverseId;
        void OnEnable() {
            Instance = this;
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientState;
            InstanceFinder.ClientManager.OnClientTimeOut += OnClientTimeOut;
            InstanceFinder.ClientManager.OnAuthenticated += OnClientAuthenticated;
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
            SetJoiningMessage("Initializing...");
            joinUniverseId = universe_id;
            Transport transport = InstanceFinder.TransportManager.Transport;
            transport.SetClientAddress((string)json["data"]["server_ip"]);
            transport.SetPort((ushort)json["data"]["server_port"]);
            Debug.Log($"Connecting To {transport.GetClientAddress()}:{transport.GetPort()}");
            bool connectionStatus = InstanceFinder.ClientManager.StartConnection();
            if (!connectionStatus)
                SetJoinResult("Initialization Failed");
            else
                SetJoiningMessage("Connecting To Game Server...");
            isConnecting = true;
            hasCanceled = false;
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
                    if (!PromptManager.PromptDelete(PromptId.LeaveGameAwait) && !hasCanceled){
                        if (wasAuthenticated) {
                            wasAuthenticated = false;
                            SetJoinResult("You were unexpectedly disconnected from game server", "Disconnected");
                        } else if (!hasCanceled && !PromptManager.HasPrompt(PromptId.AuthenticationFail)) { // make sure not handled by UnityTokenAuthenticator
                             SetJoinResult("Join Game Failed!");
                        }
                    }
                    if (!SceneManager.GetSceneByName("MainMenu").isLoaded)
                        SceneManager.LoadScene("MainMenu");
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            OnConnected = null;
            OnDisconnected = null;
        }
    }
}
