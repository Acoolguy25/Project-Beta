using Newtonsoft.Json.Linq;
using UnityEngine;

using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using UnityEngine.SceneManagement;
using RyanAssets.Client.ClientCore;

using FishNet;
using FishNet.Transporting;
using System.Threading.Tasks;
using RyanAssets.Client.ClientModules;
namespace RyanAssets.Client.ClientCore {
    public class ClientConnector : MonoBehaviour {
        public static ClientConnector Instance;
        [SerializeField]
        GameObject[] gameOnlyObjects;
        static bool wasAuthenticated, isConnecting, hasCanceled;
        static string joinServerId;
        void Awake() {
            if (Instance != null) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientState;
            InstanceFinder.ClientManager.OnClientTimeOut += OnClientTimeOut;
            InstanceFinder.ClientManager.OnAuthenticated += OnClientAuthenticated;
            DontDestroyOnLoad(gameObject);
            SetGameActive(false);
        }
        void SetGameActive(bool active) {
            foreach (GameObject gameObj in gameOnlyObjects) {
                gameObj.SetActive(active);
            }
        }
        void SetJoiningMessage(string reason, string title = "Joining") {
            PromptManager.PromptDelete(PromptId.JoinGameAwait);
            if (reason != null)
                _ = PromptManager.PromptCancelableWait(title + " Server", reason, PromptId.JoinGameAwait).ContinueWith(async task => {
                    PromptButton button = await task;
                    if (button == PromptButton.Cancel)
                        CancelJoinGameServer();
                });
        }
        void SetJoinResult(string reason, string title = "Join Failed") {
            SetJoiningMessage(null);
            PromptManager.PromptDelete(PromptId.JoinGameResponse);
            if (reason != null)
                PromptManager.PromptOk(title, reason, PromptId.JoinGameResponse);
        }
        async Task<(string, JObject)> WaitForServerLoad() {
            return await BackendNetwork.GetRequest($"/api/servers/v1/{joinServerId}/wait");
        }
        public async void JoinGameServer(JObject json) {
            string status = json["data"]["status"].ToString();
            joinServerId = json["data"]["server_id"].ToString();
            if (status == "starting") {
                (string response, JObject obj) = await BackendClient.RequestAsync(WaitForServerLoad, "Waiting For Server", promptWaiting: PromptId.PlayGameAwait, promptResult: PromptId.PlayGameConfirm, retryPolicy: RetryPolicy.RetryOrCancel);
                if (response != null)
                    return;
            } else if (status != "ready") {
                SetJoinResult($"Unknown Join Status: {status}");
                return;
            }
            SetJoiningMessage("Initializing...");
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
                    if (wasAuthenticated) {
                        wasAuthenticated = false;
                        SetJoinResult("You were unexpectedly disconnected from game server", "Disconnected");
                        if (SceneManager.GetActiveScene().name != "MainMenu")
                            SceneManager.LoadScene("MainMenu");
                    } else if (!hasCanceled) {
                        SetJoinResult("Join Game Failed!");
                    }
                    SetGameActive(false);
                    break;
            }
        }
        private void OnClientAuthenticated() {
            // SetJoinResult("Authenticated!", "Join Success");
            SetJoinResult(null);
            wasAuthenticated = true;
            SetGameActive(true);
        }
    }
}