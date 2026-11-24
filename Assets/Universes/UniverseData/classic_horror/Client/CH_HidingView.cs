using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The view from inside a hiding place: you look out through the crack in its door. While you hide the camera can only
    /// peek a little either side of straight out, its near plane sits just past the door so the room shows through the
    /// crack (not the inside of the door), and your own flashlight is dark. Runs after the first-person camera each frame
    /// and before the cutscene director, so a cutscene still wins.
    /// </summary>
    [DefaultExecutionOrder(895)]
    public sealed class CH_HidingView : MonoBehaviour {
        [Tooltip("Camera near plane while hidden (metres): just past the door, which stands this close in front of where you hide.")]
        [SerializeField, Min(0.01f)] float nearClip = 0.32f;
        [Tooltip("How far you can turn your head either side of straight out (degrees).")]
        [SerializeField, Range(0f, 90f)] float peekYaw = 22f;
        [Tooltip("How far you can look up or down (degrees).")]
        [SerializeField, Range(0f, 89f)] float peekPitch = 18f;

        Camera hidingCamera;
        float savedNearClip;
        float outwardYaw;
        GameObject flashlight;
        readonly List<Light> beams = new();

        public bool Active { get; private set; }

        /// <summary>Starts looking out of a hiding place whose door faces <paramref name="outward"/> (with the main camera unless one is given).</summary>
        public void Enter(Vector3 outward, GameObject localFlashlight, Camera view = null) {
            if (Active) Exit();
            hidingCamera = view != null ? view : Camera.main;
            if (hidingCamera == null) return;
            outward.y = 0f;
            outwardYaw = outward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(outward).eulerAngles.y : hidingCamera.transform.eulerAngles.y;
            savedNearClip = hidingCamera.nearClipPlane;
            hidingCamera.nearClipPlane = nearClip;
            flashlight = localFlashlight;
            beams.Clear();
            if (flashlight != null) flashlight.GetComponentsInChildren(true, beams);
            Active = true;
        }

        /// <summary>Gives the camera back exactly as it was. The flashlight relights itself next frame if it is on.</summary>
        public void Exit() {
            if (!Active) return;
            Active = false;
            if (hidingCamera != null) hidingCamera.nearClipPlane = savedNearClip;
            hidingCamera = null;
            flashlight = null;
            beams.Clear();
        }

        void LateUpdate() {
            if (!Active) return;
            if (hidingCamera == null) { Exit(); return; }
            Vector3 look = hidingCamera.transform.eulerAngles;
            float yaw = outwardYaw + Mathf.Clamp(Mathf.DeltaAngle(outwardYaw, look.y), -peekYaw, peekYaw);
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, look.x), -peekPitch, peekPitch);
            hidingCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            // The flashlight switches its beam on every frame it is lit; inside, nobody shines a torch at the door.
            foreach (Light beam in beams) if (beam != null) beam.enabled = false;
        }

        void OnDisable() => Exit();
    }
}
