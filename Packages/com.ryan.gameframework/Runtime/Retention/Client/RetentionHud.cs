using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.UI.GameHud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RyanAssets.Retention.Client {
    /// <summary>
    /// Client side of the retention kit: receives the retention broadcasts and drives the level chip,
    /// the challenges panel (the <see cref="challengesInput"/> action, J by default, or a click on the
    /// chip), the toasts and the end-of-match summary card. Lives on the root of the generated
    /// RetentionHUD prefab; a game places that prefab in its HUD and positions the parts with the
    /// placement fields.
    /// <para>
    /// Default layout: the level chip bottom-left with the challenges panel above it, the summary card
    /// at the left edge's centre, and the toasts top-right under the top bar, below the shared player
    /// list (room for four players). The right edge's centre
    /// is left to the games' own feeds (kill feed, tag feed), and the card and the toasts never overlap.
    /// </para>
    /// <para>
    /// Hide it with <see cref="SetHidden"/> during cutscenes and menus (never deactivate it, or it
    /// misses broadcasts): toasts and the summary that arrive while hidden wait until it shows again.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RetentionHud : MonoBehaviour {
        /// <summary>Most toasts kept while hidden; older ones are dropped.</summary>
        const int MaxQueuedToasts = 4;

        readonly struct QueuedToast {
            public readonly Sprite Icon;
            public readonly Color Color;
            public readonly string Text;

            public QueuedToast(Sprite icon, Color color, string text) {
                Icon = icon;
                Color = color;
                Text = text;
            }
        }

        [Header("Parts")]
        [SerializeField] RetentionLevelChip levelChip;
        [SerializeField] RetentionChallengesPanel challengesPanel;
        [SerializeField] HudToastFeed toasts;
        [SerializeField] RetentionSummaryCard summaryCard;
        [SerializeField] RetentionIcons icons;

        [Header("Layout (applied in Awake)")]
        [SerializeField] bool showLevelChip = true;
        [SerializeField] RetentionPlacement levelChipPlacement = new(new Vector2(0f, 0f), new Vector2(16f, 16f));
        [SerializeField] RetentionPlacement challengesPanelPlacement = new(new Vector2(0f, 0f), new Vector2(16f, 66f));
        [Tooltip("Top right, under the top bar and below the shared player list (room for four players; the authoring sets the offset from the theme).")]
        [SerializeField] RetentionPlacement toastPlacement = new(new Vector2(1f, 1f), new Vector2(-12f, -260f));
        [Tooltip("Left edge, centred: clear of the right-centre column the games' feeds use.")]
        [SerializeField] RetentionPlacement summaryPlacement = new(new Vector2(0f, 0.5f), new Vector2(24f, 0f));

        [Header("Input")]
        [Tooltip("Opens and closes the challenges panel. J is free in every game as of 2026-11; add a gamepad binding here if a game wants one.")]
        [SerializeField] InputAction challengesInput = new("Challenges", InputActionType.Button, "<Keyboard>/j");

        [Header("Look and feel")]
        [SerializeField] RetentionHudStyle style = new();
        [SerializeField] RetentionHudFeel feel = new();
        [SerializeField] Sprite levelUpIcon;
        [SerializeField] Sprite challengeIcon;
        [SerializeField] Sprite streakIcon;
        [SerializeField] Sprite warningIcon;

        [Header("Text")]
        [Tooltip("{0} = challenge text, {1} = XP.")]
        [SerializeField] string challengeCompleteFormat = "Challenge complete: {0}  +{1} XP";
        [Tooltip("{0} = level.")]
        [SerializeField] string levelUpFormat = "Level {0} reached!";
        [Tooltip("{0} = level (centre text).")]
        [SerializeField] string levelUpCenterFormat = "LEVEL {0}";
        [Tooltip("{0} = unlock title.")]
        [SerializeField] string unlockFormat = "Unlocked: {0}";
        [Tooltip("Greeting on join with a live streak: {0} = streak days, {1} = XP today's first match adds, {2} = the day it makes.")]
        [SerializeField] string streakGreetingFormat = "{0}-day streak! Play a match today to make it day {2} (+{1} XP)";
        [Tooltip("Greeting on join without a streak: {1} = XP of day 1's bonus.")]
        [SerializeField] string streakStartFormat = "Play a match today to start a daily streak (+{1} XP)";
        [Tooltip("Greeting on join after a streak lapsed: {1} = XP of day 1's bonus, {3} = the best streak.")]
        [SerializeField] string streakLapsedFormat = "Your streak ended. Start a new one today (+{1} XP, best {3} days)";
        [SerializeField] string notSavedText = "Your progress could not be loaded; nothing is saved this session.";

        [Header("Network")]
        [Tooltip("Seconds between snapshot requests while none has arrived.")]
        [SerializeField, Min(0.5f)] float snapshotRetrySeconds = 2f;

        readonly Queue<QueuedToast> queuedToasts = new();
        CanvasGroup group;
        ClientManager manager;
        RetentionSnapshotBroadcast snapshot;
        RetentionSummaryBroadcast pendingSummary;
        bool registered, hasSnapshot, needsSnapshot = true, greeted, hidden, hasPendingSummary, holdChipLevel;
        int heldLevel;
        float heldFraction;
        float snapshotTime, nextRequestTime, summaryAt;

        /// <summary>The player's level in this game (1 until the first snapshot).</summary>
        public int Level => hasSnapshot ? snapshot.level : 1;
        /// <summary>The last full state received; false until the first one arrives.</summary>
        public bool HasSnapshot => hasSnapshot;
        public RetentionSnapshotBroadcast Snapshot => snapshot;
        public bool Hidden => hidden;
        public bool ChallengesOpen => challengesPanel != null && challengesPanel.Visible;
        /// <summary>Whether the end-of-match summary card is on screen (games make room for it).</summary>
        public bool SummaryVisible => summaryCard != null && summaryCard.Visible;

        /// <summary>Raised when the player's level changes (and on the first snapshot), e.g. to refresh locked items in a shop.</summary>
        public event Action<int> LevelChanged;
        /// <summary>Raised when a match summary arrives (before it shows).</summary>
        public event Action<RetentionSummaryBroadcast> SummaryReceived;

        // ---------------------------------------------------------------- public controls

        /// <summary>
        /// Hides (or shows again) the whole retention HUD, for cutscenes, menus and game-over screens.
        /// Toasts and summaries received while hidden show when it is shown again.
        /// </summary>
        public void SetHidden(bool value) {
            if (hidden == value) return;
            hidden = value;
            ApplyVisibility();
            if (hidden) {
                ShowChallenges(false);
                return;
            }
            while (queuedToasts.Count > 0) {
                QueuedToast toast = queuedToasts.Dequeue();
                Toast(toast.Icon, toast.Color, toast.Text);
            }
        }

        public void ToggleChallenges() => ShowChallenges(!ChallengesOpen);

        public void ShowChallenges(bool show) {
            if (challengesPanel == null) return;
            if (show && (hidden || (summaryCard != null && summaryCard.Visible))) return;
            if (show) {
                if (hasSnapshot) challengesPanel.Bind(snapshot, icons, style);
                challengesPanel.Show();
            } else challengesPanel.Hide();
        }

        /// <summary>Moves one part of the HUD (at runtime, or from a game's HUD authoring on its instance).</summary>
        public void SetPlacement(RetentionHudPart part, RetentionPlacement placement) {
            switch (part) {
                case RetentionHudPart.LevelChip: levelChipPlacement = placement; break;
                case RetentionHudPart.ChallengesPanel: challengesPanelPlacement = placement; break;
                case RetentionHudPart.Toasts: toastPlacement = placement; break;
                case RetentionHudPart.SummaryCard: summaryPlacement = placement; break;
                default: throw new ArgumentOutOfRangeException(nameof(part), part, null);
            }
            ApplyPlacement();
        }

        public RetentionPlacement GetPlacement(RetentionHudPart part) => part switch {
            RetentionHudPart.LevelChip => levelChipPlacement,
            RetentionHudPart.ChallengesPanel => challengesPanelPlacement,
            RetentionHudPart.Toasts => toastPlacement,
            RetentionHudPart.SummaryCard => summaryPlacement,
            _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
        };

        /// <summary>Shows or hides the always-on level chip (the panel, toasts and summary still work).</summary>
        public void SetLevelChipVisible(bool visible) {
            showLevelChip = visible;
            if (levelChip != null) levelChip.gameObject.SetActive(visible);
        }

        /// <summary>Re-applies the placement fields to the parts.</summary>
        [ContextMenu("Apply Placement")]
        public void ApplyPlacement() {
            if (levelChip != null) levelChipPlacement.ApplyTo((RectTransform)levelChip.transform);
            if (challengesPanel != null) challengesPanelPlacement.ApplyTo((RectTransform)challengesPanel.transform);
            if (toasts != null) toastPlacement.ApplyTo((RectTransform)toasts.transform);
            if (summaryCard != null) summaryPlacement.ApplyTo((RectTransform)summaryCard.transform);
        }

        // ---------------------------------------------------------------- lifecycle

        void Awake() {
            // Prefabs built before kit try 3 saved this root at scale 0 (a root canvas's driven scale
            // with no screen). Nested under a game's canvas that hides the whole HUD, so undo it.
            if (transform.parent != null && transform.localScale == Vector3.zero) transform.localScale = Vector3.one;
            group = GetComponent<CanvasGroup>();
            ApplyPlacement();
            string key = KeyName();
            if (levelChip != null) {
                levelChip.SetStyle(style);
                levelChip.SetKey(key);
                levelChip.gameObject.SetActive(showLevelChip);
                if (levelChip.button != null) levelChip.button.onClick.AddListener(ToggleChallenges);
            }
            if (challengesPanel != null) {
                challengesPanel.SetStyle(style);
                challengesPanel.SetKey(key);
                if (challengesPanel.closeButton != null) challengesPanel.closeButton.onClick.AddListener(CloseChallenges);
            }
            if (summaryCard != null) {
                summaryCard.FinalLevelShown += ReleaseChipLevel;
                summaryCard.LevelShown += OnCardLevel;
            }
            ApplyVisibility();
        }

        void OnDestroy() {
            if (summaryCard != null) {
                summaryCard.FinalLevelShown -= ReleaseChipLevel;
                summaryCard.LevelShown -= OnCardLevel;
            }
            if (levelChip != null && levelChip.button != null) levelChip.button.onClick.RemoveListener(ToggleChallenges);
            if (challengesPanel != null && challengesPanel.closeButton != null) challengesPanel.closeButton.onClick.RemoveListener(CloseChallenges);
        }

        void OnEnable() {
            // Broadcasts sent while disabled were missed: ask for the full state again.
            needsSnapshot = true;
            challengesInput?.Enable();
            Register();
        }

        void OnDisable() {
            challengesInput?.Disable();
            Unregister();
        }

        string KeyName() {
            if (challengesInput == null || challengesInput.bindings.Count == 0) return string.Empty;
            return challengesInput.GetBindingDisplayString(0);
        }

        void Register() {
            if (registered) return;
            manager = InstanceFinder.ClientManager;
            if (manager == null) return;
            manager.RegisterBroadcast<RetentionSnapshotBroadcast>(OnSnapshot);
            manager.RegisterBroadcast<RetentionXpBroadcast>(OnXp);
            manager.RegisterBroadcast<RetentionLevelUpBroadcast>(OnLevelUp);
            manager.RegisterBroadcast<RetentionChallengeBroadcast>(OnChallenge);
            manager.RegisterBroadcast<RetentionProgressBroadcast>(OnProgress);
            manager.RegisterBroadcast<RetentionSummaryBroadcast>(OnSummary);
            registered = true;
        }

        void Unregister() {
            if (!registered) return;
            registered = false;
            if (manager == null) return;
            manager.UnregisterBroadcast<RetentionSnapshotBroadcast>(OnSnapshot);
            manager.UnregisterBroadcast<RetentionXpBroadcast>(OnXp);
            manager.UnregisterBroadcast<RetentionLevelUpBroadcast>(OnLevelUp);
            manager.UnregisterBroadcast<RetentionChallengeBroadcast>(OnChallenge);
            manager.UnregisterBroadcast<RetentionProgressBroadcast>(OnProgress);
            manager.UnregisterBroadcast<RetentionSummaryBroadcast>(OnSummary);
            manager = null;
        }

        void Update() {
            if (!registered) Register();
            float now = Time.unscaledTime;
            RequestSnapshotIfNeeded(now);
            ReadKey();
            if (hasSnapshot && challengesPanel != null && challengesPanel.Visible) challengesPanel.SetCountdown(SecondsToReset(now));
            if (hasPendingSummary && !hidden && now >= summaryAt) ShowSummary();
            // The card was closed (or never shown) before it reached the final level.
            if (holdChipLevel && !hasPendingSummary && (summaryCard == null || !summaryCard.Visible)) ReleaseChipLevel();
        }

        int SecondsToReset(float now) => snapshot.secondsToReset - (int)(now - snapshotTime);

        void RequestSnapshotIfNeeded(float now) {
            // The server also pushes a snapshot at midnight; asking covers a missed one.
            if (hasSnapshot && SecondsToReset(now) < 0) needsSnapshot = true;
            if (!needsSnapshot || manager == null || !manager.Started || now < nextRequestTime) return;
            nextRequestTime = now + snapshotRetrySeconds;
            manager.Broadcast(new RetentionSnapshotRequest());
        }

        void ReadKey() {
            if (challengesInput == null || !challengesInput.WasPressedThisFrame()) return;
            // The character map is off while typing in chat or a menu is open.
            if (InputService.Instance != null && !InputService.GetActionMapActive(RyanAssetsActionMap.Character)) return;
            ToggleChallenges();
        }

        void CloseChallenges() => ShowChallenges(false);

        void ApplyVisibility() {
            if (group == null) return;
            group.alpha = hidden ? 0f : 1f;
            group.blocksRaycasts = group.interactable = !hidden;
        }

        // ---------------------------------------------------------------- broadcasts

        void OnSnapshot(RetentionSnapshotBroadcast message, Channel channel) {
            int previousLevel = hasSnapshot ? snapshot.level : 0;
            snapshot = message;
            snapshot.challenges ??= Array.Empty<RetentionChallengeView>();
            hasSnapshot = true;
            needsSnapshot = false;
            snapshotTime = Time.unscaledTime;
            RefreshAll();
            if (!greeted) Greet();
            if (snapshot.level != previousLevel) LevelChanged?.Invoke(snapshot.level);
        }

        void OnXp(RetentionXpBroadcast message, Channel channel) {
            int previousLevel = snapshot.level;
            snapshot.level = message.level;
            snapshot.levelXp = message.levelXp;
            snapshot.levelXpNeeded = message.levelXpNeeded;
            snapshot.totalXp = message.totalXp;
            RefreshLevel();
            if (message.announce && !hidden && levelChip != null && showLevelChip) {
                levelChip.ShowGain(message.xp, feel.xpGainSeconds);
                GameSfx.Play(feel.xpGain, feel.xpGainVolume);
            }
            if (hasSnapshot && message.level != previousLevel) LevelChanged?.Invoke(message.level);
        }

        void OnLevelUp(RetentionLevelUpBroadcast message, Channel channel) {
            snapshot.hasNextUnlock = message.hasNextUnlock;
            snapshot.nextUnlock = message.nextUnlock;
            if (challengesPanel != null && hasSnapshot) challengesPanel.Bind(snapshot, icons, style);
            Toast(levelUpIcon, style.levelUp, Format(levelUpFormat, message.level));
            if (message.unlocks != null)
                foreach (RetentionUnlockView unlock in message.unlocks)
                    Toast(icons != null ? icons.Get(unlock.icon) : null, style.unlock, Format(unlockFormat, unlock.title));
            if (hidden) return;
            GameSfx.Play(message.unlocks != null && message.unlocks.Length > 0 ? feel.unlock : feel.levelUp);
            if (levelChip != null) levelChip.Bump(feel.levelPunch);
            if (feel.levelUpCenterText) ScreenFx.ShowText(Format(levelUpCenterFormat, message.level), style.levelUp, feel.levelUpCenterSeconds, feel.levelUpCenterSize);
        }

        void OnProgress(RetentionProgressBroadcast message, Channel channel) {
            RetentionChallengeView[] challenges = snapshot.challenges;
            if (!hasSnapshot || message.day != snapshot.challengeDay || challenges == null || message.index < 0 || message.index >= challenges.Length) {
                needsSnapshot = true; // another day's list: resync
                return;
            }
            challenges[message.index].progress = message.progress;
            if (challengesPanel != null) challengesPanel.SetChallenge(message.index, challenges[message.index], style);
        }

        void OnChallenge(RetentionChallengeBroadcast message, Channel channel) {
            RetentionChallengeView[] challenges = snapshot.challenges;
            if (hasSnapshot && message.day == snapshot.challengeDay && challenges != null && message.index >= 0 && message.index < challenges.Length && challenges[message.index].id == message.id) {
                challenges[message.index].progress = message.progress;
                challenges[message.index].done |= message.completed;
                if (challengesPanel != null) challengesPanel.SetChallenge(message.index, challenges[message.index], style);
                RefreshChallengeCount();
            } else {
                needsSnapshot = true; // a different day's list: resync
            }
            if (!message.completed) return;
            Toast(challengeIcon, style.challenge, Format(challengeCompleteFormat, message.text, message.xpReward));
            if (hidden) return;
            GameSfx.Play(feel.challengeComplete);
            if (levelChip != null) levelChip.Bump(feel.levelPunch);
        }

        void OnSummary(RetentionSummaryBroadcast message, Channel channel) {
            SummaryReceived?.Invoke(message);
            // The match's XP arrives just before its summary: keep the chip on the level the match
            // started at, so it does not give the card's level-up away, until the card gets there.
            // A game that grants XP during the match already showed some of it on the chip; the chip
            // never steps back below what it shows, and follows the card's level-ups from there.
            if (summaryCard != null) {
                holdChipLevel = true;
                heldLevel = message.levelBefore;
                heldFraction = message.levelXpNeededBefore > 0 ? Mathf.Clamp01((float)message.levelXpBefore / message.levelXpNeededBefore) : 1f;
                if (levelChip != null && levelChip.ShownLevel > heldLevel && levelChip.ShownLevel <= message.levelAfter) {
                    heldLevel = levelChip.ShownLevel;
                    heldFraction = levelChip.ShownFraction;
                }
                RefreshLevel();
            }
            pendingSummary = message;
            hasPendingSummary = true;
            summaryAt = Time.unscaledTime + feel.summaryDelaySeconds;
        }

        // ---------------------------------------------------------------- views

        void ShowSummary() {
            hasPendingSummary = false;
            if (summaryCard == null) return;
            ShowChallenges(false);
            summaryCard.Play(pendingSummary, icons, style, feel);
        }

        void Greet() {
            greeted = true;
            if (snapshot.notSaved) Toast(warningIcon, style.warning, notSavedText);
            if (snapshot.streakClaimedToday) return;
            // A nudge for everyone with today's bonus unclaimed: new players and lapsed streaks most of all.
            string format = snapshot.streakDays > 0 ? streakGreetingFormat
                : snapshot.bestStreak > 0 ? streakLapsedFormat
                : streakStartFormat;
            Toast(streakIcon, style.streak, Format(format, snapshot.streakDays, snapshot.streakNextXp, snapshot.streakDays + 1, snapshot.bestStreak));
        }

        void RefreshAll() {
            RefreshLevel();
            if (levelChip != null) levelChip.SetStreak(snapshot.streakDays);
            RefreshChallengeCount();
            if (challengesPanel != null) challengesPanel.Bind(snapshot, icons, style);
        }

        void RefreshLevel() {
            float fraction = snapshot.levelXpNeeded > 0 ? (float)snapshot.levelXp / snapshot.levelXpNeeded : 1f;
            if (levelChip != null) {
                if (holdChipLevel) levelChip.SetLevel(heldLevel, heldFraction);
                else levelChip.SetLevel(snapshot.level, fraction);
            }
            if (challengesPanel != null) challengesPanel.SetLevel(snapshot.level, snapshot.levelXp, snapshot.levelXpNeeded);
        }

        /// <summary>The summary card crossed into <paramref name="level"/>: the held chip keeps up with it.</summary>
        void OnCardLevel(int level) {
            if (!holdChipLevel || level <= heldLevel) return;
            heldLevel = level;
            heldFraction = 0f;
            RefreshLevel();
            if (levelChip != null && !hidden) levelChip.Bump(feel.levelPunch);
        }

        /// <summary>Lets the chip show the current level again (the card reached it, or closed).</summary>
        void ReleaseChipLevel() {
            if (!holdChipLevel) return;
            holdChipLevel = false;
            bool gained = snapshot.level > heldLevel;
            RefreshLevel();
            if (gained && levelChip != null && !hidden) levelChip.Bump(feel.levelPunch);
        }

        void RefreshChallengeCount() {
            if (levelChip == null || snapshot.challenges == null) return;
            int done = 0;
            foreach (RetentionChallengeView challenge in snapshot.challenges)
                if (challenge.done) done++;
            levelChip.SetChallenges(done, snapshot.challenges.Length);
        }

        void Toast(Sprite icon, Color color, string text) {
            if (hidden) {
                if (queuedToasts.Count >= MaxQueuedToasts) queuedToasts.Dequeue();
                queuedToasts.Enqueue(new QueuedToast(icon, color, text));
                return;
            }
            if (toasts == null) return;
            toasts.Push(icon, color, text, feel.toastSeconds);
            // A long toast wraps onto more lines; the text measures its height against the width the
            // first layout pass gives it, so lay the feed out twice to get the row height right.
            var feed = (RectTransform)toasts.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(feed);
            LayoutRebuilder.ForceRebuildLayoutImmediate(feed);
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
