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
        [Min(0f)] public float cannonBlastScale = 0.55f;
        [Min(0f)] public float frostBurstScale = 0.45f;
        [Min(0f)] public float sniperHitScale = 0.8f;

        [Header("Kills")]
        [Tooltip("Shake from a blast fades out over this many metres.")]
        [Min(1f)] public float shakeRadius = 45f;
        [Tooltip("Debris colour multiplied into the spark burst of a wreck.")]
        public Color debrisTint = new(0.45f, 0.42f, 0.38f);
        [Tooltip("Height above a wreck at which the \"+$\" popup appears.")]
        [Min(0f)] public float rewardPopupHeight = 3f;
        [Min(0.1f)] public float rewardPopupSize = 1f;
        [Tooltip("Random sideways spread of kill popups, so kills in a tight group stay readable.")]
        [Min(0f)] public float rewardPopupSpread = 1.2f;
        [Tooltip("Kill coins in quick succession climb this many semitones each.")]
        [Min(0f)] public float coinLadderStep = 1f;
        [Min(1)] public int coinLadderSteps = 10;

        [Header("Enemies")]
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
        [Tooltip("Damage pulse strength per point of base damage (capped at 1).")]
        [Min(0f)] public float damagePulsePerPoint = 0.12f;
        [Tooltip("Seconds between alarm sounds while the base is critical and still being hit.")]
        [Min(1f)] public float alarmInterval = 6f;

        [Header("Towers")]
        public Color levelUpColor = new(1f, 0.82f, 0.25f);
        [Min(0f)] public float buildDustScale = 3.2f;
        [Min(0f)] public float levelUpScale = 4f;
        [Min(0f)] public float sellBurstScale = 2.2f;
        [Min(0.1f)] public float sellPopupSize = 1.3f;

        static readonly TowerFx SilentTower = new();
        static readonly EnemyFx PlainEnemy = new();

        public TowerFx Tower(TD_TowerKind kind) => (int)kind < towers.Length ? towers[(int)kind] : SilentTower;
        public EnemyFx Enemy(TD_EnemyKind kind) => (int)kind < enemies.Length ? enemies[(int)kind] : PlainEnemy;
    }
}
