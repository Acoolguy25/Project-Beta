using UnityEngine;
using FishNet;
using FishNet.Object;
using FishNet.Transporting;

namespace RyanAssets.Shared.Server {
    public class ServerHelperInit : MonoBehaviour {
#if UNITY_SERVER
        [SerializeField]
        private NetworkObject helperInitPrefab;
        void Awake() {
            // ServerBootStrap.StartServerEvent += OnStartServer;
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }
        void OnServerConnectionState(ServerConnectionStateArgs args) {
            if (args.ConnectionState == LocalConnectionState.Started){
                GameObject helperInit = Instantiate(helperInitPrefab.gameObject);
                InstanceFinder.ServerManager.Spawn(helperInit);
            }
        }
#endif
    }
}