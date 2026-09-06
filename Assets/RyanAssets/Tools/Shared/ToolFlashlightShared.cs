using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    /// <summary>Replicated utility light. Charge is intentionally unlimited; game
    /// modes can make visibility itself a cost without stranding a dark player.</summary>
    public sealed class ToolFlashlightShared : ToolBaseShared {
        public readonly SyncVar<bool> LightOn = new(true);
        public readonly SyncVar<Vector3> AimDirection = new(Vector3.forward);
        [SerializeField] Light beam;
        [SerializeField] float toggleCooldown = 0.2f;
        float nextToggle, nextAim;
        public bool IsIlluminating => LightOn.Value && equipped;
        public Vector3 BeamDirection => AimDirection.Value.normalized;

        [ServerRpc]
        public void ToggleLightServerRpc() {
            if (Time.unscaledTime < nextToggle || !equipped || connectedCharacter == null
                || connectedCharacter.GetComponent<IEntity>()?.IsDead == true) return;
            nextToggle = Time.unscaledTime + toggleCooldown;
            LightOn.Value = !LightOn.Value;
        }

        [ServerRpc]
        public void AimServerRpc(Vector3 direction) {
            if (Time.unscaledTime < nextAim || !equipped
                || !float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z)
                || direction.sqrMagnitude < 0.5f || direction.sqrMagnitude > 1.5f) return;
            nextAim = Time.unscaledTime + 0.07f;
            AimDirection.Value = direction.normalized;
        }

        void LateUpdate() {
            if (beam == null) return;
            beam.enabled = IsIlluminating;
            if (connectedCharacter == null) return;
            // The owner sees a beam from their lens; observers see the same light
            // aimed from the character. Only direction is accepted over the wire.
            Camera camera = IsOwner ? Camera.main : null;
            if (camera != null) {
                beam.transform.SetPositionAndRotation(camera.transform.position + camera.transform.right * 0.12f, camera.transform.rotation);
            } else {
                Transform eye = connectedCharacter.transform.Find("CharacterCamera");
                beam.transform.position = eye != null ? eye.position : connectedCharacter.transform.position + Vector3.up * 1.6f;
                beam.transform.rotation = Quaternion.LookRotation(BeamDirection);
            }
        }
    }
}
