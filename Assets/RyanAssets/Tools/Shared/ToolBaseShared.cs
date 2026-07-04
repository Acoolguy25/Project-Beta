using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Declarations;
using System;
using UnityEngine;


namespace RyanAssets.Tools.Shared {
    public class ToolBaseShared : NetworkBehaviour {
        [SerializeField]
        public ToolEnum toolEnum;
        [SerializeField]
        public string toolName, toolDesc;

        readonly public SyncVar<NetworkBehaviour> connectedCharacter = new();

        public bool equipped => gameObject.activeInHierarchy;

        public Action<ToolBaseShared> equippedEvent, unequippedEvent;
        public static Action<ToolBaseShared> equippedStaticEvent, unequippedStaticEvent;
        public static Action<ToolBaseShared> createEvent, destroyEvent;

        void Equip() {
            equippedStaticEvent?.Invoke(this);
            equippedEvent?.Invoke(this);
            gameObject.SetActive(true);
        }
        void Unequip() {
            unequippedStaticEvent?.Invoke(this);
            unequippedEvent?.Invoke(this);
            gameObject.SetActive(false);
        }
        [ObserversRpc(RunLocally = true, ExcludeOwner = true)]
        public void EquipOthersRpc() {
            Equip();
        }
        [ObserversRpc(RunLocally = true, ExcludeOwner = true)]
        public void UnequipOthersRpc() {
            Unequip();
        }
        [ObserversRpc(RunLocally = true)]
        public void EquipServer() {
            Equip();
        }
        [ObserversRpc(RunLocally = true)]
        public void UnequipServer() {
            Equip();
        }
#if !UNITY_SERVER
        public void EquipClient() {
            Equip();
            EquipServerRpc();
        }
        public void UnequipClient() {
            Unequip();
            UnequipServerRpc();
        }
#endif
        [ServerRpc]
        public void EquipServerRpc() {
            EquipOthersRpc();
        }
        [ServerRpc]
        public void UnequipServerRpc() {
            UnequipOthersRpc();
        }

        // Initalization
        [SerializeField]
        private MonoBehaviour clientScript, clientObserver, serverScript;
        public override void OnStartClient() {
            base.OnStartClient();
            if (IsOwner)
                gameObject.AddComponent(clientScript.GetType());
            else if (clientObserver)
                gameObject.AddComponent(clientObserver.GetType());
            if (equipped)
                Equip();
        }
        public override void OnStartServer() {
            base.OnStartServer();
            gameObject.AddComponent(serverScript.GetType());
        }
        public override void OnStartNetwork() {
            base.OnStartNetwork();
            transform.SetParent(connectedCharacter.Value.transform, false);
        }
        public void OnDestroy() {
            destroyEvent?.Invoke(this);
        }
    }
}