using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Retention.Client {
    /// <summary>
    /// The small always-on level pill: level number, XP bar, streak flame, today's challenge count and
    /// the key that opens the challenges panel. Clicking it opens the panel too. Built by the retention
    /// HUD authoring; driven by <see cref="RetentionHud"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RetentionLevelChip : MonoBehaviour {
        public Button button;
        [Tooltip("Scaled by punches (level-ups, completed challenges).")]
        public RectTransform punchTarget;
        public Image levelDisc;
        public TMP_Text levelLabel;
        public HudBar bar;
        public GameObject streakGroup;
        public Image streakIcon;
        public TMP_Text streakLabel;
        public Image challengesIcon;
        public TMP_Text challengesLabel;
        public GameObject keyCap;
        public TMP_Text keyLabel;
        [Header("+XP popup")]
        public HudPopup gainPopup;
        public TMP_Text gainLabel;
        [Tooltip("Scale punch of the +XP label on each gain.")]
        [Range(0f, 1f)] public float gainPunch = 0.18f;
        [Min(0.05f)] public float punchSeconds = 0.3f;

        int shownLevel = -1, shownStreak = -1, shownDone = -1, shownTotal = -1;
        int pendingGain;
        float gainEndsAt;

        public void SetStyle(RetentionHudStyle style) {
            if (levelDisc != null) levelDisc.color = style.xp;
            if (bar != null) bar.SetColor(style.xp);
            if (streakIcon != null) streakIcon.color = style.streak;
            if (challengesIcon != null) challengesIcon.color = style.challenge;
            if (gainLabel != null) gainLabel.color = Color.Lerp(style.xp, Color.white, 0.35f);
        }

        public void SetKey(string key) {
            if (keyLabel == null) return;
            keyLabel.text = key;
            if (keyCap != null) keyCap.SetActive(!string.IsNullOrEmpty(key));
        }

        public void SetLevel(int level, float fraction) {
            if (bar != null) bar.Value = fraction;
            if (level == shownLevel || levelLabel == null) return;
            shownLevel = level;
            levelLabel.SetText("{0}", level);
        }

        /// <summary>Shows the streak flame while a streak is alive (days &gt; 0).</summary>
        public void SetStreak(int days) {
            if (days == shownStreak) return;
            shownStreak = days;
            if (streakGroup != null) streakGroup.SetActive(days > 0);
            if (streakLabel != null) streakLabel.SetText("{0}", days);
        }

        public void SetChallenges(int done, int total) {
            if (done == shownDone && total == shownTotal) return;
            shownDone = done;
            shownTotal = total;
            if (challengesLabel != null) challengesLabel.SetText("{0}/{1}", (float)done, total); // float args: two ints would be ambiguous with TMP's (span, start, length) overload
        }

        /// <summary>Pops "+XP" beside the chip; gains within <paramref name="seconds"/> of each other add up.</summary>
        public void ShowGain(int xp, float seconds) {
            if (gainPopup == null || gainLabel == null) return;
            pendingGain = Time.unscaledTime < gainEndsAt ? pendingGain + xp : xp;
            gainEndsAt = Time.unscaledTime + seconds;
            gainLabel.SetText("+{0} XP", pendingGain);
            gainPopup.Show(seconds);
            if (gainPunch > 0f) Punch.Scale(gainLabel.transform, gainPunch, punchSeconds);
        }

        /// <summary>A scale punch of the whole chip (level-up, completed challenge).</summary>
        public void Bump(float amount) {
            if (punchTarget != null && amount > 0f) Punch.Scale(punchTarget, amount, punchSeconds);
        }
    }
}
