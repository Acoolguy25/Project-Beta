using Newtonsoft.Json.Linq;
using UnityEngine;

using RyanAssets.NetworkService;
using RyanAssets.PromptService;

using FishNet;
using FishNet.Transporting;
namespace RyanAssets.Client.ClientCore {
    public class ClientConnector: MonoBehaviour {
        public static ClientConnector Instance;
        void Awake(){
            Instance = this;
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientState;
            InstanceFinder.ClientManager.OnClientTimeOut += OnClientTimeOut;
            InstanceFinder.ClientManager.OnAuthenticated += OnClientAuthenticated;
        }
        void SetJoiningMessage(string reason, string title = "Joining"){
            PromptManager.PromptDelete(PromptId.JoinGameAwait);
            if (reason != null)
                PromptManager.PromptWait(title + " Server", reason, PromptId.JoinGameAwait);
        }
        void SetJoinResult(string reason, string title = "Join Failed"){
            SetJoiningMessage(null);
            PromptManager.PromptDelete(PromptId.JoinGameResponse);
            if (reason != null)
                PromptManager.PromptOk(title, reason, PromptId.JoinGameResponse);
        }
        public void JoinGameServer(JObject json){
            SetJoiningMessage("Initializing...");
            Transport transport = InstanceFinder.TransportManager.Transport;
            transport.SetClientAddress((string) json["data"]["server_ip"]);
            transport.SetPort((ushort)json["data"]["server_port"]);
            Debug.Log($"Connecting To {transport.GetClientAddress()}:{transport.GetPort()}");
            bool connectionStatus = InstanceFinder.ClientManager.StartConnection();
            if (!connectionStatus)
                SetJoinResult("Initialization Failed");
            else
                SetJoiningMessage("Connecting To Game Server...");
        }
        private void OnClientTimeOut(){
            SetJoinResult("Client Timed Out");
        }
        private void OnClientState(ClientConnectionStateArgs args){
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting:
                    SetJoiningMessage("Connecting...");
                    break;

                case LocalConnectionState.Started:
                    SetJoinResult(null);
                    break;

                case LocalConnectionState.Stopping:
                    // SetJoiningMessage("Disconnecting from server...", "Disconnecting");
                    break;

                case LocalConnectionState.Stopped:
                    SetJoinResult("You were unexpectedly disconnected from game server", "Disconnected");
                    break;
            }
        }
        private void OnClientAuthenticated(){
            SetJoinResult("Authenticated!");
        }
    }
}