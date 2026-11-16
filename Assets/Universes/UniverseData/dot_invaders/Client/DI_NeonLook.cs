using UnityEngine;
#if !UNITY_SERVER
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// The board's neon look: a global Volume with the game's own profile (strong bloom on the
    /// HDR neon colours, neutral tonemapping that keeps team colours saturated, a soft vignette), generated
    /// by DI_Authoring as DI_NeonProfile.asset. The shared top-down camera renders without
    /// post-processing, so while the board is active this turns it on for the main camera and
    /// gives it back afterwards. A kit mood (<c>MoodVolume.Apply</c>, priority 20) still wins over it.
    /// </summary>
    public sealed class DI_NeonLook : MonoBehaviour {
#if !UNITY_SERVER
        [Tooltip("Global volume carrying DI_NeonProfile; lives on a child of the board.")]
        [SerializeField] Volume volume;

        Camera boundCamera;
        UniversalAdditionalCameraData boundCameraData;
        bool forcedPostProcessing;

        void Awake() {
            if (volume == null) {
                Debug.LogError("Dot Invaders: the board has no neon volume. Run Ryan/Dot Invaders/Rebuild HUD and Board.", this);
                enabled = false;
            }
        }

        void OnEnable() {
            if (volume != null)
                volume.enabled = true;
        }

        void LateUpdate() {
            // The top-down camera can take over after the board appears, so follow Camera.main.
            Camera main = Camera.main;
            if (main != boundCamera) {
                Release();
                boundCamera = main;
                boundCameraData = main != null ? main.GetUniversalAdditionalCameraData() : null;
            }
            if (boundCameraData != null && !boundCameraData.renderPostProcessing) {
                boundCameraData.renderPostProcessing = true;
                forcedPostProcessing = true;
            }
        }

        void OnDisable() {
            Release();
            boundCamera = null;
            boundCameraData = null;
            if (volume != null)
                volume.enabled = false;
        }

        void Release() {
            if (forcedPostProcessing && boundCameraData != null)
                boundCameraData.renderPostProcessing = false;
            forcedPostProcessing = false;
        }
#endif
    }
}
