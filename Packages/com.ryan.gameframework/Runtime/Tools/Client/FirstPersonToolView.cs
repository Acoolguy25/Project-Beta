using RyanAssets.Tools.Shared;
using UnityEngine;

namespace RyanAssets.Tools.Client {
    /// <summary>Owner-only hand presentation for any tool in a locked first-person view.</summary>
    public sealed class FirstPersonToolView : MonoBehaviour {
        [SerializeField] Vector3 cameraOffset = new(0.24f, -0.22f, 0.4f);
        [SerializeField] Vector3 cameraAngles;
        [SerializeField, Min(0.01f)] float viewScale = 0.65f;
        ToolBaseShared tool;
        Vector3 originalPosition;
        Vector3 originalScale;
        Quaternion originalRotation;
        bool posed;
        void Start() {
            tool = GetComponent<ToolBaseShared>();
            originalPosition = tool.weaponRoot.transform.localPosition;
            originalScale = tool.weaponRoot.transform.localScale;
            originalRotation = tool.weaponRoot.transform.localRotation;
        }
        void LateUpdate() {
#if !UNITY_SERVER
            if (tool == null || !tool.IsOwner) return;
            var camera = Camera.main;
            bool firstPerson = RyanAssets.DataService.PlayerData.localData != null
                && RyanAssets.DataService.PlayerData.localData.lockedCameraType.Value == (int)RyanAssets.Shared.Declarations.GameCameraType.FirstPersonCamera;
            if (firstPerson && camera != null && tool.equipped) {
                Vector3 scale = tool.weaponRoot.transform.parent.lossyScale;
                tool.weaponRoot.transform.localScale = new Vector3(viewScale / Mathf.Max(0.001f, scale.x), viewScale / Mathf.Max(0.001f, scale.y), viewScale / Mathf.Max(0.001f, scale.z));
                tool.weaponRoot.transform.SetPositionAndRotation(camera.transform.TransformPoint(cameraOffset), camera.transform.rotation * Quaternion.Euler(cameraAngles));
                posed = true;
            } else if (posed) {
                tool.weaponRoot.transform.SetLocalPositionAndRotation(originalPosition, originalRotation);
                tool.weaponRoot.transform.localScale = originalScale;
                posed = false;
            }
#endif
        }
    }
}
