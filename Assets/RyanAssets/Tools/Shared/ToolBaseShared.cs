using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;


namespace RyanAssets.Tools.Shared {
    public class ToolBaseShared : NetworkBehaviour {
        [SerializeField]
        public ToolEnum toolEnum;
        [SerializeField]
        public string toolName, toolDesc;
        [SerializeField]
        public Sprite toolImage;
        [SerializeField]
        public string ParentObjectName = "RightHand";

        readonly public SyncVar<NetworkObject> connectedCharacter = new();

        public bool equipped => gameObject.activeInHierarchy;

        public Action<ToolBaseShared> equippedEvent, unequippedEvent;
        public static Action<ToolBaseShared> equippedStaticEvent, unequippedStaticEvent;
        public static Action<ToolBaseShared> createEvent, destroyEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            equippedStaticEvent = null;
            unequippedStaticEvent = null;
            createEvent = null;
            destroyEvent = null;
        }
        void Equip() {
            ToolBaseShared otherTool = transform.parent.GetComponentInChildren<ToolBaseShared>();
            if (otherTool != null)
                otherTool.Unequip();
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
#if !UNITY_SERVER
        [SerializeField]
        private MonoScript clientScript, clientObserver;
        public override void OnStartClient() {
            _ = test();
            if (connectedCharacter.Value != null)
                StartClient();
            else {
                connectedCharacter.OnChange += OnChangeFunction;
            }
            
        }
        async Task test() {
            await Awaitable.WaitForSecondsAsync(1f);
            StartClient();
        }
        void OnChangeFunction(NetworkObject oldval, NetworkObject newval, bool asServer)  {
            if (newval != null) {
                StartClient();
                connectedCharacter.OnChange -= OnChangeFunction;
            }
        }
        void StartClient() {
            StartNetwork();
            if (IsOwner) {
                gameObject.AddComponent(clientScript.GetClass());
                gameObject.SetActive(false); // unequipped by default
            } else if (clientObserver != null)
                gameObject.AddComponent(clientObserver.GetClass());
            createEvent?.Invoke(this);
        }
#else
        [SerializeField]
        private MonoScript serverScript;
        public override void OnStartServer() {
            base.OnStartServer();
            if (serverScript != null)
                gameObject.AddComponent(serverScript.GetClass());
            //gameObject.SetActive(false);
            createEvent?.Invoke(this);
            StartNetwork();
        }
#endif
        public void StartNetwork() {
            Transform rightHand = TransformHelper.FindChildRecursive(connectedCharacter.Value.transform, ParentObjectName);
            Debug.Assert(rightHand != null, "RightHand not found!");
            transform.SetParent(rightHand, false);
        }
        public void OnDestroy() {
            destroyEvent?.Invoke(this);
        }
    }
}