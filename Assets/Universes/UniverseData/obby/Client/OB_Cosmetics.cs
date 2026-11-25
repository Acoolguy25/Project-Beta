using System.Collections.Generic;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Retention.Client;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Shows the local player's level unlocks (<see cref="OB_Retention.Unlocks"/>): a trail behind their
    /// robot, the star radar, gold checkpoint flags and the finish fireworks. The level comes from the
    /// retention HUD's snapshot, so it matches what the server announced. Local presentation only.
    /// </summary>
    public sealed class OB_Cosmetics : MonoBehaviour {
        enum TrailStyle { None, Cloud, Spark, Rainbow, Comet, Champion }

        [Tooltip("The shared retention HUD; its level decides what is unlocked.")]
        public RetentionHud retentionHud;

        [Header("Trails")]
        [Tooltip("Where the trail follows, in the character body's space.")]
        public Vector3 trailOffset = new(0f, 1.4f, 0f);
        public Color cloudTrail = new(1f, 1f, 1f, 0.8f);
        public Color sparkTrail = OB_Palette.Hex(0x9FE6FF);
        public Color cometTrail = OB_Palette.Hex(0xFF8A3D);
        public Color championTrail = OB_Palette.Gold;
        [Min(0f)] public float trailScale = 1.1f;
        [Tooltip("Scale of the comet and champion trails.")]
        [Min(0f)] public float bigTrailScale = 1.6f;
        [Tooltip("Seconds the rainbow trail takes to run through every colour.")]
        [Min(0.1f)] public float rainbowSeconds = 5f;
        [Tooltip("Seconds between the sparks a spark or champion trail throws while moving.")]
        [Min(0.05f)] public float sparkInterval = 0.3f;
        [Tooltip("Slowest speed (m/s) that throws sparks.")]
        [Min(0f)] public float sparkMinimumSpeed = 6f;

        [Header("Star radar")]
        public Color starBeamColor = OB_Palette.Star;
        [Min(0f)] public float starBeamScale = 1f;

        [Header("Gold flags")]
        [Tooltip("Material for the flag cloth of reached checkpoints.")]
        public Material goldFlagMaterial;

        [Header("Finish fireworks")]
        [Min(1)] public int fireworkBursts = 10;
        [Min(0f)] public float fireworkSpread = 6f;
        [Min(0f)] public float fireworkHeight = 4f;
        [Min(0f)] public float fireworkRingScale = 7f;

        TrailStyle shownStyle;
        Transform trailTarget;
        FxHandle trail;
        float nextSpark;
        readonly List<FxHandle> starBeams = new();

        public int Level => retentionHud != null ? retentionHud.Level : 1;

        bool Owns(string unlockId) => OB_Retention.Owns(Level, unlockId);

        TrailStyle Style =>
            Owns(OB_Retention.ChampionTrailUnlock) ? TrailStyle.Champion
            : Owns(OB_Retention.CometTrailUnlock) ? TrailStyle.Comet
            : Owns(OB_Retention.RainbowTrailUnlock) ? TrailStyle.Rainbow
            : Owns(OB_Retention.SparkTrailUnlock) ? TrailStyle.Spark
            : Owns(OB_Retention.CloudTrailUnlock) ? TrailStyle.Cloud
            : TrailStyle.None;

        void OnDisable() {
            trail.Stop();
            shownStyle = TrailStyle.None;
            trailTarget = null;
            StopBeams();
        }

        void Update() {
            var player = CharacterMovement.Local;
            Transform body = player != null && player.Body != null ? player.Body.transform : null;
            TrailStyle style = body != null ? Style : TrailStyle.None;
            if (style != shownStyle || body != trailTarget) {
                trail.Stop();
                shownStyle = style;
                trailTarget = body;
                if (style != TrailStyle.None) trail = GameFx.Attach(FxKind.Trail, body, trailOffset, TrailColor(style), TrailScale(style));
            }
            if (style == TrailStyle.None) return;
            if (style == TrailStyle.Rainbow) trail.SetTint(TrailColor(style));
            if ((style is TrailStyle.Spark or TrailStyle.Champion) && Time.time >= nextSpark
                && player.Body.linearVelocity.sqrMagnitude >= sparkMinimumSpeed * sparkMinimumSpeed) {
                nextSpark = Time.time + sparkInterval;
                GameFx.Play(FxKind.Sparks, body.TransformPoint(trailOffset), TrailColor(style), 0.6f);
            }
        }

        Color TrailColor(TrailStyle style) => style switch {
            TrailStyle.Cloud => cloudTrail,
            TrailStyle.Spark => sparkTrail,
            TrailStyle.Rainbow => Color.HSVToRGB(Mathf.Repeat(Time.time / rainbowSeconds, 1f), 0.65f, 1f),
            TrailStyle.Comet => cometTrail,
            TrailStyle.Champion => championTrail,
            _ => Color.clear
        };

        float TrailScale(TrailStyle style) => style is TrailStyle.Comet or TrailStyle.Champion ? bigTrailScale : trailScale;

        // ---------------------------------------------------------------- course hooks (called by the controller)
        /// <summary>With the star radar, every star still to collect shines a beam into the sky.</summary>
        public void ShowStars(OB_CourseLayout layout, ulong collected) {
            StopBeams();
            if (layout == null || !Owns(OB_Retention.StarRadarUnlock)) return;
            for (int s = 0; s < layout.StageCount && s < 64; s++) {
                var stage = layout.stages[s];
                if (!stage.hasStar || (collected & (1UL << s)) != 0) continue;
                starBeams.Add(GameFx.Start(FxKind.BeaconBeam, stage.star, starBeamColor, starBeamScale));
            }
        }

        void StopBeams() {
            foreach (var beam in starBeams) beam.Stop();
            starBeams.Clear();
        }

        /// <summary>
        /// With gold flags, a reached checkpoint's flag cloth turns gold (call after the pad's own marker
        /// has shown it reached). Flags start plain on every new course, so only reached ones are touched.
        /// </summary>
        public void DecorateFlag(OB_CheckpointFlag flag, bool reached) {
            if (!reached || flag == null || flag.flag == null || goldFlagMaterial == null || !Owns(OB_Retention.GoldFlagsUnlock)) return;
            if (flag.flag.TryGetComponent(out Renderer cloth)) cloth.sharedMaterial = goldFlagMaterial;
        }

        /// <summary>With finish fireworks, a ring of confetti bursts in the air and a big shockwave.</summary>
        public void Finish(Vector3 position) {
            if (!Owns(OB_Retention.ConfettiFinishUnlock)) return;
            for (int i = 0; i < fireworkBursts; i++) {
                float angle = i * Mathf.PI * 2f / fireworkBursts;
                var at = position + new Vector3(Mathf.Cos(angle) * fireworkSpread, fireworkHeight, Mathf.Sin(angle) * fireworkSpread);
                GameFx.Play(FxKind.Confetti, at, OB_Palette.Blocks[i % OB_Palette.Blocks.Length]);
            }
            GameFx.Play(FxKind.ShockwaveRing, position, OB_Palette.Gold, fireworkRingScale);
        }
    }
}
