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
        /// <summary>
        /// Whether shared <see cref="RyanAssets.Shared.Combat.ExplosionEffect"/> blasts get the kit's
        /// camera shake (and a sound when they author none). Default true; a game with its own blast
        /// feedback turns it off while it runs.
        /// </summary>
        public static bool ExplosionEffectFeedback { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ExplosionEffectFeedback = true;

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

        /// <summary>
        /// A full-body poof (spawn, vanish, tag) for a character <paramref name="height"/> units tall
        /// standing at <paramref name="feet"/>: several <see cref="FxKind.DustPuff"/>s layered from the
        /// feet to the head instead of one stretched puff, so a 5.6-unit robot gets a full cloud at
        /// normal texture detail. <paramref name="scale"/> scales every puff (1 = as designed).
        /// </summary>
        public static void Poof(Vector3 feet, float height, Color tint, float scale = 1f) =>
            Poof(feet, Vector3.up, height, tint, scale);

        /// <summary>As <see cref="Poof(Vector3, float, Color, float)"/>, white.</summary>
        public static void Poof(Vector3 feet, float height, float scale = 1f) =>
            Poof(feet, Vector3.up, height, Color.white, scale);

        /// <summary>As <see cref="Poof(Vector3, float, Color, float)"/> for a character whose up axis is <paramref name="up"/>.</summary>
        public static void Poof(Vector3 feet, Vector3 up, float height, Color tint, float scale = 1f) {
            if (GameFeelRunner.Get() == null)
                return;
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            height = Mathf.Max(0.2f, height);
            scale = Mathf.Max(0.01f, scale);
            // Puffs grow with the body only up to 1.6x; taller bodies get more layers instead.
            float puff = Mathf.Clamp(height / 2.4f, 0.6f, 1.6f) * scale;
            int layers = Mathf.Clamp(Mathf.CeilToInt(height / 1.1f), 1, 8);
            Quaternion rotation = UpRotation(up);
            Vector3 side = Vector3.Cross(up, Mathf.Abs(Vector3.Dot(up, Vector3.forward)) < 0.9f ? Vector3.forward : Vector3.right).normalized;
            Vector3 side2 = Vector3.Cross(up, side);
            for (int i = 0; i < layers; i++) {
                float along = layers == 1 ? 0.4f : (float)i / (layers - 1);
                // Widest at the feet and waist, a little smaller at the head.
                float size = puff * Mathf.Lerp(1.1f, 0.8f, along);
                float angle = i * 2.39996f; // golden angle spreads the layers around the body
                Vector3 jitter = (side * Mathf.Cos(angle) + side2 * Mathf.Sin(angle)) * (0.12f * height * (i == 0 ? 0f : 1f));
                Vector3 position = feet + up * (along * height * 0.85f) + jitter;
                Run(FxKind.DustPuff, position, rotation, tint, size, false, null, default);
            }
        }

        static Quaternion UpRotation(Vector3 up) =>
            up.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.up, up) : Quaternion.identity;

        static FxHandle Run(FxKind kind, Vector3 position, Quaternion rotation, Color tint, float scale, bool hold, Transform follow, Vector3 offset) {
            GameFeelRunner runner = GameFeelRunner.Get();
            return runner != null ? runner.Fx.Play(kind, position, rotation, tint, scale, hold, follow, offset) : default;
        }
    }
}
