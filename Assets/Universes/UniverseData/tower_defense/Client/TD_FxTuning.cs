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
        [Tooltip("Tesla lightning: width (m), life (s) and how far the bolt's kinks stray from a straight line (m).")]
        [Min(0.01f)] public float arcWidth = 0.28f, arcSeconds = 0.16f;
        [Min(0f)] public float arcJitter = 0.9f;
        [Tooltip("How often a living bolt re-jitters (times per second), so it crackles instead of hanging still.")]
        [Min(1f)] public float arcFlickerRate = 30f;
        [Min(0f)] public float arcHitScale = 0.8f;
        [Tooltip("Mortar shells: flight seconds, arc height (m), shell scale, blast scale and shake (0..1).")]
        [Min(0.05f)] public float mortarFlight = 1.05f;
        [Min(0f)] public float mortarArc = 16f, mortarShellScale = 1.1f, mortarBlastScale = 1.7f;
        [Range(0f, 1f)] public float mortarShake = 0.22f;

        [Header("Kills")]
        [Tooltip("Shake from a blast fades out over this many metres.")]
        [Min(1f)] public float shakeRadius = 45f;
        [Tooltip("Debris colour multiplied into the spark burst of a wreck.")]
        public Color debrisTint = new(0.45f, 0.42f, 0.38f);
        [Tooltip("Colour of the smoke a wreck leaves (dark, so it reads as burning metal, not cotton).")]
        public Color smokeTint = new(0.32f, 0.3f, 0.29f);
        [Tooltip("Height above a wreck at which the \"+$\" popup appears.")]
        [Min(0f)] public float rewardPopupHeight = 3f;
        [Tooltip("Popup size for the smallest reward (rewardPopupSmallReward); rewards in between scale up to rewardPopupLargeSize.")]
        [Min(0.1f)] public float rewardPopupSmallSize = 0.5f;
        [Min(1)] public int rewardPopupSmallReward = 6;
        [Tooltip("Popup size for rewardPopupLargeReward and above (a boss).")]
        [Min(0.1f)] public float rewardPopupLargeSize = 1.05f;
        [Min(1)] public int rewardPopupLargeReward = 250;
        [Tooltip("Kills this close together (metres) within rewardMergeSeconds share one popup that shows the sum.")]
        [Min(0f)] public float rewardMergeRadius = 6f;
        [Tooltip("How long a popup waits for more kills nearby before it shows the summed reward.")]
        [Min(0f)] public float rewardMergeSeconds = 0.35f;
        [Tooltip("Random sideways spread of kill popups, so kills in a tight group stay readable.")]
        [Min(0f)] public float rewardPopupSpread = 1.2f;
        [Tooltip("Kill coins in quick succession climb this many semitones each.")]
        [Min(0f)] public float coinLadderStep = 1f;
        [Min(1)] public int coinLadderSteps = 10;

        /// <summary>Kill popup size for <paramref name="reward"/>: small for a Scout, large for a boss (log scale between).</summary>
        public float RewardPopupSize(int reward) {
            float t = Mathf.InverseLerp(Mathf.Log(rewardPopupSmallReward), Mathf.Log(Mathf.Max(rewardPopupSmallReward + 1, rewardPopupLargeReward)), Mathf.Log(Mathf.Max(1, reward)));
            return Mathf.Lerp(rewardPopupSmallSize, rewardPopupLargeSize, t);
        }

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

        [Header("Shields and medics")]
        [Tooltip("Burst when a shield breaks (shield colour) and its callout.")]
        [Min(0f)] public float shieldBreakScale = 2.2f;
        public string shieldBreakText = "SHIELD DOWN";
        [Min(0.1f)] public float shieldBreakTextSize = 0.6f;
        [Tooltip("Shields that break together (an EMP, a Tesla chain) share one callout: no new one for this many seconds.")]
        [Min(0f)] public float shieldBreakTextGap = 0.8f;
        public SfxSound shieldBreakSound = new();
        [Tooltip("Hits a shield soaks (the shield bar drops) spark in the shield colour at this scale.")]
        [Min(0f)] public float shieldSparkScale = 0.5f;
        [Tooltip("Hits too weak to touch a shield (TD_Rules.ShieldBlockBelow): a grey ricochet glancing off, a dull ping and, now and then, a callout.")]
        public Color ricochetColor = new(0.78f, 0.8f, 0.84f);
        [Min(0f)] public float ricochetScale = 0.6f;
        public SfxSound ricochetSound = new();
        public string blockedText = "BLOCKED";
        [Min(0.1f)] public float blockedTextSize = 0.55f;
        [Tooltip("At most one BLOCKED callout this often (seconds), however many Gunners are bouncing.")]
        [Min(0f)] public float blockedTextGap = 2.5f;
        [Tooltip("A BLOCKED callout the HUD guard would move further than this (fraction of screen height) is skipped, so it never lands on a different enemy or tower.")]
        [Range(0f, 1f)] public float blockedMaxShift = 0.06f;
        [Tooltip("Heal sparkles on an enemy a medic is repairing: at most one per enemy every this many seconds.")]
        [Min(0.05f)] public float healFxInterval = 0.7f;
        [Min(0f)] public float healFxScale = 0.9f;

        [Header("Abilities")]
        [Tooltip("Airstrike jet: flying height and speed (m, m/s), how far before the target it appears and its scale.")]
        [Min(1f)] public float jetHeight = 24f, jetSpeed = 95f, jetLeadIn = 140f;
        [Min(0.1f)] public float jetScale = 1f;
        [Tooltip("Bombs: how many blasts, spread over the strike radius, and the time between them.")]
        [Min(1)] public int strikeBlasts = 6;
        [Tooltip("The stick of bombs covers this share of the strike's diameter along the road, each blast jittered by this share of the radius.")]
        [Range(0f, 1f)] public float strikeSpread = 0.8f, strikeJitter = 0.25f;
        [Tooltip("The ring marking the strike, as a share of its radius.")]
        [Min(0f)] public float strikeRingScale = 0.5f;
        [Min(0f)] public float strikeBlastInterval = 0.07f, strikeBlastScale = 2.2f;
        [Range(0f, 1f)] public float strikeShake = 0.85f;
        [Tooltip("Ground marker where the strike lands, shown from the call until the bombs fall.")]
        [Min(0f)] public float strikeMarkerScale = 3f;
        public SfxSound jetSound = new();
        [Tooltip("EMP: ring size at the base and on each enemy, the screen flash and its length.")]
        [Min(0f)] public float empRingScale = 14f, empEnemyScale = 1.6f;
        public Color empFlash = new(0.45f, 0.75f, 1f, 0.35f);
        [Min(0.05f)] public float empFlashSeconds = 0.35f;
        public SfxSound empSound = new();
        [Tooltip("Callout over the strike or the base (the toast pill names who called it). Kept low and short: "
            + "at 8 m and with the caster's name it ran over the top panel.")]
        [Min(0.1f)] public float abilityCalloutSize = 0.9f;
        [Min(0f)] public float abilityCalloutHeight = 4f;

        [Header("Carrier")]
        [Tooltip("A destroyed Carrier bursts in the drone colour (ring and sparks) so the swarm it lets loose reads, with a short callout.")]
        [Min(0f)] public float carrierBurstScale = 3f;
        public string carrierBurstText = "DRONES!";
        [Min(0.1f)] public float carrierBurstTextSize = 0.6f;

        [Header("Popups and the HUD")]
        [Tooltip("Popups are kept below the HUD's top band (TD_ScreenGuard): their half height and the gap kept, as shares of the screen height.")]
        [Range(0f, 0.2f)] public float popupGuardHalfHeight = 0.04f, popupGuardMargin = 0.012f;
        [Tooltip("Half width per character of a popup, and the least half width (shares of the screen height).")]
        [Range(0f, 0.1f)] public float popupGuardCharWidth = 0.012f, popupGuardMinHalfWidth = 0.035f;
        [Tooltip("The retention kit's toast column (top right) is kept clear this far down from its top (canvas units).")]
        [Min(0f)] public float kitToastColumnHeight = 180f;

        [Header("New enemies")]
        [Tooltip("The new-enemy card stays up this long into the wave it introduces (it shows from the build phase before).")]
        [Min(0f)] public float introWaveSeconds = 10f;
        [Tooltip("The first blocked hit of a game also says why in a toast, for this long.")]
        [Min(0.3f)] public float blockedHintSeconds = 4f;

        [Header("Base")]
        public Color baseHitFlash = new(1f, 0.25f, 0.2f);
        [Min(0.05f)] public float baseHitFlashSeconds = 0.35f;
        [Tooltip("Camera kick when an enemy reaches the base.")]
        [Range(0f, 1f)] public float leakShake = 0.35f;
        [Tooltip("Edge flash (ScreenFx.DangerFlash) on a leak: a moment, never a standing wash. The HUD's red base bar and pulsing base icon are the standing warning.")]
        public Color leakFlashColor = new(1f, 0.12f, 0.08f);
        [Min(0.1f)] public float leakFlashSeconds = 0.7f;
        [Range(0f, 1f)] public float leakFlashAlpha = 0.32f;
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
        [Tooltip("While a tower is in hand, cells within this many cells of the aim show their grid ring.")]
        [Min(1)] public int gridCells = 3;

        [Header("Game over")]
        [Tooltip("Confetti burst at the base on victory.")]
        [Min(0f)] public float victoryConfettiScale = 8f;
        [Tooltip("Bursts in front of the camera on victory, beside the result card, so the celebration is seen from any camera.")]
        [Min(0)] public int victoryScreenBursts = 6;
        [Min(0f)] public float victoryScreenBurstInterval = 0.35f;
        [Tooltip("Distance in front of the camera of those bursts (metres).")]
        [Min(1f)] public float victoryScreenBurstDepth = 9f;
        [Min(0f)] public float victoryScreenBurstScale = 1.6f;
        [Tooltip("Viewport points (x, y) of the bursts, used in turn; keep them clear of the card in the middle and the status bar on top.")]
        public Vector2[] victoryScreenBurstPoints = { new(0.2f, 0.42f), new(0.8f, 0.42f), new(0.3f, 0.25f), new(0.7f, 0.25f), new(0.12f, 0.6f), new(0.88f, 0.6f) };
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
