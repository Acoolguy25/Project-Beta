using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.Input;
using RyanAssets.Tools.Shared;
using RyanAssets.Characters.Shared;
#endif

namespace RyanAssets.Tools.Client {
    public sealed class FlashlightToolClient : ToolBaseClient {
#if !UNITY_SERVER
        float nextAim;
        protected override bool RequiresWorldTarget => false;
        protected ToolFlashlightShared tool => (ToolFlashlightShared)toolBaseShared;
        protected override void OnActivate(Vector3 point) {
            base.OnActivate(point);
            tool.ToggleLightServerRpc();
        }
        void Update() {
            if (!tool.IsOwner || !tool.IsSpawned || !tool.equipped || Time.unscaledTime < nextAim || Camera.main == null) return;
            nextAim = Time.unscaledTime + 0.1f;
            tool.AimServerRpc(Camera.main.transform.forward);
        }
#endif
    }
}
