using System;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.murder_mystery.Client {
    /// <summary>
    /// What a smoke bomb looks and sounds like (<see cref="MM_SmokeBroadcast"/>): a thick grey cloud at the
    /// innocent's feet that hangs for a few seconds, a pop, a smoke trail behind them while they run, and, for
    /// the thrower, speed lines for the burst. Built from the shared game-feel kit. Owned and ticked by
    /// <see cref="MM_ClientHUD"/>; its numbers are tunable on the HUD in the Inspector.
    /// </summary>
    [Serializable]
    public sealed class MM_SmokeFx {
        [Tooltip("Cloud colour (the kit's smoke is tinted by it).")]
        public Color cloudColor = new(0.62f, 0.64f, 0.68f, 1f);
        [Tooltip("Size of the poof at the feet; the robots are about 5.6 m tall.")]
        [Min(0.1f)] public float poofScale = 2.2f;
        [Tooltip("Smoke puffs in a ring round the spot, and how far out they sit (metres).")]
        [Min(0)] public int ringPuffs = 7;
        [Min(0f)] public float ringRadius = 2.6f;
        [Min(0.1f)] public float ringPuffScale = 3.2f;
        [Tooltip("Puffs stacked up the body so the cloud hides a standing robot (metres between layers).")]
        [Min(0)] public int columnPuffs = 3;
        [Min(0.1f)] public float columnSpacing = 1.6f;
        [Tooltip("The cloud is topped up this many times, this far apart, so it hangs instead of fading at once.")]
        [Min(0)] public int refills = 5;
        [Min(0.05f)] public float refillSeconds = 0.6f;
        [Tooltip("Smoke trail behind the runner while the burst lasts.")]
        [Min(0f)] public float trailScale = 2f;
        [Min(0f)] public float trailHeight = 2.2f;
        [Tooltip("Speed lines on the thrower's screen during the burst.")]
        [Range(0f, 1f)] public float speedLines = 0.65f;
        [Range(0f, 1f)] public float popVolume = 0.8f;
        [Tooltip("Players further than this from a smoke bomb do not hear it.")]
        [Min(1f)] public float audibleDistance = 50f;

        Vector3 cloudAt;
        int refillsLeft;
        float nextRefillAt;
        FxHandle trail;
        float trailUntil, speedLinesUntil;

        /// <summary>A bomb went off at <paramref name="position"/>; <paramref name="runner"/> is whoever threw it (may be null).</summary>
        public void Play(Vector3 position, Transform runner, bool yours, float boostSeconds, Vector3 listener) {
            cloudAt = position;
            Puff(position);
            refillsLeft = refills;
            nextRefillAt = Time.time + refillSeconds;
            if (yours || (listener - position).sqrMagnitude <= audibleDistance * audibleDistance) {
                GameSfx.PlayAt(SfxCue.Explosion, position, popVolume * 0.5f, GameSfx.Semitones(7f));
                GameSfx.PlayAt(SfxCue.Whoosh, position, popVolume);
            }
            if (runner != null && trailScale > 0f && boostSeconds > 0f) {
                trail.Stop();
                trail = GameFx.Attach(FxKind.Trail, runner, Vector3.up * trailHeight, cloudColor, trailScale);
                trailUntil = Time.time + boostSeconds;
            }
            if (yours && speedLines > 0f && boostSeconds > 0f) {
                ScreenFx.SetSpeedLines(speedLines);
                speedLinesUntil = Time.time + boostSeconds;
            }
        }

        /// <summary>Per frame: tops the cloud up, and ends the trail and the speed lines with the burst.</summary>
        public void Tick() {
            float now = Time.time;
            if (refillsLeft > 0 && now >= nextRefillAt) {
                refillsLeft--;
                nextRefillAt = now + refillSeconds;
                Puff(cloudAt, refill: true);
            }
            if (trailUntil > 0f && now >= trailUntil) {
                trailUntil = 0f;
                trail.Stop();
            }
            if (speedLinesUntil > 0f && now >= speedLinesUntil) {
                speedLinesUntil = 0f;
                ScreenFx.SetSpeedLines(0f);
            }
        }

        /// <summary>Stops everything at once (round over, HUD going away).</summary>
        public void Clear() {
            refillsLeft = 0;
            trail.Stop();
            trailUntil = 0f;
            if (speedLinesUntil > 0f)
                ScreenFx.SetSpeedLines(0f);
            speedLinesUntil = 0f;
        }

        void Puff(Vector3 at, bool refill = false) {
            if (!refill)
                GameFx.Poof(at, 5.6f, cloudColor, poofScale);
            for (int i = 0; i < ringPuffs; i++) {
                float angle = (i + (refill ? 0.5f : 0f)) / Mathf.Max(1, ringPuffs) * Mathf.PI * 2f;
                Vector3 offset = new(Mathf.Cos(angle) * ringRadius, 0.6f, Mathf.Sin(angle) * ringRadius);
                GameFx.Play(FxKind.Smoke, at + offset, cloudColor, ringPuffScale);
            }
            for (int i = 0; i < columnPuffs; i++)
                GameFx.Play(FxKind.Smoke, at + Vector3.up * (1f + i * columnSpacing), cloudColor, ringPuffScale);
        }
    }
}
