using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RyanAssets.Input {
    /// <summary>
    /// Tells a click from a hold or a drag on one mouse button.
    /// <para>
    /// A button that is already bound to something continuous - the third-person camera orbits
    /// while the right button is held - can still carry a click of its own, as long as the click is
    /// recognised by being short and still. Travel is measured from the mouse's raw
    /// <see cref="Pointer.delta"/> rather than from the cursor position, because an orbiting camera
    /// warps the cursor back to where the press began every frame and a cursor-based test would
    /// read every drag as a perfect click.
    /// </para>
    /// <para>
    /// The tracker only reads input; it never consumes it. The camera still orbits during the
    /// press, and a press that turns into a drag simply produces no click.
    /// </para>
    /// </summary>
    public sealed class MouseClickTracker {
        readonly float maxSeconds;
        readonly float maxTravelPixels;

        bool pressed;
        float pressTime;
        float travel;
        Vector2 pressPosition;

        /// <param name="maxSeconds">Longest press still counted as a click.</param>
        /// <param name="maxTravelPixels">Most raw mouse travel still counted as a click.</param>
        public MouseClickTracker(float maxSeconds = 0.3f, float maxTravelPixels = 6f) {
            this.maxSeconds = maxSeconds;
            this.maxTravelPixels = maxTravelPixels;
        }

        /// <summary>True between a tracked press and its release.</summary>
        public bool IsPressed => pressed;

        /// <summary>
        /// Advances the tracker by one frame. Returns true on the frame a click completes, with the
        /// screen position the press began at.
        /// </summary>
        /// <param name="canBegin">
        /// Whether a press this frame may start a click. Pass false while the pointer is over UI or a
        /// menu owns the mouse; a press already being tracked still finishes normally.
        /// </param>
        public bool Tick(Mouse mouse, ButtonControl button, bool canBegin, out Vector2 clickPosition) {
            clickPosition = default;
            if (mouse == null || button == null) {
                Cancel();
                return false;
            }

            if (button.wasPressedThisFrame && canBegin) {
                pressed = true;
                pressTime = Time.unscaledTime;
                travel = 0f;
                pressPosition = mouse.position.ReadValue();
                return false;
            }

            if (!pressed)
                return false;

            travel += mouse.delta.ReadValue().magnitude;
            if (!button.wasReleasedThisFrame)
                return false;

            pressed = false;
            if (travel > maxTravelPixels || Time.unscaledTime - pressTime > maxSeconds)
                return false;

            clickPosition = pressPosition;
            return true;
        }

        /// <summary>Drops a press in progress so its release is not reported.</summary>
        public void Cancel() {
            pressed = false;
            travel = 0f;
        }
    }
}
