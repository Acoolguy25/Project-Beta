using System;
using System.Collections.Generic;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Retention.Client {
    /// <summary>
    /// The panel the challenges key (or the level chip) opens: the player's level and XP, today's
    /// challenges with progress and rewards, the reset countdown, the streak and the next unlock.
    /// Rows are copies of an inactive template. Built by the retention HUD authoring.
    /// </summary>
    [RequireComponent(typeof(HudPopup))]
    public sealed class RetentionChallengesPanel : MonoBehaviour {
        [Header("Level")]
        public Image levelDisc;
        public TMP_Text levelLabel;
        public TMP_Text levelXpLabel;
        public HudBar levelBar;
        [Header("Challenges")]
        public TMP_Text resetLabel;
        [Tooltip("Inactive row copied for each challenge.")]
        public RetentionChallengeRow rowTemplate;
        [Header("Streak and next unlock")]
        public Image streakIcon;
        public TMP_Text streakLabel;
        public GameObject nextUnlockGroup;
        public Image nextUnlockDisc;
        [Tooltip("The glyph on the disc; its sprite follows the unlock.")]
        public Image nextUnlockIcon;
        public TMP_Text nextUnlockTitle;
        public TMP_Text nextUnlockDescription;
        [Tooltip("Shown when the player's progress is not being saved this session.")]
        public GameObject notSavedGroup;
        public Button closeButton;
        public TMP_Text keyLabel;

        [Header("Text")]
        [Tooltip("{0} = level.")]
        public string levelFormat = "Level {0}";
        [Tooltip("{0} = XP in the level, {1} = XP the level takes.")]
        public string levelXpFormat = "{0} / {1} XP";
        public string maxLevelText = "Max level";
        [Tooltip("Reset countdown with {0} hours and {1:00} minutes (TMP SetText format).")]
        public string resetHoursFormat = "New in {0}h {1:00}m";
        [Tooltip("Reset countdown under an hour: {0} minutes and {1:00} seconds.")]
        public string resetMinutesFormat = "New in {0}m {1:00}s";
        public string resettingText = "New challenges...";
        [Tooltip("{0} = days, {1} = XP of tomorrow's bonus.")]
        public string streakClaimedFormat = "Day {0} streak! Come back tomorrow for +{1} XP";
        [Tooltip("{0} = days, {1} = XP of today's bonus.")]
        public string streakPendingFormat = "Day {0} streak: finish a match today for +{1} XP";
        [Tooltip("{0} = XP of day 1's bonus.")]
        public string streakNoneFormat = "Finish a match today to start a streak (+{0} XP)";
        [Tooltip("{0} = level, {1} = unlock title.")]
        public string nextUnlockFormat = "Level {0}: {1}";

        readonly List<RetentionChallengeRow> rows = new();
        HudPopup popup;
        int shownCountdown = int.MinValue;

        public HudPopup Popup => popup != null ? popup : popup = GetComponent<HudPopup>();
        public bool Visible => Popup.Visible;

        public void SetKey(string key) {
            if (keyLabel != null) keyLabel.text = key;
        }

        public void SetStyle(RetentionHudStyle style) {
            if (levelDisc != null) levelDisc.color = style.xp;
            if (levelBar != null) levelBar.SetColor(style.xp);
            if (streakIcon != null) streakIcon.color = style.streak;
            if (nextUnlockDisc != null) nextUnlockDisc.color = style.unlock;
        }

        /// <summary>Fills everything from a snapshot (cheap enough for every change, not for every frame).</summary>
        public void Bind(in RetentionSnapshotBroadcast snapshot, RetentionIcons icons, RetentionHudStyle style) {
            SetLevel(snapshot.level, snapshot.levelXp, snapshot.levelXpNeeded);

            RetentionChallengeView[] challenges = snapshot.challenges ?? Array.Empty<RetentionChallengeView>();
            EnsureRows(challenges.Length);
            for (int i = 0; i < rows.Count; i++) {
                bool used = i < challenges.Length;
                if (rows[i].gameObject.activeSelf != used) rows[i].gameObject.SetActive(used);
                if (used) rows[i].Set(challenges[i], style);
            }

            if (streakLabel != null) {
                streakLabel.text = snapshot.streakDays <= 0 ? Format(streakNoneFormat, snapshot.streakNextXp)
                    : Format(snapshot.streakClaimedToday ? streakClaimedFormat : streakPendingFormat, snapshot.streakDays, snapshot.streakNextXp);
            }

            if (nextUnlockGroup != null) nextUnlockGroup.SetActive(snapshot.hasNextUnlock);
            if (snapshot.hasNextUnlock) {
                RetentionUnlockView next = snapshot.nextUnlock;
                if (nextUnlockIcon != null && icons != null) nextUnlockIcon.sprite = icons.Get(next.icon);
                if (nextUnlockTitle != null) nextUnlockTitle.text = Format(nextUnlockFormat, next.level, next.title);
                if (nextUnlockDescription != null) nextUnlockDescription.text = next.description ?? string.Empty;
            }
            if (notSavedGroup != null) notSavedGroup.SetActive(snapshot.notSaved);
        }

        public void SetLevel(int level, int levelXp, int levelXpNeeded) {
            if (levelLabel != null) levelLabel.text = Format(levelFormat, level);
            if (levelXpLabel != null) levelXpLabel.text = levelXpNeeded > 0 ? Format(levelXpFormat, levelXp, levelXpNeeded) : maxLevelText;
            if (levelBar != null) levelBar.Value = levelXpNeeded > 0 ? (float)levelXp / levelXpNeeded : 1f;
        }

        public void SetChallenge(int index, in RetentionChallengeView challenge, RetentionHudStyle style) {
            if (index >= 0 && index < rows.Count && rows[index].gameObject.activeSelf) rows[index].Set(challenge, style);
        }

        /// <summary>Updates the reset countdown; allocation-free, so call it every frame while the panel shows.</summary>
        public void SetCountdown(int seconds) {
            if (resetLabel == null || seconds == shownCountdown) return;
            int previous = shownCountdown;
            shownCountdown = seconds;
            if (seconds <= 0) resetLabel.text = resettingText;
            else if (seconds >= 3600) {
                // Hours and minutes only change once a minute.
                if (previous / 60 != seconds / 60 || previous <= 0) resetLabel.SetText(resetHoursFormat, (float)(seconds / 3600), seconds / 60 % 60);
            } else resetLabel.SetText(resetMinutesFormat, (float)(seconds / 60), seconds % 60);
        }

        public void Show() => Popup.Show();
        public void Hide() => Popup.Hide();

        void Awake() {
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        }

        void EnsureRows(int count) {
            if (rowTemplate == null) return;
            while (rows.Count < count) {
                RetentionChallengeRow row = Instantiate(rowTemplate, rowTemplate.transform.parent);
                row.name = $"Challenge {rows.Count + 1}";
                row.transform.SetSiblingIndex(rowTemplate.transform.GetSiblingIndex() + rows.Count + 1);
                rows.Add(row);
            }
        }

        static string Format(string format, params object[] args) {
            try {
                return string.Format(format ?? string.Empty, args);
            } catch (FormatException) {
                return format;
            }
        }
    }
}
