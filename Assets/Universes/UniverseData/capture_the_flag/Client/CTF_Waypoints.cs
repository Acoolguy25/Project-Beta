using RyanAssets.Characters.Client;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Keeps the flag (and, while you carry, home) markers over their targets, clamped to the screen
    /// edge with an arrow when the target is behind you or off screen. Told what to show by
    /// <see cref="CTF_ClientController"/>; this only does the screen maths.
    /// </summary>
    public sealed class CTF_Waypoints : MonoBehaviour {
        [Tooltip("Full-screen rect the markers live in.")]
        public RectTransform area;
        [Tooltip("Index 0 red flag, 1 blue flag.")]
        public CTF_WaypointMarker[] flagMarkers = new CTF_WaypointMarker[2];
        public CTF_WaypointMarker homeMarker;
        [Tooltip("Metres above the target the marker points at (the robot is about 5.6 m tall).")]
        [Min(0f)] public float heightOffset = 6f;
        [Header("Safe area (share of the screen kept clear for the HUD)")]
        [Tooltip("Left and right, as a share of the screen width.")]
        [Range(0f, 0.4f)] public float sideMargin = 0.06f;
        [Tooltip("Top, as a share of the screen height: below the scoreboard and flag chips.")]
        [Range(0f, 0.4f)] public float topMargin = 0.27f;
        [Tooltip("Bottom, as a share of the screen height: above the objective, team card and toolbar.")]
        [Range(0f, 0.4f)] public float bottomMargin = 0.24f;
        [Tooltip("Hide a marker when you are closer than this to its target (it is right in front of you).")]
        [Min(0f)] public float hideWithin = 10f;

        Camera cam;

        public void SetFlag(int index, bool show, Vector3 target, Sprite icon, Color color) =>
            Place(flagMarkers[index], show, target, icon, color);

        public void SetHome(bool show, Vector3 target, Sprite icon, Color color) =>
            Place(homeMarker, show, target, icon, color);

        void Place(CTF_WaypointMarker marker, bool show, Vector3 target, Sprite icon, Color color) {
            // The player switches between camera modes, each its own Camera: follow the live one.
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (!show || cam == null) {
                marker.Hide();
                return;
            }
            Vector3 from = LocalPlayer.Character != null ? LocalPlayer.Character.transform.position : cam.transform.position;
            float metres = Vector3.Distance(from, target);
            if (metres < hideWithin) {
                marker.Hide();
                return;
            }

            Vector3 screen = cam.WorldToScreenPoint(target + Vector3.up * heightOffset);
            bool behind = screen.z < 0f;
            // Behind the camera the projection mirrors through the centre; flip it back.
            if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);
            Vector2 point = screen;
            float w = Screen.width, h = Screen.height;
            Rect inner = Rect.MinMaxRect(w * sideMargin, h * bottomMargin, w * (1f - sideMargin), h * (1f - topMargin));
            bool offScreen = behind || !inner.Contains(point);
            float arrowDegrees = 0f;
            if (offScreen) {
                Vector2 centre = inner.center;
                Vector2 direction = point - centre;
                // A target behind you sits on the left or right edge (turn that way), never under the HUD.
                if (behind) direction = new Vector2(direction.x >= 0f ? 1f : -1f, 0f);
                // Scale the direction until it touches the safe rectangle.
                float scaleX = Mathf.Abs(direction.x) > 0.001f ? (inner.width * 0.5f) / Mathf.Abs(direction.x) : float.PositiveInfinity;
                float scaleY = Mathf.Abs(direction.y) > 0.001f ? (inner.height * 0.5f) / Mathf.Abs(direction.y) : float.PositiveInfinity;
                point = centre + direction * Mathf.Min(scaleX, scaleY);
                // The arrow sprite points up.
                arrowDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, point, null, out Vector2 local);
            marker.Show(local, offScreen, arrowDegrees, Mathf.RoundToInt(metres), icon, color);
        }
    }
}
