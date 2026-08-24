using FishNet.Object;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public sealed class DI_HomeBaseTeleporter : MonoBehaviour {
#if !UNITY_SERVER
        [SerializeField] float heightAboveBoard = 1f;

        bool teleported;
        bool hasHomeBase;
        Vector3 homeBasePosition;

        public void SetHomeBase(Vector3 position) {
            homeBasePosition = position;
            hasHomeBase = true;
            TryTeleport();
        }

        public void BeginMatch() {
            teleported = false;
            hasHomeBase = false;
        }

        void Update() {
            TryTeleport();
        }

        void TryTeleport() {
            if (teleported || !hasHomeBase)
                return;

            NetworkObject[] networkObjects = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
            for (int i = 0; i < networkObjects.Length; i++) {
                NetworkObject networkObject = networkObjects[i];
                if (!networkObject.IsOwner || networkObject.GetComponent("LocalCharacter") == null)
                    continue;

                Vector3 destination = homeBasePosition + Vector3.up * heightAboveBoard;
                Rigidbody body = networkObject.GetComponent<Rigidbody>();
                if (body != null) {
                    body.position = destination;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                } else {
                    networkObject.transform.position = destination;
                }

                Physics.SyncTransforms();
                teleported = true;
                return;
            }
        }
#endif
    }
}
