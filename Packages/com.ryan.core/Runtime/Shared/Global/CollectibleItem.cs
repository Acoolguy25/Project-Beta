using FishNet;
using FishNet.CodeGenerating;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RyanAssets.Shared.Global {
    public class CollectibleItem : NetworkBehaviour {
        const string PlayerTag = "Player";
        const string NpcTag = "NPC";

        [AllowMutableSyncType]
        [SerializeField]
        public SyncVar<bool> playerCharacterTrigger = new(true);

        [AllowMutableSyncType]
        [SerializeField]
        public SyncVar<bool> npcCharacterTrigger = new(false);

        [SerializeField]
        Vector3 spin = new(0f, 90f, 0f);

        void OnTriggerEnter(Collider other) {
#if UNITY_SERVER
            CollectNpcOnServer(other);
#else
            CollectLocalPlayer(other);
#endif
        }

#if !UNITY_SERVER
        void CollectLocalPlayer(Collider other) {
            if (!playerCharacterTrigger.Value)
                return;

            NetworkBehaviour collectObject = other.GetComponentInParent<NetworkBehaviour>();
            if (collectObject == null
                || !collectObject.CompareTag(PlayerTag)
                || !collectObject.Owner.IsValid
                || !collectObject.Owner.IsLocalClient)
                return;

            CollectPlayerServerRpc(collectObject);
        }

        void Update() {
            transform.rotation = Quaternion.Euler(spin * Time.time);
        }
#endif

#if UNITY_SERVER
        bool isCollected;

        public override void OnStartServer() {
            base.OnStartServer();
            isCollected = false;
        }
#endif

        [ServerRpc(RequireOwnership = false)]
        void CollectPlayerServerRpc(NetworkBehaviour collectObject, NetworkConnection conn = null) {
#if UNITY_SERVER
            if (!playerCharacterTrigger.Value || isCollected)
                return;
            if (collectObject == null
                || conn == null
                || !collectObject.CompareTag(PlayerTag)
                || !collectObject.Owner.IsValid
                || collectObject.Owner != conn) {
                conn?.Kick(FishNet.Managing.Server.KickReason.MalformedData);
                return;
            }

            CollectOnServer(collectObject, conn);
#endif
        }

#if UNITY_SERVER
        void CollectNpcOnServer(Collider other) {
            if (!npcCharacterTrigger.Value || isCollected)
                return;

            NetworkBehaviour collectObject = other.GetComponentInParent<NetworkBehaviour>();
            if (collectObject == null
                || !collectObject.CompareTag(NpcTag)
                || collectObject.Owner.IsValid)
                return;

            CollectOnServer(collectObject, null);
        }

        void CollectOnServer(NetworkBehaviour collectObject, NetworkConnection conn) {
            if (isCollected || !OnCollectServer(collectObject, conn))
                return;

            isCollected = true;
            OnCollectedClientRpc();
            Despawn();
        }

        protected virtual bool OnCollectServer(NetworkBehaviour collectObject, NetworkConnection conn) {
            return true;
        }
#endif

        [ObserversRpc]
        void OnCollectedClientRpc() {
#if !UNITY_SERVER
            OnCollectedClient();
#endif
        }

#if !UNITY_SERVER
        protected virtual void OnCollectedClient() { }
#endif

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            if (SharedGlobalEvents.Instance != null)
                NetworkObject.transform.SetParent(SharedGlobalEvents.Instance.transform, true);
        }
    }
}
