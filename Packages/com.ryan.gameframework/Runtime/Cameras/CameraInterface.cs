using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using Unity.Cinemachine;
using UnityEngine;

namespace RyanAssets.Cameras {
    /// <summary>Base class for the camera modes managed by <see cref="CameraController"/>.</summary>
    public class ICamera : MonoBehaviour {
        public virtual void EnableCamera(Transform oldCamera, GameCameraType oldCameraType) {
            if (oldCamera != null) {
                transform.position = oldCamera.position;
                transform.rotation = oldCamera.rotation;
            }
        }

        public virtual void DisableCamera(Transform newCamera, GameCameraType newCameraType) {
        }

        /// <summary>
        /// Points this camera mode at <paramref name="target"/> (null clears it). By default every
        /// Cinemachine camera in this mode follows and looks at the character's camera anchor.
        /// Called on every mode, active or not, whenever the controller's target changes.
        /// </summary>
        public virtual void SetTarget(GameCharacter target) {
            Transform anchor = target != null ? target.CharacterCamera : null;
            foreach (var cinemachine in GetComponentsInChildren<CinemachineCamera>(true))
                cinemachine.LookAt = cinemachine.Follow = anchor;
        }
    }
}
