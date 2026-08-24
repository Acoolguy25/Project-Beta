using FishNet;
using FishNet.CodeGenerating;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using RyanAssets.Shared.Global;
using RyanAssets.DataService;
using FishNet.Object.Synchronizing;
using RyanAssets.Tools.Shared;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Item.FloatingTool
{
    public class FloatingToolShared : CollectibleItem {
        [AllowMutableSyncType]
        [SerializeField]
        public SyncVar<ToolEnum> TargetToolSync;
        [SerializeField]
        public Vector3 TargetToolScale = new Vector3(6f, 6f, 6f);
        public System.Func<NetworkBehaviour, ToolEnum, bool> OnToolCollectedFunc;
        // Keep this reference serialized in every build target. If the field is
        // client-only, opening/saving the prefab for a dedicated-server target
        // strips it and clients spawn a pickup with no tool visual.
        [SerializeField]
        Transform hoverTransform;
#if UNITY_SERVER
        protected override bool OnCollectServer(NetworkBehaviour collectObject, NetworkConnection conn) {
            return OnToolCollectedFunc?.Invoke(collectObject, TargetToolSync.Value) ?? false;
        }
#else
        public override void OnStartClient() {
            base.OnStartClient();

            if (hoverTransform == null) {
                Debug.LogError($"{nameof(FloatingToolShared)} on {name} has no hover transform assigned.", this);
                return;
            }

            ToolBaseShared targetTool = FindToolPrefab(TargetToolSync.Value);
            if (targetTool == null) {
                Debug.LogError($"No spawnable tool prefab exists for {TargetToolSync.Value}.", this);
                return;
            }

            for (int i = hoverTransform.childCount - 1; i >= 0; i--)
                Destroy(hoverTransform.GetChild(i).gameObject);

            GameObject displayRoot = new($"{targetTool.name} Display");
            Transform displayTransform = displayRoot.transform;
            displayTransform.SetParent(hoverTransform, false);
            displayTransform.SetLocalPositionAndRotation(targetTool.transform.localPosition + displayTransform.lossyScale.y * 0.5f * Vector3.up, targetTool.transform.localRotation);
            displayRoot.transform.localScale = TargetToolScale;

            GameObject toolVisual = Instantiate(targetTool.weaponRoot, displayTransform, false);
            toolVisual.transform.localPosition = Vector3.zero;
            foreach (Collider collider in toolVisual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody rigidbody in toolVisual.GetComponentsInChildren<Rigidbody>(true)) {
                rigidbody.detectCollisions = false;
                rigidbody.isKinematic = true;
            }
            foreach (AudioSource audioSource in toolVisual.GetComponentsInChildren<AudioSource>(true))
                audioSource.enabled = false;
        }

        ToolBaseShared FindToolPrefab(ToolEnum targetTool) {
            if (InstanceFinder.NetworkManager == null || InstanceFinder.NetworkManager.SpawnablePrefabs == null)
                return null;

            var spawnablePrefabs = InstanceFinder.NetworkManager.SpawnablePrefabs;
            for (int i = 0; i < spawnablePrefabs.GetObjectCount(); i++) {
                NetworkObject prefab = spawnablePrefabs.GetObject(asServer: false, i);
                if (prefab != null
                    && prefab.TryGetComponent(out ToolBaseShared toolPrefab)
                    && toolPrefab.toolEnum == targetTool)
                    return toolPrefab;
            }

            return null;
        }
#endif
    }
}
