using RyanAssets.Shared.Declarations;
using System.Collections;
using UnityEngine;
using RyanAssets.Client.ClientAudio;

namespace RyanAssets.Cameras {
    public class DeathController : ICamera {
        private const string BlackOverlayName = "DeathBlackOverlay";

        public override void EnableCamera(Transform oldCamera, RyanAssets.Shared.Declarations.GameCameraType oldCameraType) {
            base.EnableCamera(oldCamera, oldCameraType);
        }
    }
}
