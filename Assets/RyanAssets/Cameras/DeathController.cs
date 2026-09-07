using RyanAssets.Shared.Declarations;
using System.Collections;
using UnityEngine;
using RyanAssets.Client.ClientAudio;

namespace RyanAssets.Cameras {
    public class DeathController : ICamera {
        private const string BlackOverlayName = "DeathBlackOverlay";

        void OnEnable() {
            EnsureBlackOverlay();
        }

        public override void EnableCamera(Transform oldCamera, RyanAssets.Shared.Declarations.GameCameraType oldCameraType) {
            base.EnableCamera(oldCamera, oldCameraType);
            Object.FindFirstObjectByType<MusicService>()?.StopAllMusic();
            EnsureBlackOverlay();
        }

        void EnsureBlackOverlay() {
            if (transform.Find(BlackOverlayName) != null)
                return;

            GameObject overlayObject = new GameObject(
                BlackOverlayName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.Image));
            overlayObject.transform.SetParent(transform, false);

            Canvas canvas = overlayObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            RectTransform rectTransform = overlayObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            UnityEngine.UI.Image image = overlayObject.GetComponent<UnityEngine.UI.Image>();
            image.color = Color.black;
            image.raycastTarget = false;
        }
    }
}
