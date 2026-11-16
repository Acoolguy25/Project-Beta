using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Springy scale animations for anything that was hit, bought, collected or landed on. Works on
    /// plain Transforms and RectTransforms alike, uses unscaled time, and always returns the target
    /// to the scale it had before the first punch (rapid punches never compound).
    /// <code>
    /// Punch.Scale(coinIcon.transform);                     // bump
    /// Punch.Squash(character.Model, 0.25f);                // landing squash (local Y)
    /// Punch.Pop(newBuilding.transform);                    // grow in when spawned
    /// </code>
    /// </summary>
    public static class Punch {
        /// <summary>A uniform bump: grows by <paramref name="amount"/> (0.15 = 15%), wobbles, settles.</summary>
        public static void Scale(Transform target, float amount = 0.15f, float seconds = 0.3f) =>
            Add(target, PunchAnimator.Mode.Scale, amount, seconds);

        /// <summary>
        /// Squash along the target's local Y by <paramref name="amount"/> (bulging sideways), then a
        /// rebound stretch. A negative amount stretches first.
        /// </summary>
        public static void Squash(Transform target, float amount = 0.2f, float seconds = 0.35f) =>
            Add(target, PunchAnimator.Mode.Squash, amount, seconds);

        /// <summary>Grows in from <paramref name="from"/> of its size with a small overshoot (spawns, panels).</summary>
        public static void Pop(Transform target, float from = 0.6f, float seconds = 0.3f) =>
            Add(target, PunchAnimator.Mode.Pop, from, seconds);

        /// <summary>Stops any punch on <paramref name="target"/> and restores its scale.</summary>
        public static void Cancel(Transform target) => GameFeelRunner.Existing?.Punches.Cancel(target);

        static void Add(Transform target, PunchAnimator.Mode mode, float amount, float seconds) {
            if (target == null)
                return;
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Punches.Add(target, mode, amount, seconds);
        }
    }
}
