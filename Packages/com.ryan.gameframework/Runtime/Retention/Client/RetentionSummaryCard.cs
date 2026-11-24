using System;
using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Retention.Client {
    /// <summary>
    /// The end-of-match card: reward lines tick in, the XP bar fills from the match's start (bursting
    /// at every level-up), unlocks are revealed one by one, then the card waits and hides itself. The
    /// close button skips the animation first, then closes. Built by the retention HUD authoring;
    /// <see cref="RetentionHud"/> calls <see cref="Play"/>.
    /// <para>
    /// The card's size is settled when it starts: every line and the unlock block are laid out at once
    /// and revealed by fading (each row has a CanvasGroup), so the title and the emblem never move
    /// while it animates.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(HudPopup))]
    public sealed class RetentionSummaryCard : MonoBehaviour {
        enum Phase { Idle, Lines, Fill, Unlocks, Done }

        const int LabelCell = 0, GoldCell = 1, XpCell = 2;
        /// <summary>Smallest size a long label shrinks to, as a share of its size (two lines still fit a row).</summary>
        const float LabelMinScale = 0.82f;
        /// <summary>No label shrinks below this (the theme's small text size), so it stays readable.</summary>
        const float LabelMinSize = 13f;
        /// <summary>A label that fits on one line at this share of its size stays on one line.</summary>
        const float OneLineMinScale = 0.9f;
        /// <summary>Height of a row whose label wraps, as a share of the normal row height.</summary>
        const float TallRowScale = 1.5f;

        public TMP_Text title;
        [Header("Level")]
        public RectTransform levelEmblem;
        public Image levelDisc;
        public TMP_Text levelLabel;
        public HudBar bar;
        public TMP_Text barLabel;
        public TMP_Text totalLabel;
        public GameObject totalGoldGroup;
        public TMP_Text totalGoldLabel;
        [Header("Lines")]
        [Tooltip("Cells: label, gold, XP.")]
        public HudRows lines;
        [Tooltip("Icon of a line that pays a reward (null keeps the row template's).")]
        public Sprite rewardIcon;
        [Tooltip("Icon of an info line (no reward, only a detail such as \"3 / 3\").")]
        public Sprite infoIcon;
        [Header("Unlock reveal")]
        public HudPopup unlockPopup;
        [Tooltip("Old faint outline where the unlock appears. Unused since try 5: the block shows a locked teaser until the reveal.")]
        public GameObject unlockPlaceholder;
        public Image unlockDisc;
        public Image unlockIcon;
        public TMP_Text unlockKicker;
        public TMP_Text unlockTitle;
        public TMP_Text unlockDescription;
        [Header("Footer")]
        public GameObject streakGroup;
        public Image streakIcon;
        public TMP_Text streakLabel;
        public GameObject nextUnlockGroup;
        public Image nextUnlockIcon;
        public TMP_Text nextUnlockLabel;
        public Button closeButton;

        [Header("Text")]
        public string defaultTitle = "Match complete";
        [Tooltip("{0} = reward label, {1} = how many were merged.")]
        public string mergedLineFormat = "{0} x{1}";
        [Tooltip("A line with a detail and a reward: {0} = label, {1} = detail (rich text; TextMeshPro has no </alpha>).")]
        public string detailLineFormat = "{0}  <size=85%><alpha=#99>{1}</size>";
        public string unlockKickerText = "UNLOCKED";
        [Tooltip("{0} = which unlock, {1} = how many this match.")]
        public string unlockKickerManyFormat = "UNLOCKED {0}/{1}";
        [Tooltip("Kicker of the locked teaser shown until the reveal, for one unlock.")]
        public string unlockTeaserKicker = "NEW UNLOCK";
        [Tooltip("Kicker of the locked teaser for several unlocks: {0} = how many.")]
        public string unlockTeaserKickerManyFormat = "{0} NEW UNLOCKS";
        [Tooltip("Title of the locked teaser: {0} = level of the first unlock.")]
        public string unlockTeaserTitleFormat = "Reach level {0}";
        [Tooltip("Icon of the locked teaser (empty: the theme's \"lock\" icon).")]
        public Sprite unlockTeaserIcon;
        [Tooltip("Footer under a match that paid the streak (the bonus itself is a line): {0} = streak day, {1} = tomorrow's day.")]
        public string streakFormat = "Come back tomorrow for day {1} of your streak!";
        [Tooltip("{0} = level, {1} = unlock title.")]
        public string nextUnlockFormat = "Next: Level {0} - {1}";
        [Tooltip("{0} = XP in the level, {1} = XP the level takes.")]
        public string levelXpFormat = "{0} / {1} XP";
        public string maxLevelText = "Max level";

        RetentionSummaryBroadcast summary;
        RetentionIcons icons;
        RetentionHudStyle style;
        RetentionHudFeel feel;
        HudPopup popup;
        Phase phase;
        float nextAt, pauseUntil, hideAt;
        int revealed, levelsGained, crossed, unlockIndex, shownXp, shownGold, confettiIndex;
        float startFraction, distance, travelled, fillSpeed;
        SfxLadder lineTicks;
        SfxCue lineTicksCue;
        CanvasGroup[] rowGroups = Array.Empty<CanvasGroup>();

        public HudPopup Popup => popup != null ? popup : popup = GetComponent<HudPopup>();
        /// <summary>True from <see cref="Play"/> until the card has hidden.</summary>
        public bool Visible => Popup.Visible;
        /// <summary>Raised when the card reaches the match's final level (after the fill, or on skip).</summary>
        public event Action FinalLevelShown;

        void Awake() {
            if (closeButton != null) closeButton.onClick.AddListener(OnClose);
        }

        void OnDestroy() {
            if (closeButton != null) closeButton.onClick.RemoveListener(OnClose);
        }

        /// <summary>Shows the card for <paramref name="message"/> and starts its animation.</summary>
        public void Play(in RetentionSummaryBroadcast message, RetentionIcons iconSet, RetentionHudStyle hudStyle, RetentionHudFeel hudFeel) {
            summary = message;
            icons = iconSet;
            style = hudStyle;
            feel = hudFeel;
            if (lineTicks == null || lineTicksCue != feel.summaryLine) {
                lineTicksCue = feel.summaryLine;
                lineTicks = new SfxLadder(lineTicksCue);
            }
            lineTicks.Reset();
            Popup.Show();

            if (title != null) title.text = string.IsNullOrEmpty(summary.title) ? defaultTitle : summary.title;
            if (levelDisc != null) levelDisc.color = style.xp;
            if (bar != null) bar.SetColor(style.xp);
            SetLevel(summary.levelBefore);
            startFraction = Fraction(summary.levelXpBefore, summary.levelXpNeededBefore);
            levelsGained = Mathf.Max(0, summary.levelAfter - summary.levelBefore);
            // Reaching the maximum level ends on the level-up itself (the bar stays full): no extra lap.
            float endFraction = levelsGained > 0 && summary.levelXpNeededAfter <= 0 ? 0f : Fraction(summary.levelXpAfter, summary.levelXpNeededAfter);
            distance = levelsGained == 0 ? Mathf.Max(0f, endFraction - startFraction) : 1f - startFraction + (levelsGained - 1) + endFraction;
            float seconds = Mathf.Max(feel.minFillSeconds, distance * feel.secondsPerLevel);
            fillSpeed = distance > 0f ? distance / seconds : 0f;
            travelled = 0f;
            crossed = 0;
            shownXp = -1;
            if (bar != null) bar.Value = startFraction;
            if (barLabel != null) barLabel.text = string.Empty;
            SetCounter(0);
            shownGold = -1;
            if (totalGoldGroup != null) totalGoldGroup.SetActive(summary.totalGold > 0);
            SetGold(0);

            FillLines();
            FillFooter();
            ReserveUnlock();
            revealed = 0;
            unlockIndex = 0;
            pauseUntil = 0f;
            phase = Phase.Lines;
            nextAt = Time.unscaledTime + feel.lineInterval;
        }

        /// <summary>Jumps to the end of the animation (the final state shows, effects are skipped).</summary>
        public void Skip() {
            if (phase is Phase.Idle or Phase.Done) return;
            int count = lines != null ? lines.Count : 0;
            for (; revealed < count; revealed++) SetRowShown(revealed, true);
            pauseUntil = 0f;
            if (levelsGained > 0 && levelDisc != null) levelDisc.color = style.levelUp;
            if (bar != null) bar.Value = Fraction(summary.levelXpAfter, summary.levelXpNeededAfter);
            ShowFinalLevel();
            RetentionUnlockView[] unlocks = summary.unlocks;
            if (unlocks != null && unlocks.Length > 0) {
                unlockIndex = Mathf.Clamp(unlockIndex, 1, unlocks.Length);
                ShowUnlock(unlockIndex - 1, false);
            }
            Finish();
        }

        public void Hide() {
            phase = Phase.Idle;
            Popup.Hide();
        }

        void OnClose() {
            if (phase is Phase.Done or Phase.Idle) Hide();
            else Skip();
        }

        void Update() {
            float now = Time.unscaledTime;
            switch (phase) {
                case Phase.Lines: UpdateLines(now); break;
                case Phase.Fill: UpdateFill(now); break;
                case Phase.Unlocks: UpdateUnlocks(now); break;
                case Phase.Done:
                    if (feel.autoHideSeconds > 0f && now >= hideAt) Hide();
                    else CycleUnlocks(now);
                    break;
            }
        }

        // ---------------------------------------------------------------- phases

        void UpdateLines(float now) {
            int count = lines != null ? lines.Count : 0;
            if (revealed >= count) {
                phase = Phase.Fill;
                return;
            }
            if (now < nextAt) return;
            SetRowShown(revealed, true);
            Punch.Pop(lines.Row(revealed).transform, feel.linePopFrom, feel.punchSeconds);
            lineTicks.Play();
            revealed++;
            nextAt = now + feel.lineInterval;
        }

        void UpdateFill(float now) {
            if (now < pauseUntil) return;
            if (fillSpeed <= 0f) {
                EndFill();
                return;
            }
            travelled = Mathf.Min(distance, travelled + Time.unscaledDeltaTime * fillSpeed);
            float position = startFraction + travelled;
            int boundary = crossed + 1;
            if (crossed < levelsGained && position >= boundary) {
                // Stop on the full bar for a beat, then carry on from empty in the next level.
                travelled = boundary - startFraction;
                crossed++;
                if (bar != null) bar.Value = 1f;
                SetCounters(travelled / distance);
                LevelReached();
                pauseUntil = now + feel.levelUpPause;
                return;
            }
            if (bar != null) bar.Value = Mathf.Clamp01(position - crossed);
            SetCounters(travelled / distance);
            if (travelled >= distance) EndFill();
        }

        void EndFill() {
            if (bar != null && summary.levelXpNeededAfter <= 0) bar.Value = 1f;
            ShowFinalLevel();
            bool unlocks = summary.unlocks != null && summary.unlocks.Length > 0;
            phase = unlocks ? Phase.Unlocks : Phase.Done;
            nextAt = Time.unscaledTime;
            if (!unlocks) Finish();
        }

        void UpdateUnlocks(float now) {
            if (now < nextAt) return;
            if (unlockIndex >= summary.unlocks.Length) {
                Finish();
                return;
            }
            ShowUnlock(unlockIndex, true);
            unlockIndex++;
            nextAt = now + feel.unlockSeconds;
        }

        /// <summary>After the reveal, several unlocks keep taking turns (quietly) until the card hides.</summary>
        void CycleUnlocks(float now) {
            RetentionUnlockView[] unlocks = summary.unlocks;
            if (unlocks == null || unlocks.Length < 2 || now < nextAt) return;
            unlockIndex = unlockIndex % unlocks.Length + 1;
            ShowUnlock(unlockIndex - 1, false);
            if (unlockIcon != null) Punch.Pop(unlockIcon.rectTransform, feel.unlockPopFrom, feel.punchSeconds);
            nextAt = now + feel.unlockSeconds;
        }

        void ShowFinalLevel() {
            SetCounter(summary.totalXp);
            SetGold(summary.totalGold);
            SetLevel(summary.levelAfter);
            if (barLabel != null)
                barLabel.text = summary.levelXpNeededAfter > 0 ? Format(levelXpFormat, summary.levelXpAfter, summary.levelXpNeededAfter) : maxLevelText;
            FinalLevelShown?.Invoke();
        }

        void Finish() {
            phase = Phase.Done;
            hideAt = Time.unscaledTime + feel.autoHideSeconds;
            nextAt = Time.unscaledTime + feel.unlockSeconds;
        }

        // ---------------------------------------------------------------- pieces

        void LevelReached() {
            SetLevel(summary.levelBefore + crossed);
            if (levelDisc != null) levelDisc.color = style.levelUp;
            if (levelEmblem != null) Punch.Scale(levelEmblem, feel.levelPunch, feel.punchSeconds);
            GameSfx.Play(feel.levelUp);
            Confetti();
        }

        void ShowUnlock(int index, bool celebrate) {
            if (unlockPlaceholder != null) unlockPlaceholder.SetActive(false);
            if (unlockPopup != null) unlockPopup.Show();
            if (unlockDescription != null) unlockDescription.alpha = 1f;
            FillUnlock(index);
            if (!celebrate) return;
            if (unlockIcon != null) Punch.Pop(unlockIcon.rectTransform, feel.unlockPopFrom, feel.punchSeconds);
            GameSfx.Play(feel.unlock);
            Confetti();
        }

        void FillUnlock(int index) {
            RetentionUnlockView unlock = summary.unlocks[index];
            if (unlockDisc != null) unlockDisc.color = style.unlock;
            if (unlockIcon != null && icons != null) unlockIcon.sprite = icons.Get(unlock.icon);
            if (unlockKicker != null)
                unlockKicker.text = summary.unlocks.Length > 1 ? Format(unlockKickerManyFormat, index + 1, summary.unlocks.Length) : unlockKickerText;
            if (unlockTitle != null) unlockTitle.text = unlock.title;
            if (unlockDescription != null) unlockDescription.text = unlock.description ?? string.Empty;
        }

        /// <summary>
        /// Lays the unlock block out now, with the longest description of the match, so its reveal does
        /// not resize the card, and shows it as a locked teaser ("3 NEW UNLOCKS", "Reach level 5") until
        /// the bar gets there: never an empty box. Without unlocks it takes no space.
        /// </summary>
        void ReserveUnlock() {
            if (unlockPopup == null) return;
            RetentionUnlockView[] unlocks = summary.unlocks;
            bool any = unlocks != null && unlocks.Length > 0;
            unlockPopup.deactivateWhenHidden = false;
            unlockPopup.gameObject.SetActive(any);
            if (unlockPlaceholder != null) unlockPlaceholder.SetActive(false);
            if (!any) return;
            int longest = 0;
            for (int i = 1; i < unlocks.Length; i++)
                if ((unlocks[i].description?.Length ?? 0) > (unlocks[longest].description?.Length ?? 0)) longest = i;
            FillUnlock(longest);
            // The teaser: the same block, locked. The description keeps its space but is not shown yet.
            if (unlockDisc != null) unlockDisc.color = style.muted;
            if (unlockIcon != null) {
                Sprite locked = unlockTeaserIcon != null ? unlockTeaserIcon : icons != null ? icons.Get("lock") : null;
                if (locked != null) unlockIcon.sprite = locked;
            }
            if (unlockKicker != null)
                unlockKicker.text = unlocks.Length > 1 ? Format(unlockTeaserKickerManyFormat, unlocks.Length) : unlockTeaserKicker;
            if (unlockTitle != null) unlockTitle.text = Format(unlockTeaserTitleFormat, unlocks[0].level);
            if (unlockDescription != null) unlockDescription.alpha = 0f;
            unlockPopup.Show();
        }

        void FillLines() {
            if (lines == null) return;
            RetentionRewardView[] rewards = summary.lines ?? Array.Empty<RetentionRewardView>();
            lines.SetCount(rewards.Length);
            if (rowGroups.Length < rewards.Length) Array.Resize(ref rowGroups, rewards.Length);
            for (int i = 0; i < rewards.Length; i++) {
                RetentionRewardView reward = rewards[i];
                HudRow row = lines.Row(i);
                bool hasReward = reward.xp > 0 || reward.gold > 0;
                bool hasDetail = !string.IsNullOrEmpty(reward.detail);
                bool info = hasDetail && !hasReward;
                string label = reward.count > 1 ? Format(mergedLineFormat, reward.label, reward.count) : reward.label;
                // A reward line shows its detail after the label; an info line shows it where the XP would be.
                // (TextMeshPro has no </alpha> tag; older formats had one, and it showed as text.)
                if (hasDetail && hasReward) label = Format(detailLineFormat, label, reward.detail).Replace("</alpha>", string.Empty);
                row.Set(LabelCell, label);
                row.Set(GoldCell, reward.gold > 0 ? $"+{reward.gold}" : string.Empty, style.gold);
                if (info) row.Set(XpCell, reward.detail, style.muted);
                else row.Set(XpCell, reward.xp > 0 ? $"+{reward.xp} XP" : string.Empty, style.xp);
                TMP_Text[] cells = row.cells;
                if (cells != null && cells.Length > XpCell) {
                    if (cells[LabelCell] != null) FitLabel(cells[LabelCell]);
                    // An info line's detail may run on, right-aligned, over the empty gold cell.
                    if (cells[XpCell] != null) cells[XpCell].overflowMode = info ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
                    // A reward line without gold gives the gold cell's width to its label.
                    if (cells[GoldCell] != null) cells[GoldCell].gameObject.SetActive(!(hasReward && reward.gold <= 0));
                }
                Sprite icon = info ? infoIcon : rewardIcon;
                if (icon != null && row.icon != null) {
                    row.icon.sprite = icon;
                    row.icon.color = info ? style.muted : style.levelUp;
                }
                if (rowGroups[i] == null) row.TryGetComponent(out rowGroups[i]);
                SetRowShown(i, false);
            }
            FitRowHeights();
        }

        /// <summary>
        /// A label too long for one line ("Find the first hider of a round 2 times" next to a gold value)
        /// gets a taller row, so it wraps to two lines at a readable size instead of two tiny ones. Done
        /// once when the card starts (rows are revealed by fading), so the card does not resize later.
        /// </summary>
        void FitRowHeights() {
            if (lines == null || lines.Count == 0) return;
            float baseHeight = RowHeight(lines.template);
            if (baseHeight <= 0f) return;
            for (int i = 0; i < lines.Count; i++) SetRowHeight(lines.Row(i), baseHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            bool changed = false;
            for (int i = 0; i < lines.Count; i++) {
                HudRow row = lines.Row(i);
                TMP_Text cell = row.cells != null && row.cells.Length > LabelCell ? row.cells[LabelCell] : null;
                if (cell == null || string.IsNullOrEmpty(cell.text)) continue;
                float width = cell.rectTransform.rect.width;
                // Slightly long labels stay on one line, a little smaller (auto-size); longer ones wrap.
                if (width <= 0f || OneLineWidth(cell) * OneLineMinScale <= width + 0.5f) continue;
                SetRowHeight(row, Mathf.Max(baseHeight, baseHeight * TallRowScale));
                changed = true;
            }
            if (changed) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        /// <summary>Width of the label on one line at its full size.</summary>
        static float OneLineWidth(TMP_Text cell) {
            bool auto = cell.enableAutoSizing;
            float size = cell.fontSize;
            cell.enableAutoSizing = false;
            if (auto) cell.fontSize = cell.fontSizeMax;
            float width = cell.GetPreferredValues(cell.text, float.PositiveInfinity, float.PositiveInfinity).x;
            cell.fontSize = size;
            cell.enableAutoSizing = auto;
            return width;
        }

        static float RowHeight(HudRow row) =>
            row != null && row.TryGetComponent(out LayoutElement size) ? size.preferredHeight : -1f;

        static void SetRowHeight(HudRow row, float height) {
            if (!row.TryGetComponent(out LayoutElement size) || Mathf.Approximately(size.preferredHeight, height)) return;
            size.minHeight = size.preferredHeight = size.maxHeight = height;
        }

        /// <summary>
        /// Lets a long label ("Hold 8 waves without a leak") wrap to a second line and shrink to fit the
        /// row before it is cut with an ellipsis. Short labels keep their size. A label the prefab already
        /// auto-sizes is left as authored.
        /// </summary>
        static void FitLabel(TMP_Text cell) {
            if (cell.enableAutoSizing) return;
            float size = cell.fontSize;
            cell.textWrappingMode = TextWrappingModes.Normal;
            cell.overflowMode = TextOverflowModes.Ellipsis;
            cell.fontSizeMax = size;
            cell.fontSizeMin = Mathf.Min(size, Mathf.Max(LabelMinSize, Mathf.Round(size * LabelMinScale)));
            cell.lineSpacing = -14f;
            cell.enableAutoSizing = true;
        }

        /// <summary>Hides or shows a line without changing the layout (fades its CanvasGroup; rows without one fall back to deactivating).</summary>
        void SetRowShown(int index, bool shown) {
            CanvasGroup group = index < rowGroups.Length ? rowGroups[index] : null;
            if (group != null) group.alpha = shown ? 1f : 0f;
            else lines.Row(index).gameObject.SetActive(shown);
        }

        void FillFooter() {
            bool streak = summary.streakDays > 0;
            if (streakGroup != null) streakGroup.SetActive(streak);
            if (streakIcon != null) streakIcon.color = style.streak;
            if (streak && streakLabel != null) streakLabel.text = Format(streakFormat, summary.streakDays, summary.streakDays + 1);

            if (nextUnlockGroup != null) nextUnlockGroup.SetActive(summary.hasNextUnlock);
            if (!summary.hasNextUnlock) return;
            if (nextUnlockIcon != null) {
                nextUnlockIcon.color = style.unlock;
                if (icons != null) nextUnlockIcon.sprite = icons.Get(summary.nextUnlock.icon);
            }
            if (nextUnlockLabel != null) nextUnlockLabel.text = Format(nextUnlockFormat, summary.nextUnlock.level, summary.nextUnlock.title);
        }

        void SetLevel(int level) {
            if (levelLabel != null) levelLabel.SetText("{0}", level);
        }

        /// <summary>Counts the XP total and the gold total up together, by how far the fill has run.</summary>
        void SetCounters(float progress) {
            SetCounter(Mathf.RoundToInt(summary.totalXp * progress));
            SetGold(Mathf.RoundToInt(summary.totalGold * progress));
        }

        void SetGold(int gold) {
            if (gold == shownGold || totalGoldLabel == null) return;
            shownGold = gold;
            totalGoldLabel.SetText("+{0}", gold);
        }

        void SetCounter(int xp) {
            if (xp == shownXp || totalLabel == null) return;
            shownXp = xp;
            totalLabel.SetText("+{0} XP", xp);
        }

        void Confetti() {
            Camera cam = Camera.main;
            Vector2[] points = feel.confettiPoints;
            if (cam == null || points == null || points.Length == 0) return;
            for (int i = 0; i < feel.confettiBursts; i++) {
                Vector2 p = points[confettiIndex++ % points.Length];
                GameFx.Play(FxKind.Confetti, cam.ViewportToWorldPoint(new Vector3(p.x, p.y, feel.confettiDepth)), feel.confettiScale);
            }
        }

        static float Fraction(int levelXp, int levelXpNeeded) => levelXpNeeded > 0 ? Mathf.Clamp01((float)levelXp / levelXpNeeded) : 1f;

        static string Format(string format, params object[] args) {
            try {
                return string.Format(format ?? string.Empty, args);
            } catch (FormatException) {
                return format;
            }
        }
    }
}
