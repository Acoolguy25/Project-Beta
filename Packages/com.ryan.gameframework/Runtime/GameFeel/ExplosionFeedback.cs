using RyanAssets.Shared.Combat;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Gives every shared <see cref="ExplosionEffect"/> a camera shake on this client, and the kit's
    /// explosion sound when the effect's prefab authors no report of its own. The effect only raises a
    /// client event (<see cref="ExplosionEffect.Exploded"/>), so shared and server code never depend on
    /// this client-only kit. Tuned in GameFeelSettings &gt; Shared Explosions; switched at runtime with
    /// <see cref="GameFx.ExplosionEffectFeedback"/>.
    /// </summary>
    internal static class ExplosionFeedback {
        // After ExplosionEffect clears its event at SubsystemRegistration.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe() {
            ExplosionEffect.Exploded -= OnExploded;
            ExplosionEffect.Exploded += OnExploded;
        }

        static void OnExploded(ExplosionEffect effect, Vector3 position, float scale) {
            if (!GameFx.ExplosionEffectFeedback)
                return;
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings == null || !settings.explosions.enabled)
                return;
            GameFeelSettings.ExplosionTuning tuning = settings.explosions;
            bool large = scale >= tuning.largeScale;
            if (effect == null || !effect.HasAuthoredAudio)
                GameSfx.PlayAt(large ? SfxCue.ExplosionLarge : SfxCue.Explosion, position);
            float radius = tuning.shakeRadius * Mathf.Max(1f, scale);
            if (effect != null)
                radius = Mathf.Max(radius, effect.DamageRadius * 2f);
            float strength = large ? 1f : tuning.shakeStrength;
            CameraShake.Explosion(position, radius, strength, tuning.shakeSeconds);
        }
    }
}
