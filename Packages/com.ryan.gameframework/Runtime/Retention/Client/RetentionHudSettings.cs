using System;
using RyanAssets.GameFeel;
using UnityEngine;

namespace RyanAssets.Retention.Client {
    /// <summary>The parts of the retention HUD a game can move (<see cref="RetentionHud.SetPlacement"/>).</summary>
    public enum RetentionHudPart { LevelChip, ChallengesPanel, Toasts, SummaryCard }

    /// <summary>
    /// Where one HUD element sits: anchor (and pivot) on the screen plus an offset in canvas units
    /// (1920x1080 reference). <see cref="RetentionHud"/> applies it in Awake, so a game moves an
    /// element by overriding the value on its HUD instance or prefab variant.
    /// </summary>
    [Serializable]
    public struct RetentionPlacement {
        [Tooltip("Screen anchor and pivot (0,0 = bottom left, 1,1 = top right).")]
        public Vector2 anchor;
        [Tooltip("Offset from the anchor in canvas units.")]
        public Vector2 offset;

        public RetentionPlacement(Vector2 anchor, Vector2 offset) {
            this.anchor = anchor;
            this.offset = offset;
        }

        public void ApplyTo(RectTransform rect) {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset;
        }
    }

    /// <summary>The accent colours of the retention HUD (one per kind of reward).</summary>
    [Serializable]
    public sealed class RetentionHudStyle {
        [Tooltip("Level chip, XP bar and XP popups.")]
        public Color xp = new(0.24f, 0.55f, 1f);
        [Tooltip("Daily challenges: progress bars and completion toasts.")]
        public Color challenge = new(0.2f, 0.78f, 0.48f);
        [Tooltip("Level-up toasts and the summary card's level emblem.")]
        public Color levelUp = new(1f, 0.79f, 0.24f);
        [Tooltip("Unlock toasts and the unlock reveal.")]
        public Color unlock = new(0.73f, 0.55f, 1f);
        [Tooltip("The play streak flame.")]
        public Color streak = new(1f, 0.6f, 0.25f);
        [Tooltip("Account gold amounts.")]
        public Color gold = new(1f, 0.79f, 0.24f);
        [Tooltip("Warnings (progress not saving).")]
        public Color warning = new(0.95f, 0.33f, 0.36f);
        [Tooltip("Text and bars of finished challenges.")]
        public Color muted = new(0.49f, 0.53f, 0.59f);
    }

    /// <summary>Sounds, effects and timings of the retention HUD.</summary>
    [Serializable]
    public sealed class RetentionHudFeel {
        [Header("Sounds")]
        public SfxCue xpGain = SfxCue.Pickup;
        public SfxCue challengeComplete = SfxCue.Purchase;
        public SfxCue levelUp = SfxCue.LevelUp;
        public SfxCue unlock = SfxCue.Win;
        [Tooltip("Ticks as the summary lines appear (rising a semitone each).")]
        public SfxCue summaryLine = SfxCue.Coin;
        [Range(0f, 1f)] public float xpGainVolume = 0.35f;

        [Header("Toasts")]
        [Min(0.5f)] public float toastSeconds = 4f;
        [Tooltip("The '+XP' popup by the level chip stays this long; gains within it add up.")]
        [Min(0.2f)] public float xpGainSeconds = 1.6f;
        [Tooltip("Show 'LEVEL 7' in the centre of the screen on a level-up during play.")]
        public bool levelUpCenterText = true;
        [Min(0.2f)] public float levelUpCenterSeconds = 1.4f;
        [Range(0.2f, 1f)] public float levelUpCenterSize = 0.7f;

        [Header("Summary card")]
        [Tooltip("Wait after the summary arrives before showing it, so the game's own result shows first.")]
        [Min(0f)] public float summaryDelaySeconds = 1.2f;
        [Tooltip("Seconds between two reward lines appearing.")]
        [Min(0f)] public float lineInterval = 0.18f;
        [Tooltip("Seconds a full bar (one whole level) takes to fill.")]
        [Min(0.1f)] public float secondsPerLevel = 1.1f;
        [Tooltip("Shortest fill, so small gains still read as movement.")]
        [Min(0f)] public float minFillSeconds = 0.5f;
        [Tooltip("Pause on a full bar at each level-up.")]
        [Min(0f)] public float levelUpPause = 0.35f;
        [Tooltip("How long each unlock is shown before the next one.")]
        [Min(0.2f)] public float unlockSeconds = 1.8f;
        [Tooltip("The card hides itself this long after its animation ends (0 = until closed).")]
        [Min(0f)] public float autoHideSeconds = 9f;

        [Header("Confetti (summary level-ups and unlocks)")]
        [Min(0)] public int confettiBursts = 2;
        [Tooltip("Viewport points the bursts use in turn, beside the card.")]
        public Vector2[] confettiPoints = { new(0.62f, 0.62f), new(0.92f, 0.38f), new(0.7f, 0.3f), new(0.95f, 0.7f) };
        [Tooltip("Distance in front of the camera.")]
        [Min(1f)] public float confettiDepth = 9f;
        [Min(0f)] public float confettiScale = 1.4f;
        [Tooltip("Scale punch of the level emblem and chip on a level-up.")]
        [Range(0f, 1f)] public float levelPunch = 0.3f;
        [Tooltip("Scale a summary line pops in from.")]
        [Range(0.3f, 1f)] public float linePopFrom = 0.8f;
        [Tooltip("Scale an unlock icon pops in from.")]
        [Range(0.1f, 1f)] public float unlockPopFrom = 0.4f;
        [Tooltip("Duration of the punches and pops above.")]
        [Min(0.05f)] public float punchSeconds = 0.35f;
    }
}
