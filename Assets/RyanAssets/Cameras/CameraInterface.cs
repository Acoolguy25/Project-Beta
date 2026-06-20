using System.Collections;
using UnityEngine;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Cameras {
    public class ICamera: MonoBehaviour {
        public virtual void EnableCamera(Transform oldCamera, GameCameraType oldCameraType) {
            // default
            transform.position = oldCamera.position;
        }
        public virtual void DisableCamera(Transform newCamera, GameCameraType newCameraType) {
            // default
        }
    }
}