using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Keeps Tower Defense's world popups (kill rewards, "SHIELD DOWN", "BLOCKED", ability callouts) off the
    /// HUD's top band. The kit's FloatingText draws over every canvas, so a popup spawned near the top of
    /// the screen covered the status bar, the pill row or a toast. Before a popup spawns, its screen box
    /// (with the rise it will make) is tested against the HUD panels that are showing, and the spawn point
    /// is moved straight down the screen, at the same depth, until it clears them.
    /// </summary>
    public sealed class TD_ScreenGuard {
        readonly RectTransform[] blockers;
        readonly CanvasGroup[] groups;
        readonly TD_Fader[] faders;
        readonly RectTransform kitToasts;
        readonly TD_FxTuning tuning;
        readonly List<Rect> rects = new();
        readonly Vector3[] corners = new Vector3[4];
        int cachedFrame = -1;

        /// <param name="blockers">HUD panels to keep clear (hidden ones and faded-out ones are ignored).</param>
        /// <param name="kitToasts">The retention kit's toast feed: kept clear down to <see cref="TD_FxTuning.kitToastColumnHeight"/>, as its rows come and go.</param>
        public TD_ScreenGuard(RectTransform[] blockers, RectTransform kitToasts, TD_FxTuning tuning) {
            this.blockers = blockers ?? System.Array.Empty<RectTransform>();
            groups = new CanvasGroup[this.blockers.Length];
            faders = new TD_Fader[this.blockers.Length];
            for (int i = 0; i < this.blockers.Length; i++) {
                if (this.blockers[i] == null) continue;
                groups[i] = this.blockers[i].GetComponentInParent<CanvasGroup>();
                faders[i] = this.blockers[i].GetComponentInParent<TD_Fader>();
            }
            this.kitToasts = kitToasts;
            this.tuning = tuning;
        }

        /// <summary>
        /// <paramref name="position"/>, or a point straight below it on screen (same depth) where a popup with
        /// <paramref name="characters"/> characters that rises <paramref name="rise"/> metres stays clear of the
        /// HUD's top band. Unchanged when no camera is up or the point is behind it.
        /// </summary>
        public Vector3 Clear(Vector3 position, float rise, int characters) => Clear(position, rise, characters, out _);

        /// <inheritdoc cref="Clear(Vector3, float, int)"/>
        /// <param name="shift">How far the popup was moved down, as a fraction of the screen height (0 = not moved).</param>
        public Vector3 Clear(Vector3 position, float rise, int characters, out float shift) {
            shift = 0f;
            var cam = Camera.main;
            if (cam == null) return position;
            Vector3 screen = cam.WorldToScreenPoint(position);
            if (screen.z <= 0f) return position;
            Refresh();
            float h = Screen.height;
            float riseY = Mathf.Max(0f, cam.WorldToScreenPoint(position + Vector3.up * rise).y - screen.y);
            float halfHeight = tuning.popupGuardHalfHeight * h;
            float halfWidth = Mathf.Max(tuning.popupGuardMinHalfWidth, characters * tuning.popupGuardCharWidth) * h;
            float margin = tuning.popupGuardMargin * h;
            float y = screen.y;
            // The top edge of the screen counts too: a popup must not rise off it.
            y = Mathf.Min(y, h - margin - riseY - halfHeight);
            for (int pass = 0; pass < 4; pass++) {
                bool moved = false;
                foreach (var r in rects) {
                    if (screen.x + halfWidth < r.xMin || screen.x - halfWidth > r.xMax) continue;
                    if (y - halfHeight > r.yMax || y + riseY + halfHeight < r.yMin) continue;
                    y = r.yMin - margin - riseY - halfHeight;
                    moved = true;
                }
                if (!moved) break;
            }
            if (Mathf.Abs(y - screen.y) < 0.5f) return position;
            shift = (screen.y - y) / h;
            return cam.ScreenToWorldPoint(new Vector3(screen.x, y, screen.z));
        }

        /// <summary>Forgets this frame's rectangles: call after showing a panel that popups later this frame must clear.</summary>
        public void Invalidate() => cachedFrame = -1;

        /// <summary>The screen rectangles of the panels showing this frame (worked out once a frame).</summary>
        void Refresh() {
            if (cachedFrame == Time.frameCount) return;
            cachedFrame = Time.frameCount;
            rects.Clear();
            for (int i = 0; i < blockers.Length; i++) {
                var rect = blockers[i];
                if (rect == null || !rect.gameObject.activeInHierarchy) continue;
                // A fading panel counts by where it is going: one fading in this frame (a toast shown just
                // before its popup) is kept clear already, one fading out is not.
                if (faders[i] != null ? !faders[i].Visible : groups[i] != null && groups[i].alpha < 0.05f) continue;
                if (TryScreenRect(rect, out var r)) rects.Add(r);
            }
            if (kitToasts != null && kitToasts.gameObject.activeInHierarchy && TryScreenRect(kitToasts, out var toasts, needHeight: false)) {
                // The feed grows down from its top edge as toasts arrive: keep a whole column clear.
                var canvas = kitToasts.GetComponentInParent<Canvas>();
                float column = tuning.kitToastColumnHeight * (canvas != null ? canvas.scaleFactor : 1f);
                rects.Add(Rect.MinMaxRect(toasts.xMin, Mathf.Min(toasts.yMin, toasts.yMax - column), toasts.xMax, toasts.yMax));
            }
        }

        bool TryScreenRect(RectTransform rect, out Rect screen, bool needHeight = true) {
            rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera ui = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (var corner in corners) {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(ui, corner);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            screen = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return screen.width > 1f && (!needHeight || screen.height > 1f);
        }
    }
}
