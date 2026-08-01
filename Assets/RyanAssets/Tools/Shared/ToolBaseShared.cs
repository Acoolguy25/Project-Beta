using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using FishNet.Serializing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using System;
using UnityEngine;
using UnityEngine.UIElements;
using RyanAssets.DataService;


namespace RyanAssets.Tools.Shared {
    public class ToolBaseShared : NetworkBehaviour {
        [SerializeField]
        private string clientScript, clientObserver, serverScript;

        [Header("Tool Data")]
        [SerializeField]
        public ToolEnum toolEnum;
        [SerializeField]
        public string toolName, toolDesc;
        [SerializeField]
        public Sprite toolImage;
        [SerializeField]

        [Header("Weapon Stats")]
        public uint staminaCost = 10;
        [SerializeField]
        public uint hitDamage = 150;
        [SerializeField]
        public float attackCooldown = 0.85f;
        [SerializeField]
        public float reloadDuration = 1.0f;

        [Header("Ammo Stats")]
        [SerializeField]
        public int currentAmmo = -1; // -1 to disable ammo
        [SerializeField]
        public int maxClipAmmo = 10;
        [SerializeField]
        public int currentStoredAmmo = -1; // -1 for infinite ammo
        [SerializeField]
        public int maxStoredAmmo = 10;

        [Header("Animation")]
        [SerializeField]
        public string animationPackName;

        [Header("Internal")]
        [SerializeField]
        public string ParentObjectName = "RightHand";

        [SerializeField]
        public GameObject weaponRoot;


        [NonSerialized]
        public NetworkBehaviour connectedCharacter;

        public bool equipped => weaponRoot.activeInHierarchy;

        public Action<ToolBaseShared> equippedEvent, unequippedEvent;
        public Action<int> currentAmmoEvent, maxAmmoEvent;
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
            equippedStaticEvent?.Invoke(this);
            equippedEvent?.Invoke(this);
            weaponRoot.SetActive(true);
        }
        public void Unequip() {
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
            Unequip();
        }
#if !UNITY_SERVER
        public void EquipClient() {
            Equip();
            EquipServerRpc();
        }
        public void UnequipClient() {
            Unequip();
            if (IsController)
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
                SpawnClientScript();
            } else if (clientObserver != "") {
                Type clientObserverType = Type.GetType(clientObserver);
                gameObject.AddComponent(clientObserverType);
            }
            createStaticEvent?.Invoke(this);
        }
#else
        public override void OnStartServer() {
            base.OnStartServer();
            if (serverScript != ""){
                Type serverScriptType = Type.GetType(serverScript);
                gameObject.AddComponent(serverScriptType);
            }
            if (!Owner.IsValid) {
                SpawnClientScript();
            }
            weaponRoot.SetActive(false);
            createStaticEvent?.Invoke(this);
        }
        
#endif
        public override void OnStartNetwork() {
            //if (connectedCharacter == null)
            //    return;
            Transform rightHand = TransformHelper.FindChildRecursive(connectedCharacter.transform, ParentObjectName);
            Debug.Assert(rightHand != null, "RightHand not found!");
            transform.SetParent(rightHand, false);
        }
        void SpawnClientScript() {
            if (clientScript != "") {
                Type clientScriptType = Type.GetType(clientScript);
                gameObject.AddComponent(clientScriptType);
            }
        }
        protected virtual void Awake() {
            weaponRoot = transform.GetChild(0).gameObject;
        }
        public void OnDestroy() {
            if (equipped)
                Unequip();
            destroyStaticEvent?.Invoke(this);
        }


        // RPC HIT DETECTION
#pragma warning disable CS0067
        public event Action<NetworkObject> hitEvent;
#pragma warning restore CS0067
        public void OnHit(NetworkObject gameCharacter) {
#if UNITY_SERVER
            hitEvent.Invoke(gameCharacter);
#else
        _OnHitRpc(gameCharacter);
#endif
        }
        [ServerRpc(RequireOwnership = true)]
        public void _OnHitRpc(NetworkObject gameCharacter) {
            OnHit(gameCharacter);
        }
    }
}
