using UnityEngine;
using FishNet;
using FishNet.Object;

namespace RyanAssets.Server.ServerCore {
    public class ServerHelperInit : MonoBehaviour {
        [SerializeField]
        private NetworkObject helperInitPrefab;
        void Awake() {
            ServerBootStrap.StartServerEvent += OnStartServer;
        }
        void OnStartServer() {
            GameObject helperInit = Instantiate(helperInitPrefab.gameObject);
            InstanceFinder.ServerManager.Spawn(helperInit);
        }
    }
}