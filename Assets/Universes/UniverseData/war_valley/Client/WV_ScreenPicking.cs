using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Screen-space tests the commander's box and click selection are built on: whether a unit,
    /// troop, or building as drawn falls inside a drag box, and whether a point is on screen.
    /// <para>
    /// The box is tested against each target's whole on-screen outline plus a margin rather than
    /// against the single point at its feet. Testing only that point missed a soldier whose body was
    /// plainly inside the box, and a tank whose pivot sat just outside it.
    /// </para>
    /// </summary>
    public static class WV_ScreenPicking {
        /// <summary>
        /// How far outside a drag box a target may be drawn and still be picked up, as a fraction of
        /// the screen's height, so a box drawn roughly around a squad takes the squad.
        /// </summary>
        public const float BoxSelectPaddingScreenFraction = 0.03f;

        // Main-thread only, like every caller; shared so a box test allocates nothing.
        static readonly List<Renderer> rendererScratch = new();
        static readonly List<Collider> colliderScratch = new();

        /// <summary>Whether any part of <paramref name="target"/> as drawn, padded, overlaps <paramref name="box"/>.</summary>
        public static bool TouchesBox(Camera camera, Component target, Rect box) {
            if (target == null || !TryGetScreenOutline(camera, target, out Rect outline))
                return false;
            float padding = Screen.height * BoxSelectPaddingScreenFraction;
            outline = Rect.MinMaxRect(
                outline.xMin - padding, outline.yMin - padding, outline.xMax + padding, outline.yMax + padding);
            return outline.Overlaps(box);
        }

        /// <summary>
        /// The screen rectangle <paramref name="target"/>'s visible body covers, from its renderers or,
        /// failing those, its colliders. False when it has neither or is wholly behind the camera.
        /// </summary>
        public static bool TryGetScreenOutline(Camera camera, Component target, out Rect outline) {
            outline = default;
            if (!TryGetWorldBounds(target, out Bounds bounds))
                return false;

            Vector3 centre = bounds.center;
            Vector3 extents = bounds.extents;
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2) {
                Vector3 screen = camera.WorldToScreenPoint(centre + Vector3.Scale(extents, new Vector3(x, y, z)));
                // Behind the camera a point projects mirrored in front of it, so it is left out.
                if (screen.z <= 0f)
                    continue;
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            if (float.IsInfinity(min.x))
                return false;
            outline = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        /// <summary>World bounds of the enabled renderers under <paramref name="target"/>, or of its colliders when it draws nothing.</summary>
        public static bool TryGetWorldBounds(Component target, out Bounds bounds) {
            bounds = default;
            bool found = false;
            target.GetComponentsInChildren(false, rendererScratch);
            foreach (Renderer renderer in rendererScratch) {
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer)
                    continue;
                if (!found) {
                    bounds = renderer.bounds;
                    found = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            if (found)
                return true;

            target.GetComponentsInChildren(false, colliderScratch);
            foreach (Collider collider in colliderScratch) {
                if (collider == null || !collider.enabled)
                    continue;
                if (!found) {
                    bounds = collider.bounds;
                    found = true;
                } else {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            return found;
        }

        /// <summary>Whether <paramref name="worldPosition"/> projects inside the screen rectangle from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static bool IsInsideBox(Camera camera, Vector3 worldPosition, Vector2 min, Vector2 max) {
            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            // Anything behind the camera projects to a mirrored point in front of it, so the
            // depth test has to come before the rectangle test.
            if (viewport.z <= 0f)
                return false;

            Vector2 screen = camera.WorldToScreenPoint(worldPosition);
            return screen.x >= min.x && screen.x <= max.x && screen.y >= min.y && screen.y <= max.y;
        }

        /// <summary>Whether <paramref name="worldPosition"/> is anywhere on screen.</summary>
        public static bool IsOnScreen(Camera camera, Vector3 worldPosition) =>
            IsInsideBox(camera, worldPosition, Vector2.zero, new Vector2(Screen.width, Screen.height));
    }
}
