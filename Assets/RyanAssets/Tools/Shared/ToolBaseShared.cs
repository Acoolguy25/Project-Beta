using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using FishNet.Serializing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;


namespace RyanAssets.Tools.Shared {
    public class ToolBaseShared : NetworkBehaviour {
        [SerializeField]
        private MonoScript clientScript, clientObserver, serverScript;
        [SerializeField]
        public ToolEnum toolEnum;
        [SerializeField]
        public string toolName, toolDesc;
        [SerializeField]
        public Sprite toolImage;
        [SerializeField]
        public string ParentObjectName = "RightHand";
        [SerializeField]
        public uint staminaCost = 10;
        [SerializeField]
        public uint hitDamage = 150;

        public NetworkBehaviour connectedCharacter;
        public GameObject weaponRoot;

        public bool equipped => weaponRoot.activeInHierarchy;

        public Action<ToolBaseShared> equippedEvent, unequippedEvent;
        public static Action<ToolBaseShared> equippedStaticEvent, unequippedStaticEvent;
        public static Action<ToolBaseShared> createStaticEvent, destroyStaticEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            equippedStaticEvent = null;
            unequippedStaticEvent = null;
            createStaticEvent = null;
            destroyStaticEvent = null;
        }
        void Equip() {
            ToolBaseShared otherTool = transform.parent.GetComponentInChildren<ToolBaseShared>();
            if (otherTool != null)
                otherTool.Unequip();
            equippedStaticEvent?.Invoke(this);
            equippedEvent?.Invoke(this);
            weaponRoot.SetActive(true);
        }
        void Unequip() {
            unequippedStaticEvent?.Invoke(this);
            unequippedEvent?.Invoke(this);
            weaponRoot.SetActive(false);
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
        public override void WritePayload(NetworkConnection connection, Writer writer) {
            writer.WriteNetworkBehaviour(connectedCharacter);
        }

        public override void ReadPayload(NetworkConnection connection, Reader reader) {
            connectedCharacter = reader.ReadNetworkBehaviour();
        }
#if !UNITY_SERVER
        
        public override void OnStartClient() {
            weaponRoot.SetActive(false); // unequipped by default
            if (IsOwner) {
                gameObject.AddComponent(clientScript.GetClass());
            } else if (clientObserver != null)
                gameObject.AddComponent(clientObserver.GetClass());
            createStaticEvent?.Invoke(this);
        }
#else
        public override void OnStartServer() {
            base.OnStartServer();
            if (serverScript != null)
                gameObject.AddComponent(serverScript.GetClass());
            weaponRoot.SetActive(false);
            createStaticEvent?.Invoke(this);
        }
#endif
        public override void OnStartNetwork() {
            Transform rightHand = TransformHelper.FindChildRecursive(connectedCharacter.transform, ParentObjectName);
            Debug.Assert(rightHand != null, "RightHand not found!");
            transform.SetParent(rightHand, false);
        }
        void Awake() {
            weaponRoot = transform.GetChild(0).gameObject;
        }
        public void OnDestroy() {
            destroyStaticEvent?.Invoke(this);
        }
    }
}
