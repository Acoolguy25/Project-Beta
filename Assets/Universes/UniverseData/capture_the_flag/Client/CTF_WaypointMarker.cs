using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// One screen marker for a world target: a coloured disc with an icon and the distance, plus an
    /// arrow that points at the target while it is off screen. Positioned by <see cref="CTF_Waypoints"/>.
    /// </summary>
    public sealed class CTF_WaypointMarker : MonoBehaviour {
        public CanvasGroup group;
        public Image disc;
        public Image icon;
        public TMP_Text distance;
        [Tooltip("Pivot rotated to point at the target; hidden while the target is on screen.")]
        public RectTransform arrow;
        [Min(0.01f)] public float fadeSeconds = 0.15f;

        RectTransform rect;
        int shownMetres = -1;
        bool visible;

        public RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

        void Awake() {
            group.alpha = 0f;
            arrow.gameObject.SetActive(false);
        }

        public void Hide() => visible = false;

        public void Show(Vector2 localPosition, bool offScreen, float arrowDegrees, int metres, Sprite sprite, Color color) {
            visible = true;
            Rect.anchoredPosition = localPosition;
            if (arrow.gameObject.activeSelf != offScreen) arrow.gameObject.SetActive(offScreen);
            if (offScreen) arrow.localRotation = Quaternion.Euler(0f, 0f, arrowDegrees);
            if (icon.sprite != sprite) icon.sprite = sprite;
            if (disc.color != color) disc.color = color;
            if (metres != shownMetres) {
                shownMetres = metres;
                distance.SetText("{0} m", metres);
            }
        }

        void LateUpdate() {
            float target = visible ? 1f : 0f;
            if (!Mathf.Approximately(group.alpha, target))
                group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }
    }
}
