using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Platformer;
using RyanAssets.Retention.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Builds the course the server announces and presents the race: HUD, race strip, effects and the
    /// sky around the course. Decides nothing; every rule lives on the server.
    /// </summary>
    public sealed class OB_ClientController : MonoBehaviour {
        [Header("Stage card")]
        public TMP_Text stageLabel;
        public TMP_Text stageNameLabel, hintLabel;
        [Tooltip("Chip naming the current difficulty tier, coloured with the tier's accent.")]
        public TMP_Text tierLabel;
        public Image tierChip;
        [Tooltip("Seconds a stage hint stays up before fading out.")]
        [Min(0f)] public float hintSeconds = 7f;
        [Min(0.01f)] public float hintFadeSeconds = 0.8f;
        // Appended fields.
        [Tooltip("Fades the hint and its backing together (falls back to the label's alpha when empty).")]
        public CanvasGroup hintGroup;
        [Tooltip("Dark pill behind the hint, fitted to its text so it reads over a bright sky.")]
        public RectTransform hintBacking;
        [Tooltip("Room around the hint text inside its backing, in pixels (x: sides, y: top and bottom).")]
        public Vector2 hintBackingPadding = new(14f, 8f);
        [Tooltip("Seconds between checks of where the local player is: which segment the stage card names and the fall-warning height.")]
        [Min(0.02f)] public float trackSeconds = 0.2f;
        [Tooltip("Course progress bar fill; its anchor max x is set to the fraction of checkpoints reached.")]
        public RectTransform progressFill;
        public RectTransform stageCard;
        [Tooltip("Seconds the stage progress bar takes to catch up.")]
        [Min(0.01f)] public float progressSmoothing = 0.25f;

        [Header("Race strip")]
        [Tooltip("Other racers' dots on the progress track and your place, with pass pops.")]
        public OB_RaceStrip raceStrip;

        [Header("Race status")]
        public TMP_Text timerLabel;
        public TMP_Text statusLabel, coinsLabel, leaderLabel;
        [Tooltip("Dark pill behind the status line, shown while a split is up and fitted to its text.")]
        public RectTransform statusBacking;
        [Tooltip("Horizontal room around the split text inside its pill.")]
        [Min(0f)] public float statusBackingPadding = 32f;
        public RectTransform coinPill;
        [Tooltip("Stars collected in the current tier, out of the tier's stars (hidden on tiers without stars).")]
        public TMP_Text starsLabel;
        public RectTransform starPill;
        [Tooltip("Seconds a checkpoint split (and its difference to the gold medal pace) replaces the clock status.")]
        [Min(0f)] public float splitSeconds = 4f;

        [Header("Announcements")]
        public TMP_Text toastLabel;
        public RectTransform toast;
        public CanvasGroup toastGroup;
        public Image toastBackground;
        public TMP_Text centerLabel;
        [Min(0.1f)] public float toastSeconds = 2.2f;
        [Min(0.01f)] public float toastFadeInSeconds = 0.08f;
        [Min(0.01f)] public float toastFadeOutSeconds = 0.25f;
        [Range(0f, 1f)] public float toastOpacity = 0.9f;

        [Header("Results")]
        public GameObject resultsPanel;
        public TMP_Text resultsBody;
        public TMP_Text resultsFooter;
        [Min(1f)] public float resultsSeconds = 9f;
        [Tooltip("While the retention summary card is on screen the results board docks smaller toward the bottom right, " +
                 "so the card and the finish pad stay in view: its scale and its offset from where it normally stands.")]
        [Range(0.3f, 1f)] public float resultsDockedScale = 0.7f;
        public Vector2 resultsDockedOffset = new(330f, -170f);
        [Tooltip("Seconds the board takes to move between docked and normal.")]
        [Min(0.01f)] public float resultsDockSeconds = 0.25f;
        [Tooltip("Most rows the board lists. With more racers it lists the leaders and always your own row.")]
        [Min(1)] public int resultsMaxRows = 8;
        [Tooltip("Height of the board around its rows (header, margins and footer); the rows add their own height.")]
        [Min(0f)] public float resultsFrameHeight = 236f;
        [Tooltip("Longest racer name shown in full; longer names are cut with \"..\".")]
        [Min(4)] public int resultsNameLength = 16;

        [Header("Retention")]
        [Tooltip("The shared levels / challenges / summary HUD (nested in this HUD by OB_Authoring).")]
        public RetentionHud retentionHud;
        [Tooltip("Level unlocks: trails, star radar, gold flags, finish fireworks.")]
        public OB_Cosmetics cosmetics;

        [Header("Presentation")]
        public OB_Fx fx;
        public OB_PlayerFeel feel;
        public OB_Backdrop backdrop;
        public OB_Atmosphere atmosphere;
        [Tooltip("Parkour moves and ragdoll for the local player; hazards knock the character limp.")]
        public CharacterParkour parkour;
        [Tooltip("The opt-in Dash: the obby turns it on while it is shown (and the sprint key stops sprinting).")]
        public CharacterDash dash;
        [Tooltip("Velocity (m/s, along up) a hazard flings the character with before the respawn.")]
        [Min(0f)] public float hazardFling = 11f;
        [Tooltip("Random sideways part (m/s) of the hazard fling.")]
        [Min(0f)] public float hazardFlingSideways = 3f;

        [Header("Pops")]
        [Min(0.01f)] public float stagePopSeconds = 0.35f;
        [Tooltip("Scale the stage card pops in from when the stage changes.")]
        public float stagePopFrom = 0.92f;
        [Min(0.01f)] public float coinPopSeconds = 0.3f;
        public float coinPopFrom = 1.35f;
        [Min(0.01f)] public float toastPopSeconds = 0.4f;
        public float toastPopFrom = 0.6f;
        [Min(0.01f)] public float centerPopSeconds = 0.45f;
        public float centerPopFrom = 1.8f;

        OB_Course course;
        ClientManager manager;
        // The course is keyed on seed, stage count and rules: the same seed with another count or other
        // rules is another course, so any of them changing rebuilds.
        int builtSeed, builtStages, builtRulesHash, wantedSeed, wantedStages, wantedRulesHash;
        bool BuiltWanted => builtSeed == wantedSeed && builtStages == wantedStages && builtRulesHash == wantedRulesHash;
        float wantedCoinBonus, wantedGoldPerStage, wantedStarBonus, wantedGoldFactor;
        OB_ProgressBroadcast progress;
        bool hasProgress;
        OB_RoundBroadcast round;
        float roundReceivedAt, toastShownAt, toastUntil, resultsUntil, nextRequest, splitUntil, nextViewClock;
        /// <summary>Seconds between view-clock samples (the server judges lasers and crushers at the moment this client saw).</summary>
        const float ViewClockInterval = 0.2f;
        float progressShown, progressTarget, progressVelocity, hintShownAt;
        int hintStage = -1;
        // Mechanics whose hint this session already showed, and the hint text for the current stage part ("" = none).
        ulong hintsShown;
        string hintText = "";
        OB_StageKind hintKind;
        // The segment of the current stage the local player is on (named on the stage card).
        int segmentStage = -1, shownSegment;
        float nextTrack;
        OB_CourseLayout indexedLayout;
        readonly List<List<int>> piecesByStage = new();
        readonly List<OB_Volume> pieceVolumes = new();
        string splitText = "";
        // What the clock and status line show, so their strings are only rebuilt when it changes.
        int shownClockTenths = -1;
        readonly char[] clockChars = new char[16];
        string shownStatusKey;
        int shownStatusSeconds = -1;
        readonly StringBuilder rows = new();
        // Where the results board normally stands, and how far it has moved toward its docked place (0 to 1).
        Vector2 resultsHome;
        Vector3 resultsHomeScale;
        float resultsDock;

        void Awake() {
            course = GetComponentInParent<OB_Course>();
            resultsPanel.SetActive(false);
            var board = (RectTransform)resultsPanel.transform;
            resultsHome = board.anchoredPosition;
            resultsHomeScale = board.localScale;
            toastLabel.text = "";
            toastGroup.alpha = 0f;
            centerLabel.text = "";
            stageLabel.text = "SKY OBBY";
            stageNameLabel.text = "Connecting...";
            hintLabel.text = "";
            tierLabel.text = "";
            tierChip.enabled = false;
            coinsLabel.text = "0";
            if (starPill != null) starPill.gameObject.SetActive(false);
            timerLabel.text = "0:00.0";
            statusLabel.text = "";
            leaderLabel.text = "";
            SetProgress(0f, true);
            if (backdrop != null && course != null) backdrop.Arrange(null, course.LobbySpawn);
        }

        void OnEnable() {
            RisingLava.ChaseStarted += OnLavaChase;
            if (dash != null) dash.SetEnabled(true);
        }

        void OnDisable() {
            RisingLava.ChaseStarted -= OnLavaChase;
            if (dash != null) dash.SetEnabled(false);
        }

        void OnLavaChase(RisingLava lava) => Toast("THE LAVA IS RISING!  <size=75%><alpha=#CC>CLIMB!</size>", OB_Palette.Lava, 2.5f);

        void OnDestroy() {
            if (manager != null) {
                manager.UnregisterBroadcast<OB_CourseBroadcast>(OnCourse);
                manager.UnregisterBroadcast<OB_ProgressBroadcast>(OnProgress);
                manager.UnregisterBroadcast<OB_RoundBroadcast>(OnRound);
                manager.UnregisterBroadcast<OB_ResultsBroadcast>(OnResults);
                manager.UnregisterBroadcast<OB_StandingsBroadcast>(OnStandings);
            }
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<OB_CourseBroadcast>(OnCourse);
                manager.RegisterBroadcast<OB_ProgressBroadcast>(OnProgress);
                manager.RegisterBroadcast<OB_RoundBroadcast>(OnRound);
                manager.RegisterBroadcast<OB_ResultsBroadcast>(OnResults);
                manager.RegisterBroadcast<OB_StandingsBroadcast>(OnStandings);
            }
            if (manager != null && manager.Started && builtSeed == 0 && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new OB_CourseRequest { knownSeed = builtSeed });
            }
            if (manager != null && manager.Started && manager.Connection.IsAuthenticated && Time.unscaledTime > nextViewClock) {
                nextViewClock = Time.unscaledTime + ViewClockInterval;
                manager.Broadcast(new OB_ViewClock { time = PlatformerClock.Now }, Channel.Unreliable);
            }
            if (resultsPanel.activeSelf && Time.unscaledTime > resultsUntil) resultsPanel.SetActive(false);
            DockResults(false);
            RefreshTimer();
            AnimateHud();
            TrackLocalPlayer();
        }

        void AnimateHud() {
            float fadeOut = Mathf.Clamp01((toastUntil - Time.unscaledTime) / toastFadeOutSeconds);
            float fadeIn = Mathf.Clamp01((Time.unscaledTime - toastShownAt) / toastFadeInSeconds);
            toastGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            // Hint: shown when a stage starts, then fades so it stays out of the way.
            float hint = 1f - Mathf.Clamp01((Time.unscaledTime - hintShownAt - hintSeconds) / hintFadeSeconds);
            if (hintGroup != null) hintGroup.alpha = hint;
            else hintLabel.alpha = hint;
            progressShown = Mathf.SmoothDamp(progressShown, progressTarget, ref progressVelocity, progressSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
            SetProgress(progressShown, false);
        }

        void SetProgress(float fraction, bool snap) {
            if (snap) progressShown = progressTarget = fraction;
            if (progressFill == null) return;
            var max = progressFill.anchorMax;
            max.x = Mathf.Clamp01(progressShown);
            progressFill.anchorMax = max;
        }

        bool Racing => round.phase is OB_Phase.Racing or OB_Phase.Closing;

        // ---------------------------------------------------------------- course and progress
        void OnCourse(OB_CourseBroadcast message, Channel channel) {
            wantedSeed = message.seed;
            wantedStages = message.stageCount;
            wantedRulesHash = message.rulesHash;
            wantedCoinBonus = message.coinBonus;
            wantedGoldPerStage = message.goldPerStage;
            wantedStarBonus = message.starBonus;
            wantedGoldFactor = message.goldFactor;
            if (course == null || message.seed == 0 || BuiltWanted) return;
            int localRules = (course.rules ?? OB_CourseRules.Default).Hash();
            if (message.rulesHash != 0 && message.rulesHash != localRules)
                Debug.LogError($"[Obby] The server built course {message.seed} from other rules (hash {message.rulesHash:x8}, this client {localRules:x8}): " +
                               "the course shown here will not match the server's. Client and server must ship the same OB_Course rules.");
            if (cosmetics != null) cosmetics.ShowStars(null, 0);
            course.Build(message.seed, message.stageCount);
            builtSeed = message.seed;
            builtStages = message.stageCount;
            builtRulesHash = message.rulesHash;
            hasProgress = false;
            splitUntil = 0f;
            if (raceStrip != null) raceStrip.Clear();
            if (fx != null) {
                fx.CoinBonus = wantedCoinBonus;
                fx.StarBonus = wantedStarBonus;
                fx.RegisterCourse(course);
            }
            if (cosmetics != null) cosmetics.ShowStars(course.Layout, 0);
            resultsPanel.SetActive(false);
            SetProgress(0f, true);
            if (backdrop != null) backdrop.Arrange(course.Layout, course.LobbySpawn);
            RefreshStage();
        }

        /// <summary>Builds the announced course if a message for it arrived before the course itself was built.</summary>
        void CatchUp(int seed, Channel channel) {
            if (seed == wantedSeed && wantedSeed != 0 && !BuiltWanted)
                OnCourse(new OB_CourseBroadcast {
                    seed = wantedSeed, stageCount = wantedStages, coinBonus = wantedCoinBonus, goldPerStage = wantedGoldPerStage,
                    starBonus = wantedStarBonus, goldFactor = wantedGoldFactor, rulesHash = wantedRulesHash
                }, channel);
        }

        void OnProgress(OB_ProgressBroadcast message, Channel channel) {
            CatchUp(message.seed, channel);
            if (message.seed != builtSeed) return;
            int previous = hasProgress ? progress.checkpoint : 0;
            OB_Tier tierBefore = TierAt(previous);
            progress = message;
            hasProgress = true;
            ApplyMarkers(message.reason == OB_ProgressReason.Checkpoint, message.reason == OB_ProgressReason.Coin,
                message.reason == OB_ProgressReason.Star);
            RefreshStage();
            int total = course.Layout.StageCount;
            switch (message.reason) {
                case OB_ProgressReason.Checkpoint: {
                    OB_Tier tier = TierAt(message.checkpoint);
                    bool newTier = tier > tierBefore;
                    Color accent = OB_Themes.Get(tier).accent;
                    if (newTier)
                        Toast($"{OB_Tiers.Name(tier).ToUpperInvariant()}  <size=75%><alpha=#CC>{OB_Tiers.Theme(tier)}</size>", accent, 3f);
                    else
                        // Same numbering as the stage card: reaching checkpoint 3 starts stage 4.
                        Toast($"CHECKPOINT  <size=75%><alpha=#CC>STAGE {message.checkpoint + 1} / {total}</size>", OB_Palette.Go);
                    if (fx != null) fx.Checkpoint(newTier, accent, course.Layout.stages[message.checkpoint].spawn);
                    // A flip pad holds until the next one: a flip never carries into the next stage.
                    if (CharacterMovement.Local != null && CharacterMovement.Local.Gravity != null) CharacterMovement.Local.Gravity.Clear();
                    ShowSplit(message);
                    ShowTierSplit(message);
                    Punch.Pop(stageCard, stagePopFrom, stagePopSeconds);
                    break;
                }
                case OB_ProgressReason.Coin:
                    Punch.Pop(coinPill, coinPopFrom, coinPopSeconds);
                    break;
                case OB_ProgressReason.Star: {
                    int got = OB_Scoring.CountCoins(message.stars);
                    Toast($"STAR!  <size=75%><alpha=#CC>{got} / {course.Layout.StarCount} ON THIS COURSE</size>", OB_Palette.Star, 2.5f);
                    if (starPill != null) Punch.Pop(starPill, coinPopFrom, coinPopSeconds);
                    if (cosmetics != null) cosmetics.ShowStars(course.Layout, message.stars);
                    break;
                }
                case OB_ProgressReason.Finish:
                    // No medal is not gold: a plain finish gets the "go" green.
                    Toast(FinishToast(message), message.medal == OB_Medal.None ? OB_Palette.Go : OB_Palette.Medal(message.medal), 5f);
                    Punch.Pop(stageCard, stagePopFrom, stagePopSeconds);
                    if (fx != null) fx.Finish(course.Layout.finishTop, message.personalBest);
                    if (cosmetics != null) cosmetics.Finish(course.Layout.finishTop);
                    break;
                case OB_ProgressReason.Hazard:
                    Toast("Ouch! Back to your checkpoint.", OB_Palette.Lava, 1.5f);
                    if (fx != null) fx.Hazard();
                    // Yelp off the hazard limp; the server respawns the character a moment later.
                    if (parkour != null && CharacterMovement.Local != null) {
                        Vector3 up = CharacterMovement.Local.Up;
                        Vector3 sideways = Vector3.ProjectOnPlane(Random.onUnitSphere, up) * hazardFlingSideways;
                        parkour.Tumble(up * hazardFling + sideways);
                    }
                    break;
                case OB_ProgressReason.Fell:
                    Toast("Back to your checkpoint.", OB_Palette.InkSoft, 1.5f);
                    if (fx != null) fx.Fell();
                    break;
                case OB_ProgressReason.OutOfBounds:
                    Toast("Out of bounds. Back to your checkpoint.", OB_Palette.InkSoft, 1.5f);
                    if (fx != null) fx.Fell();
                    break;
            }
        }

        static string FinishToast(in OB_ProgressBroadcast message) {
            var text = new StringBuilder();
            text.Append(message.medal != OB_Medal.None ? message.medal.ToString().ToUpperInvariant() : "FINISHED");
            text.Append("  ").Append(OB_Scoring.Clock(message.scoredTime, true));
            float bonus = message.finishTime - message.scoredTime;
            if (bonus > 0.05f) text.Append($"  <size=70%>{(message.stars != 0 ? "coins + stars" : "coins")} -{bonus:0.0}s</size>");
            if (message.personalBest) text.Append("  <size=70%>NEW BEST!</size>");
            return text.ToString();
        }

        /// <summary>
        /// Shows the split for a checkpoint under the clock against the gold medal pace: the gold factor
        /// times the par of the stages done (coins and stars count, as in the medal).
        /// </summary>
        void ShowSplit(in OB_ProgressBroadcast message) {
            if (message.raceTime <= 0f || splitSeconds <= 0f) return;
            string clock = OB_Scoring.Clock(message.raceTime, true);
            if (wantedGoldFactor > 0f) {
                float scored = OB_Scoring.ScoredTime(message.raceTime, OB_Scoring.CountCoins(message.coins), wantedCoinBonus,
                    OB_Scoring.CountCoins(message.stars), wantedStarBonus);
                float delta = OB_Scoring.GoldPaceDelta(scored, wantedGoldFactor * course.Layout.ParBefore(message.checkpoint));
                Color color = delta <= 0f ? OB_Palette.Go : OB_Palette.Lava;
                splitText = $"{clock}  <color=#{ColorUtility.ToHtmlStringRGB(color)}>{(delta <= 0f ? "-" : "+")}{Mathf.Abs(delta):0.0}</color> <size=80%><alpha=#AA>vs gold</size>";
            } else {
                splitText = $"split {clock}";
            }
            splitUntil = Time.unscaledTime + splitSeconds;
            shownStatusKey = null;
        }

        /// <summary>When a checkpoint closes a tier, the line under the clock shows the tier's time against the saved best instead.</summary>
        void ShowTierSplit(in OB_ProgressBroadcast message) {
            if (message.tierTime <= 0f || splitSeconds <= 0f) return;
            string time = OB_Scoring.Clock(message.tierTime, true);
            string best = message.tierBest <= 0f ? "<alpha=#AA>first clear"
                : message.tierTime < message.tierBest ? $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Go)}>NEW BEST</color>"
                : $"<alpha=#AA>best {OB_Scoring.Clock(message.tierBest, true)}";
            splitText = $"{OB_Tiers.Name(message.tier)} {time}  <size=80%>{best}</size>";
            splitUntil = Time.unscaledTime + splitSeconds * 1.5f;
            shownStatusKey = null;
        }

        // ---------------------------------------------------------------- round
        void OnRound(OB_RoundBroadcast message, Channel channel) {
            round = message;
            roundReceivedAt = Time.unscaledTime;
            if (course != null) course.SetGateClosed(message.phase is OB_Phase.Waiting or OB_Phase.Countdown or OB_Phase.Results);
            CatchUp(message.seed, channel);
            string center = message.phase switch {
                OB_Phase.Countdown => message.secondsLeft.ToString(),
                OB_Phase.Racing when message.raceTime < 1.5f => "GO!",
                _ => ""
            };
            if (center != centerLabel.text) {
                bool go = center == "GO!";
                centerLabel.text = center;
                centerLabel.color = go ? OB_Palette.Go : OB_Palette.Paper;
                if (center.Length > 0) {
                    Punch.Pop(centerLabel.rectTransform, centerPopFrom, centerPopSeconds);
                    if (fx != null) {
                        if (go) fx.Go();
                        else fx.CountdownTick();
                    }
                }
            }
            // The top bar already carries the headline and the results board the field, so this line stays empty.
            leaderLabel.text = "";
            if (raceStrip != null) raceStrip.Refresh(LocalClientId, StageCount, Racing || message.phase == OB_Phase.Results);
        }

        int StageCount => course != null && course.Layout != null ? course.Layout.StageCount : 0;

        // ---------------------------------------------------------------- standings
        void OnStandings(OB_StandingsBroadcast message, Channel channel) {
            CatchUp(message.seed, channel);
            if (message.seed != builtSeed || message.racers == null || raceStrip == null) return;
            raceStrip.Apply(message, LocalClientId, StageCount, Racing);
        }

        static int LocalClientId {
            get {
                var connection = InstanceFinder.ClientManager != null ? InstanceFinder.ClientManager.Connection : null;
                return connection != null ? connection.ClientId : -1;
            }
        }

        // ---------------------------------------------------------------- results
        void OnResults(OB_ResultsBroadcast message, Channel channel) {
            var entries = message.entries ?? System.Array.Empty<OB_ResultEntry>();
            int me = LocalClientId, mine = -1;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].clientId == me) mine = i;
            // Too many racers for the board: the leaders, a line counting the rows left out, then your own
            // row in its real place (so the board never grows past resultsMaxRows lines).
            int listed = Mathf.Min(entries.Length, resultsMaxRows);
            bool appendMine = mine >= resultsMaxRows;
            if (appendMine) listed = Mathf.Max(1, resultsMaxRows - 2);
            rows.Clear();
            for (int i = 0; i < listed; i++) AppendRow(entries[i], i + 1, i == mine);
            if (appendMine) {
                int skipped = mine - listed;
                if (skipped > 0) rows.Append($"<alpha=#88><pos=10%>... {skipped} more<alpha=#FF>\n");
                AppendRow(entries[mine], mine + 1, true);
            }
            resultsBody.text = rows.Length > 0 ? rows.ToString() : "No racers.";
            resultsFooter.text = MedalTargets(message);
            // The board is as tall as its rows, so one racer is not an empty board and eight still fit.
            var board = (RectTransform)resultsPanel.transform;
            float height = resultsFrameHeight + resultsBody.GetPreferredValues(resultsBody.text, resultsBody.rectTransform.rect.width, 0f).y;
            board.sizeDelta = new Vector2(board.sizeDelta.x, height);
            resultsPanel.SetActive(true);
            DockResults(true);
            // The board repeats your finish line, so the finish toast makes way for it.
            toastUntil = Mathf.Min(toastUntil, Time.unscaledTime);
            resultsUntil = Time.unscaledTime + resultsSeconds;
            if (raceStrip != null) raceStrip.Refresh(me, StageCount, true);
        }

        // The retention summary card and the results board arrive together at the end of a race. While the card
        // is up the board steps aside (smaller, toward the bottom right) so the two never stack on top of each
        // other; it grows back to its place once the card closes.
        void DockResults(bool snap) {
            if (!resultsPanel.activeSelf) {
                resultsDock = 0f;
                return;
            }
            float target = retentionHud != null && retentionHud.SummaryVisible ? 1f : 0f;
            resultsDock = snap ? target : Mathf.MoveTowards(resultsDock, target, Time.unscaledDeltaTime / resultsDockSeconds);
            float eased = resultsDock * resultsDock * (3f - 2f * resultsDock);
            var board = (RectTransform)resultsPanel.transform;
            board.anchoredPosition = resultsHome + resultsDockedOffset * eased;
            board.localScale = resultsHomeScale * Mathf.Lerp(1f, resultsDockedScale, eased);
        }

        void AppendRow(in OB_ResultEntry entry, int place, bool mine) {
            // Podium colours only for finishers: a racer who ran out of time is placed, but never shown as a gold "1st".
            string rank = ColorUtility.ToHtmlStringRGB(!entry.finished ? OB_Palette.Paper : place switch {
                1 => OB_Palette.Gold,
                2 => OB_Palette.Silver,
                3 => OB_Palette.Bronze,
                _ => OB_Palette.Paper
            });
            string name = string.IsNullOrEmpty(entry.name) ? "?" : entry.name;
            if (name.Length > resultsNameLength) name = name.Substring(0, resultsNameLength - 2) + "..";
            rows.Append($"<color=#{rank}><b>{OB_Scoring.Ordinal(place)}</b></color><pos=10%>");
            if (mine) rows.Append($"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Gold)}>{name}</color>");
            else rows.Append(name);
            rows.Append("<pos=48%>");
            if (entry.finished) {
                rows.Append(OB_Scoring.Clock(entry.scoredTime, true));
                float bonus = entry.time - entry.scoredTime;
                if (bonus > 0.05f) rows.Append($" <size=70%><alpha=#99>-{bonus:0.0}s<alpha=#FF></size>");
            } else {
                rows.Append($"<alpha=#99>stage {Mathf.Min(entry.stage + 1, StageCount)}<alpha=#FF>");
            }
            if (entry.medal != OB_Medal.None)
                rows.Append($"<pos=70%><color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Medal(entry.medal))}>• {entry.medal.ToString().ToUpperInvariant()}</color>");
            rows.Append($"<pos=82%><color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Gold)}>{entry.coins}</color> <size=75%>{(entry.coins == 1 ? "coin" : "coins")}</size>");
            if (entry.stars > 0) rows.Append($"<pos=93%><color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Star)}>{entry.stars}</color> <size=75%>{(entry.stars == 1 ? "star" : "stars")}</size>");
            rows.Append('\n');
        }

        static string MedalTargets(in OB_ResultsBroadcast message) {
            if (message.goldTime <= 0f) return "";
            string Medal(OB_Medal medal, float seconds) =>
                $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Medal(medal))}>• {medal.ToString().ToUpperInvariant()}</color> {OB_Scoring.Clock(seconds)}";
            string bonus = message.coinBonus <= 0f ? ""
                : message.starBonus > 0f ? $"     Coins take {message.coinBonus:0.0#} s off your time, stars {message.starBonus:0.0#} s."
                : $"     Each coin takes {message.coinBonus:0.0#} s off your time.";
            return $"{Medal(OB_Medal.Gold, message.goldTime)}   {Medal(OB_Medal.Silver, message.silverTime)}   {Medal(OB_Medal.Bronze, message.bronzeTime)}{bonus}";
        }

        // ---------------------------------------------------------------- course markers and HUD
        void ApplyMarkers(bool celebrate, bool coinCollected, bool starCollected) {
            if (course == null || course.Layout == null) return;
            var pieces = course.Layout.pieces;
            var instances = course.Instances;
            for (int i = 0; i < pieces.Count && i < instances.Count; i++) {
                if (instances[i] == null) continue;
                switch (pieces[i].kind) {
                    case OB_PieceKind.CheckpointPad: {
                        bool reached = pieces[i].stage <= progress.checkpoint;
                        bool cheer = celebrate && pieces[i].stage == progress.checkpoint;
                        var marker = instances[i].GetComponentInChildren<CheckpointMarker>();
                        if (marker != null) marker.SetReached(reached, cheer);
                        var flag = instances[i].GetComponentInChildren<OB_CheckpointFlag>();
                        if (flag != null) flag.SetRaised(reached, cheer);
                        if (cosmetics != null) cosmetics.DecorateFlag(flag, reached);
                        break;
                    }
                    case OB_PieceKind.Star: {
                        int stage = pieces[i].stage;
                        bool collected = stage < 64 && (progress.stars & (1UL << stage)) != 0;
                        if (instances[i].activeSelf == collected) {
                            if (collected && starCollected && fx != null) fx.Star(instances[i].transform.position);
                            instances[i].SetActive(!collected);
                        }
                        break;
                    }
                    case OB_PieceKind.Coin: {
                        int stage = pieces[i].stage;
                        bool collected = stage < 64 && (progress.coins & (1UL << stage)) != 0;
                        if (instances[i].activeSelf == collected) {
                            if (collected && coinCollected && fx != null) fx.Coin(instances[i].transform.position);
                            instances[i].SetActive(!collected);
                        }
                        break;
                    }
                }
            }
        }

        OB_Tier TierAt(int checkpoint) {
            var layout = course != null ? course.Layout : null;
            return layout == null || layout.StageCount == 0 ? OB_Tier.Basic : layout.stages[Mathf.Clamp(checkpoint, 0, layout.StageCount - 1)].tier;
        }

        void RefreshStage() {
            if (course == null || course.Layout == null) return;
            var layout = course.Layout;
            int checkpoint = hasProgress ? progress.checkpoint : 0;
            bool finished = hasProgress && progress.finished;
            if (checkpoint != segmentStage) {
                segmentStage = checkpoint;
                shownSegment = 0;
                nextTrack = 0f;
            }
            var current = layout.stages[Mathf.Clamp(checkpoint, 0, layout.StageCount - 1)];
            int parts = current.segments.Length;
            OB_StageKind part = parts > 0 ? current.segments[Mathf.Clamp(shownSegment, 0, parts - 1)] : current.kind;
            ulong seen = hasProgress ? progress.seenMechanics : 0UL;
            // A new stage, or a new segment of it, brings up its mechanic's hint: only while the player has never
            // cleared that mechanic (saved on the server), and once per session.
            int hintFor = finished ? -2 : checkpoint * 16 + shownSegment;
            if (hintFor != hintStage) {
                hintStage = hintFor;
                hintShownAt = Time.unscaledTime;
                hintKind = part;
                bool teach = !finished && !OB_StageHints.Has(seen | hintsShown, part);
                hintText = teach ? OB_StageText.Hint(part) : "";
                if (teach && hintText.Length > 0) {
                    hintsShown |= OB_StageHints.Bit(part);
                    hintText = $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Gold)}><b>NEW</b></color>  {hintText}";
                }
            } else if (!finished && OB_StageHints.Has(seen, hintKind)) {
                // The save arrived after the hint went up: this player already knows the mechanic.
                hintText = "";
            }
            OB_Tier tier = finished ? OB_Tier.Extreme : TierAt(checkpoint);
            Color accent = OB_Themes.Get(tier).accent;
            tierChip.enabled = true;
            tierChip.color = accent;
            tierLabel.text = OB_Tiers.Name(tier).ToUpperInvariant();
            tierLabel.color = accent.grayscale > 0.55f ? OB_Palette.Ink : OB_Palette.Paper;
            if (atmosphere != null) atmosphere.SetTier(tier);
            if (finished) {
                // The finish toast gives way to the results board, so a new best also stays on the card.
                stageLabel.text = progress.personalBest ? $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Go)}>NEW BEST!</color>" : "FINISHED";
                stageNameLabel.text = progress.medal != OB_Medal.None ? $"{progress.medal} medal!" : "Course complete!";
                hintLabel.text = "Wait here for the next course, or watch the others.";
                progressTarget = 1f;
            } else {
                stageLabel.text = $"STAGE {checkpoint + 1}<alpha=#88> / {layout.StageCount}";
                // Named after the segment the player is on, with its place in the stage when it chains several.
                stageNameLabel.text = parts > 1
                    ? $"{OB_StageText.Name(part)}<size=60%><alpha=#99>  {Mathf.Clamp(shownSegment, 0, parts - 1) + 1}/{parts}</size>"
                    : OB_StageText.Name(part);
                hintLabel.text = hintText;
                progressTarget = layout.StageCount > 0 ? (float)checkpoint / layout.StageCount : 0f;
            }
            int coins = OB_Scoring.CountCoins(hasProgress ? progress.coins : 0);
            coinsLabel.text = $"{coins}<size=70%><alpha=#99> / {layout.StageCount}</size>";
            FitHint();
            RefreshStars(tier);
        }

        /// <summary>Sizes the hint's backing to its text, and hides it with no text.</summary>
        void FitHint() {
            if (hintBacking == null) return;
            bool any = !string.IsNullOrEmpty(hintLabel.text);
            hintBacking.gameObject.SetActive(any);
            if (!any) return;
            float room = hintLabel.rectTransform.rect.width;
            Vector2 size = hintLabel.GetPreferredValues(hintLabel.text, room, 0f);
            size.x = Mathf.Min(size.x, room);
            hintBacking.sizeDelta = size + hintBackingPadding * 2f;
            // The hint's text is centred in its box, so the backing is centred under the race clock with it.
            hintBacking.anchoredPosition = new Vector2((room - size.x) * 0.5f - hintBackingPadding.x, -hintBackingPadding.y);
        }

        /// <summary>
        /// A few times a second: the fall-warning height for where the local player is (an earlier stage's
        /// when they went back for a star, as the server judges it), and which segment the card names.
        /// </summary>
        void TrackLocalPlayer() {
            if (Time.unscaledTime < nextTrack) return;
            nextTrack = Time.unscaledTime + trackSeconds;
            var layout = course != null ? course.Layout : null;
            var local = CharacterMovement.Local;
            if (layout == null || layout.StageCount == 0 || local == null || local.Body == null) return;
            Vector3 position = local.Body.position;
            bool finished = hasProgress && progress.finished;
            int checkpoint = Mathf.Clamp(hasProgress ? progress.checkpoint : 0, 0, layout.StageCount - 1);
            if (feel != null) {
                float killY = layout.finishTop.y - OB_CourseGenerator.KillDepth;
                if (!finished) layout.KillHeights(checkpoint, position, out killY, out _);
                feel.KillHeight = killY;
            }
            if (finished || !layout.IsOver(checkpoint, position)) return;
            int segment = SegmentAt(layout, checkpoint, position);
            if (segment >= 0 && segment != shownSegment) {
                shownSegment = segment;
                RefreshStage();
            }
        }

        /// <summary>
        /// Segment of <paramref name="stage"/> whose closest piece is nearest <paramref name="position"/>; -1 if
        /// none. Distance is to the space each piece takes up (<see cref="OB_PieceVolumes"/>), so the far end of a
        /// long corridor or belt still names its own segment rather than a small piece whose centre is nearer.
        /// </summary>
        int SegmentAt(OB_CourseLayout layout, int stage, Vector3 position) {
            if (indexedLayout != layout) {
                indexedLayout = layout;
                piecesByStage.Clear();
                pieceVolumes.Clear();
                for (int s = 0; s < layout.StageCount; s++) piecesByStage.Add(new List<int>());
                for (int i = 0; i < layout.pieces.Count; i++) {
                    OB_Piece piece = layout.pieces[i];
                    // Pieces that take no space (fields, pickups) count as a point at their centre.
                    pieceVolumes.Add(OB_PieceVolumes.TryGet(piece, out OB_Volume volume) ? volume : new OB_Volume(piece.position, Vector3.zero, 0f));
                    if (piece.segment >= 0 && piece.stage >= 0 && piece.stage < layout.StageCount) piecesByStage[piece.stage].Add(i);
                }
            }
            int best = -1;
            float bestDistance = float.PositiveInfinity, bestCentre = float.PositiveInfinity;
            foreach (int i in piecesByStage[stage]) {
                OB_Piece piece = layout.pieces[i];
                float distance = pieceVolumes[i].SqrDistance(position);
                // Inside two pieces at once (a coin over a platform, the seam of two corridors): the nearer centre.
                float centre = (piece.position - position).sqrMagnitude;
                if (distance < bestDistance || (distance == bestDistance && centre < bestCentre)) {
                    bestDistance = distance;
                    bestCentre = centre;
                    best = piece.segment;
                }
            }
            return best;
        }

        /// <summary>The star pill: stars collected in <paramref name="tier"/> out of the tier's stars.</summary>
        void RefreshStars(OB_Tier tier) {
            if (starPill == null || starsLabel == null) return;
            var layout = course.Layout;
            int total = layout.StarsInTier(tier);
            starPill.gameObject.SetActive(total > 0);
            if (total == 0) return;
            int got = 0;
            ulong stars = hasProgress ? progress.stars : 0;
            for (int s = 0; s < layout.StageCount && s < 64; s++)
                if (layout.stages[s].hasStar && layout.stages[s].tier == tier && (stars & (1UL << s)) != 0) got++;
            // Counted per tier (the toast counts the whole course), so the pill says so.
            starsLabel.text = $"<size=55%><alpha=#99>TIER </size><alpha=#FF>{got}<size=70%><alpha=#99> / {total}</size>";
        }

        /// <summary>Updates the clock and the line under it; strings are only rebuilt when what they show changes.</summary>
        void RefreshTimer() {
            float elapsed = Time.unscaledTime - roundReceivedAt;
            float clock = hasProgress && progress.finished ? progress.finishTime : Racing ? round.raceTime + elapsed : 0f;
            int tenths = Mathf.FloorToInt(Mathf.Max(0f, clock) * 10f);
            if (tenths != shownClockTenths) {
                shownClockTenths = tenths;
                timerLabel.SetCharArray(clockChars, 0, OB_Scoring.ClockTenths(tenths, clockChars));
            }
            bool split = Racing && Time.unscaledTime < splitUntil;
            string key = split ? "split" : round.phase switch {
                OB_Phase.Closing => "closing",
                OB_Phase.Racing => hasProgress && progress.finished ? "your time" : "race time",
                OB_Phase.Countdown => "get ready",
                OB_Phase.Results => "race over",
                _ => "waiting for players"
            };
            int closesIn = key == "closing" ? Mathf.Max(0, round.secondsLeft - (int)elapsed) : -1;
            // The keys are literals, so a reference check is enough (a new split clears the key).
            if (ReferenceEquals(key, shownStatusKey) && closesIn == shownStatusSeconds) return;
            shownStatusKey = key;
            shownStatusSeconds = closesIn;
            statusLabel.text = key switch {
                "split" => splitText,
                "closing" => $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Lava)}>closes in {closesIn}s</color>",
                _ => key
            };
            // Only the split sits on a pill: it is the line read at a glance while running.
            statusBacking.gameObject.SetActive(split);
            if (split) {
                float width = statusLabel.GetPreferredValues(splitText).x + statusBackingPadding;
                statusBacking.sizeDelta = new Vector2(width, statusBacking.sizeDelta.y);
            }
        }

        /// <summary>Shows a short message in the toast pill (other obby HUD parts, such as power-ups, announce through it too).</summary>
        public void Toast(string message, Color accent, float seconds = -1f) {
            toastLabel.text = message;
            toastBackground.color = accent.WithAlpha(toastOpacity);
            toastLabel.color = accent.grayscale > 0.55f ? OB_Palette.Ink : OB_Palette.Paper;
            toastShownAt = Time.unscaledTime;
            toastUntil = Time.unscaledTime + (seconds > 0f ? seconds : toastSeconds);
            Punch.Pop(toast, toastPopFrom, toastPopSeconds);
        }
    }
}
