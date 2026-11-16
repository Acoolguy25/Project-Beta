using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Keeps post-processing on for the camera the commander plays through, so the valley's colour
    /// grade (the start scene's Daylight volume) actually shows. The shared third-person camera ships
    /// with post-processing off, and the game-feel kit's mood blender turns it back off when a mood it
    /// forced on ends, so this re-checks every frame and gives the camera back as it found it.
    /// </summary>
    public sealed class WV_CameraPostProcessing {
        Camera camera;
        UniversalAdditionalCameraData cameraData;
        bool forced;

        /// <summary>Called every frame by the client controller.</summary>
        public void Tick() {
            Camera main = Camera.main;
            if (main != camera) {
                Release();
                camera = main;
                cameraData = main != null ? main.GetUniversalAdditionalCameraData() : null;
            }
            if (cameraData != null && !cameraData.renderPostProcessing) {
                cameraData.renderPostProcessing = true;
                forced = true;
            }
        }

        /// <summary>Turns post-processing back off if this was what turned it on.</summary>
        public void Release() {
            if (forced && cameraData != null)
                cameraData.renderPostProcessing = false;
            forced = false;
            cameraData = null;
            camera = null;
        }
    }
}
