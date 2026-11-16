using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Pooled, tintable particle effects addressed by <see cref="FxKind"/>. Effects are authored Y-up
    /// (a puff rises, a ring lies flat); pass an up vector or rotation to tilt them, e.g. to a
    /// character's custom gravity. Tint multiplies the effect's own colours (white = as designed).
    /// <code>
    /// GameFx.Play(FxKind.CoinBurst, coin.position);
    /// GameFx.Play(FxKind.Sparks, hit.point, hit.normal, Color.cyan);
    /// FxHandle beacon = GameFx.Start(FxKind.BeaconBeam, flagBase, teamColor);  // ... beacon.Stop();
    /// FxHandle trail = GameFx.Attach(FxKind.Trail, rocket.transform, Vector3.zero, Color.yellow);
    /// </code>
    /// </summary>
    public static class GameFx {
        /// <summary>Plays a one-shot at <paramref name="position"/>, upright, as designed.</summary>
        public static void Play(FxKind kind, Vector3 position, float scale = 1f) =>
            Run(kind, position, Quaternion.identity, Color.white, scale, false, null, default);

        /// <summary>Plays a tinted one-shot, upright.</summary>
        public static void Play(FxKind kind, Vector3 position, Color tint, float scale = 1f) =>
            Run(kind, position, Quaternion.identity, tint, scale, false, null, default);

        /// <summary>Plays a tinted one-shot whose up axis points along <paramref name="up"/> (a surface normal, a character's up).</summary>
        public static void Play(FxKind kind, Vector3 position, Vector3 up, Color tint, float scale = 1f) =>
            Run(kind, position, UpRotation(up), tint, scale, false, null, default);

        /// <summary>Plays a tinted one-shot with a full rotation.</summary>
        public static void Play(FxKind kind, Vector3 position, Quaternion rotation, Color tint, float scale = 1f) =>
            Run(kind, position, rotation, tint, scale, false, null, default);

        /// <summary>Starts an effect that keeps playing (looping kinds) until you stop the returned handle.</summary>
        public static FxHandle Start(FxKind kind, Vector3 position, Color tint, float scale = 1f) =>
            Run(kind, position, Quaternion.identity, tint, scale, true, null, default);

        /// <summary>Starts an effect that keeps playing at <paramref name="rotation"/> until stopped.</summary>
        public static FxHandle Start(FxKind kind, Vector3 position, Quaternion rotation, Color tint, float scale = 1f) =>
            Run(kind, position, rotation, tint, scale, true, null, default);

        /// <summary>
        /// Starts an effect that follows <paramref name="target"/> (at <paramref name="localOffset"/>
        /// in its space) until stopped, or until the target is destroyed, when it fades out in place.
        /// The effect is not parented, so particles already emitted stay in the world (trails).
        /// </summary>
        public static FxHandle Attach(FxKind kind, Transform target, Vector3 localOffset, Color tint, float scale = 1f) {
            if (target == null)
                return default;
            return Run(kind, target.TransformPoint(localOffset), Quaternion.identity, tint, scale, true, target, localOffset);
        }

        /// <summary>
        /// One-call blast: <see cref="FxKind.ExplosionSmall"/> or <see cref="FxKind.ExplosionLarge"/>,
        /// its sound and a distance-scaled camera shake. <paramref name="radius"/> is the shake radius.
        /// </summary>
        public static void Explosion(Vector3 position, bool large = false, float radius = 30f) {
            Play(large ? FxKind.ExplosionLarge : FxKind.ExplosionSmall, position);
            GameSfx.PlayAt(large ? SfxCue.ExplosionLarge : SfxCue.Explosion, position);
            CameraShake.Explosion(position, radius, large ? 1f : 0.6f);
        }

        static Quaternion UpRotation(Vector3 up) =>
            up.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, up) : Quaternion.identity;

        static FxHandle Run(FxKind kind, Vector3 position, Quaternion rotation, Color tint, float scale, bool hold, Transform follow, Vector3 offset) {
            GameFeelRunner runner = GameFeelRunner.Get();
            return runner != null ? runner.Fx.Play(kind, position, rotation, tint, scale, hold, follow, offset) : default;
        }
    }
}
