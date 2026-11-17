using System;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// How Tower Defense feels: per-tower shot sounds, per-enemy death blasts and the numbers behind
    /// every burst, shake and popup. Serialized on <see cref="TD_ClientController"/> (set by
    /// TD_HudAuthoring); the effects themselves come from the shared game feel kit.
    /// </summary>
    [Serializable]
    public sealed class TD_FxTuning {
        [Serializable]
        public sealed class TowerFx {
            [Tooltip("Played at the muzzle on every shot.")]
            public SfxSound fire = new();
            [Tooltip("Played where the shot lands (shell blasts, frost bursts, sniper hits). Empty = silent.")]
            public SfxSound impact = new();
            [Tooltip("Scale of the spark burst at the muzzle (0 = none).")]
            [Min(0f)] public float muzzleSparks = 0.35f;
        }

        [Serializable]
        public sealed class EnemyFx {
            [Tooltip("Big fireball, smoke column and heavy boom instead of the small blast.")]
            public bool largeExplosion;
            [Min(0.1f)] public float explosionScale = 1f;
            [Tooltip("Camera shake strength (0..1) at the blast, fading with distance.")]
            [Range(0f, 1f)] public float shake = 0.25f;
            [Tooltip("Scale of the debris sparks and smoke thrown out by the wreck.")]
            [Min(0f)] public float debrisScale = 1f;
        }

        [Tooltip("Index-aligned with TD_TowerKind.")]
        public TowerFx[] towers = Array.Empty<TowerFx>();
        [Tooltip("Index-aligned with TD_EnemyKind.")]
        public EnemyFx[] enemies = Array.Empty<EnemyFx>();

        [Header("Shots")]
        [Min(0f)] public float cannonBlastScale = 0.9f;
        [Min(0f)] public float frostBurstScale = 0.7f;
        [Min(0f)] public float sniperHitScale = 1.1f;
        [Tooltip("Cannon shells: flight seconds, arc height (m) and shell scale.")]
        [Min(0.05f)] public float cannonFlight = 0.45f;
        [Min(0f)] public float cannonArc = 3.5f, cannonShellScale = 0.7f;
        [Tooltip("Frost bolts: flight seconds, arc height (m) and bolt scale.")]
        [Min(0.05f)] public float frostFlight = 0.3f;
        [Min(0f)] public float frostArc = 0.6f, frostBoltScale = 0.5f;
        [Tooltip("Sniper beam and gunner tracer: width (m) and life (s).")]
        [Min(0.01f)] public float sniperBeamWidth = 0.22f, sniperBeamSeconds = 0.22f, tracerWidth = 0.09f, tracerSeconds = 0.06f;
        [Tooltip("How far a tracer's far end fades toward white.")]
        [Range(0f, 1f)] public float tracerTipWhiten = 0.6f;
        [Tooltip("HDR multiplier on the tower colour for shells and bolts in flight.")]
        [Min(0f)] public float projectileGlow = 2f;
        [Tooltip("Shots aim this high above an enemy's pivot; towers without a muzzle fire from this high.")]
        public float aimHeight = 1f, fallbackMuzzleHeight = 3f;

        [Header("Kills")]
        [Tooltip("Shake from a blast fades out over this many metres.")]
        [Min(1f)] public float shakeRadius = 45f;
        [Tooltip("Debris colour multiplied into the spark burst of a wreck.")]
        public Color debrisTint = new(0.45f, 0.42f, 0.38f);
        [Tooltip("Colour of the smoke a wreck leaves (dark, so it reads as burning metal, not cotton).")]
        public Color smokeTint = new(0.32f, 0.3f, 0.29f);
        [Tooltip("Height above a wreck at which the \"+$\" popup appears.")]
        [Min(0f)] public float rewardPopupHeight = 3f;
        [Min(0.1f)] public float rewardPopupSize = 1f;
        [Tooltip("Random sideways spread of kill popups, so kills in a tight group stay readable.")]
        [Min(0f)] public float rewardPopupSpread = 1.2f;
        [Tooltip("Kill coins in quick succession climb this many semitones each.")]
        [Min(0f)] public float coinLadderStep = 1f;
        [Min(1)] public int coinLadderSteps = 10;

        [Header("Enemies")]
        [Tooltip("Enemy health bars are always red (enemy colour), darkening as they empty; friendly bars use the green-to-red health ramp.")]
        public Color enemyBarFull = new(1f, 0.27f, 0.22f);
        public Color enemyBarEmpty = new(0.55f, 0.08f, 0.08f);
        [Tooltip("Colour multiplied over an enemy while frost slows it.")]
        public Color slowTint = new(0.45f, 0.72f, 1f);
        [Tooltip("Colour an enemy flashes toward when hit (HDR multiplier below).")]
        public Color hitFlashColor = Color.white;
        [Range(0f, 1f)] public float hitFlashStrength = 0.55f;
        [Range(1f, 6f)] public float hitFlashBrightness = 2.5f;

        [Header("Base")]
        public Color baseHitFlash = new(1f, 0.25f, 0.2f);
        [Min(0.05f)] public float baseHitFlashSeconds = 0.35f;
        [Tooltip("Camera kick when an enemy reaches the base.")]
        [Range(0f, 1f)] public float leakShake = 0.35f;
        [Tooltip("Edge flash (ScreenFx.DangerFlash) on a leak: a moment, never a standing wash. The HUD's red base bar and pulsing base icon are the standing warning.")]
        public Color leakFlashColor = new(1f, 0.12f, 0.08f);
        [Min(0.1f)] public float leakFlashSeconds = 0.7f;
        [Range(0f, 1f)] public float leakFlashAlpha = 0.22f;
        [Tooltip("The same flash once the base is critical: longer and stronger.")]
        [Min(0.1f)] public float criticalFlashSeconds = 1.4f;
        [Range(0f, 1f)] public float criticalFlashAlpha = 0.5f;
        [Tooltip("Seconds between alarm sounds while the base is critical and still being hit.")]
        [Min(1f)] public float alarmInterval = 6f;

        [Header("Towers")]
        public Color levelUpColor = new(1f, 0.82f, 0.25f);
        [Tooltip("Dust thrown up by building or selling: tinted like the dirt around the pads, never white.")]
        public Color dustTint = new(0.62f, 0.53f, 0.42f);
        [Min(0f)] public float buildDustScale = 2.4f;
        [Min(0f)] public float levelUpScale = 4f;
        [Tooltip("Height of the level-up sparks above the pad.")]
        [Min(0f)] public float levelUpSparkHeight = 2f;
        [Tooltip("Squash (negative = flatten) of a tower as it levels up.")]
        [Range(-0.5f, 0.5f)] public float levelUpSquash = -0.2f;
        [Tooltip("Rings are lifted this much off the pad so its top doesn't swallow them.")]
        [Min(0f)] public float ringLift = 0.25f;
        [Min(0f)] public float sellBurstScale = 2.2f;
        [Min(0.1f)] public float sellPopupSize = 1.3f;
        [Tooltip("Build markers rise out of the ghost at this many metres per second.")]
        [Min(0.1f)] public float markerLiftSpeed = 12f;

        [Header("Game over")]
        [Min(0f)] public float victoryConfettiScale = 8f;
        [Tooltip("Shake radius of the base's final explosion.")]
        [Min(1f)] public float defeatShakeRadius = 90f;

        [Header("Toasts (seconds)")]
        [Min(0.3f)] public float shortToastSeconds = 1.5f;
        [Min(0.3f)] public float toastSeconds = 2.5f;

        [Header("Tower badges")]
        [Tooltip("Badges (level stars) hide when closer to the camera than this many metres.")]
        [Min(0f)] public float badgeHideDistance = 7f;
        [Tooltip("Badges hide above this viewport height, so none ever sits over the status bar.")]
        [Range(0.5f, 1f)] public float badgeMaxViewportY = 0.8f;
        [Tooltip("Badges hide this close to the left or right screen edge (viewport fraction).")]
        [Range(0f, 0.3f)] public float badgeEdgeMargin = 0.04f;

        static readonly TowerFx SilentTower = new();
        static readonly EnemyFx PlainEnemy = new();

        public TowerFx Tower(TD_TowerKind kind) => (int)kind < towers.Length ? towers[(int)kind] : SilentTower;
        public EnemyFx Enemy(TD_EnemyKind kind) => (int)kind < enemies.Length ? enemies[(int)kind] : PlainEnemy;
    }
}
