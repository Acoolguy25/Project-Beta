using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RyanAssets.Cameras {
    /// <summary>Eye-level camera using the normal character input and menu gates.</summary>
    [DefaultExecutionOrder(-20), RequireComponent(typeof(Camera), typeof(AudioListener))]
    public sealed class FirstPersonController : ICamera {
        [SerializeField, Range(40, 110)] float fieldOfView = 76f;
        [SerializeField, Range(0.01f, 0.3f)] float nearClip = 0.04f;
        [SerializeField, Range(30, 89)] float pitchLimit = 85f;
        [SerializeField] Vector3 eyeOffset = new(0, 0, 0.08f);
        readonly Dictionary<Renderer, ShadowCastingMode> hiddenRenderers = new();
        GameCharacter target;
        Camera view;
        float yaw, pitch;
        bool hasFocus = true;

        public override void EnableCamera(Transform oldCamera, GameCameraType oldCameraType) {
            if (oldCamera != null) {
                yaw = oldCamera.eulerAngles.y;
                pitch = Mathf.DeltaAngle(0, oldCamera.eulerAngles.x);
            }
            view = GetComponent<Camera>();
            view.fieldOfView = fieldOfView;
            view.nearClipPlane = nearClip;
            view.farClipPlane = 1000f;
            view.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            gameObject.tag = "MainCamera";
        }

        void OnEnable() {
            CameraController.OnCameraTargetAdded += OnTarget;
            CameraController.OnCameraTargetRemoved += OnTargetRemoved;
            OnTarget(CameraController.targetCharacter);
        }

        void OnTarget(GameCharacter next) {
            RestoreBody();
            target = next;
            if (target == null) return;
            // Keep the local character's shadow while preventing the inside of the
            // head/body from covering the lens. Tools remain independently visible.
            foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                hiddenRenderers[renderer] = renderer.shadowCastingMode;
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            yaw = target.transform.eulerAngles.y;
            pitch = 0;
            ApplyPose();
        }

        void OnTargetRemoved(GameCharacter removed) {
            if (removed == target) { RestoreBody(); target = null; }
        }

        void Update() {
            bool controlling = target != null && !target.IsDead && hasFocus && InputService.Instance != null
                && InputService.GetActionMapActive(RyanAssetsActionMap.Character);
            Cursor.lockState = controlling ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !controlling;
            if (!controlling || InputService.characterControls == null) return;
            Vector2 look = InputService.characterControls.look;
            float sensitivity = GameSettingsClient.GetSettingValue<int>("TurnSensitivity") / 100f;
            // The shared Look bindings already invert Y. Mouse delta is per-frame;
            // the stick is a rate and must be integrated over time.
            float inputScale = InputService.characterControls.LookIsRate ? 0.5f * Time.unscaledDeltaTime : 3f;
            yaw += look.x * inputScale * sensitivity * GameSettingsClient.GetSettingValue<int>("HorizontalTurnSensitivity") / 100f;
            pitch = Mathf.Clamp(pitch + look.y * inputScale * sensitivity * GameSettingsClient.GetSettingValue<int>("VerticalTurnSensitivity") / 100f, -pitchLimit, pitchLimit);
        }

        void LateUpdate() => ApplyPose();
        void ApplyPose() {
            if (target == null || target.CharacterCamera == null) return;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = target.CharacterCamera.position + transform.rotation * eyeOffset;
        }
        void OnApplicationFocus(bool focused) => hasFocus = focused;
        void RestoreBody() {
            foreach (var pair in hiddenRenderers)
                if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value;
            hiddenRenderers.Clear();
        }
        void OnDisable() {
            CameraController.OnCameraTargetAdded -= OnTarget;
            CameraController.OnCameraTargetRemoved -= OnTargetRemoved;
            RestoreBody();
            target = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
