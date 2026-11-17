using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Platformer;
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
        public RectTransform coinPill;
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
        [Tooltip("Most rows the board lists. With more racers it lists the leaders and always your own row.")]
        [Min(1)] public int resultsMaxRows = 8;
        [Tooltip("Height of the board around its rows (header, margins and footer); the rows add their own height.")]
        [Min(0f)] public float resultsFrameHeight = 236f;
        [Tooltip("Longest racer name shown in full; longer names are cut with \"..\".")]
        [Min(4)] public int resultsNameLength = 16;

        [Header("Presentation")]
        public OB_Fx fx;
        public OB_PlayerFeel feel;
        public OB_Backdrop backdrop;
        public OB_Atmosphere atmosphere;
        [Tooltip("Parkour moves and ragdoll for the local player; hazards knock the character limp.")]
        public CharacterParkour parkour;
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
        int builtSeed, wantedSeed, wantedStages;
        float wantedCoinBonus, wantedGoldPerStage;
        OB_ProgressBroadcast progress;
        bool hasProgress;
        OB_RoundBroadcast round;
        float roundReceivedAt, toastShownAt, toastUntil, resultsUntil, nextRequest, splitUntil;
        float progressShown, progressTarget, progressVelocity, hintShownAt;
        int hintStage = -1;
        string splitText = "";
        // What the clock and status line show, so their strings are only rebuilt when it changes.
        int shownClockTenths = -1;
        readonly char[] clockChars = new char[16];
        string shownStatusKey;
        int shownStatusSeconds = -1;
        readonly StringBuilder rows = new();

        static string StageName(OB_StageKind kind) => kind switch {
            OB_StageKind.Hops => "Skyhop",
            OB_StageKind.Springs => "Spring Climb",
            OB_StageKind.MoonLeap => "Moon Leap",
            OB_StageKind.Conveyors => "Belt Run",
            OB_StageKind.Crumble => "Crumble Bridge",
            OB_StageKind.Beat => "Beat Blocks",
            OB_StageKind.WallJump => "Wall Kick Shaft",
            OB_StageKind.Movers => "Sky Ferry",
            OB_StageKind.Spinner => "Sweeper",
            OB_StageKind.LavaRun => "Lava Stones",
            OB_StageKind.Boost => "Boost Gap",
            OB_StageKind.WallWalk => "Wall Walk",
            OB_StageKind.CeilingRun => "Ceiling Run",
            _ => kind.ToString()
        };

        static string StageHint(OB_StageKind kind) => kind switch {
            OB_StageKind.Hops => "Jump from platform to platform. Shift sprints, C slides; catch ledges and wall run to save a jump.",
            OB_StageKind.Springs => "Land on a spring to launch, then steer onto the ledge.",
            OB_StageKind.MoonLeap => "Low gravity! Your jumps go much higher and farther.",
            OB_StageKind.Conveyors => "Belts push you around. Walk against them.",
            OB_StageKind.Crumble => "Cracked blocks fall soon after you land. Keep moving.",
            OB_StageKind.Beat => "Pink and blue blocks swap on the beat. They blink before vanishing.",
            OB_StageKind.WallJump => "Jump into a striped wall, then press Jump again to kick off it.",
            OB_StageKind.Movers => "Ride the moving platforms across.",
            OB_StageKind.Spinner => "Jump over the sweeper. If it hits you, you go flying!",
            OB_StageKind.LavaRun => "Hop across the stones. Lava sends you back.",
            OB_StageKind.Boost => "Run over the boost pad to fly across the gap.",
            OB_StageKind.WallWalk => "Step into the glowing field and gravity pulls you onto the wall. Run along it!",
            OB_StageKind.CeilingRun => "Gravity flips upside down here. Run along the ceiling and jump the lava.",
            _ => ""
        };

        void Awake() {
            course = GetComponentInParent<OB_Course>();
            resultsPanel.SetActive(false);
            toastLabel.text = "";
            toastGroup.alpha = 0f;
            centerLabel.text = "";
            stageLabel.text = "SKY OBBY";
            stageNameLabel.text = "Connecting...";
            hintLabel.text = "";
            tierLabel.text = "";
            tierChip.enabled = false;
            coinsLabel.text = "0";
            timerLabel.text = "0:00.0";
            statusLabel.text = "";
            leaderLabel.text = "";
            SetProgress(0f, true);
            if (backdrop != null && course != null) backdrop.Arrange(null, course.LobbySpawn);
        }

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
            if (resultsPanel.activeSelf && Time.unscaledTime > resultsUntil) resultsPanel.SetActive(false);
            RefreshTimer();
            AnimateHud();
        }

        void AnimateHud() {
            float fadeOut = Mathf.Clamp01((toastUntil - Time.unscaledTime) / toastFadeOutSeconds);
            float fadeIn = Mathf.Clamp01((Time.unscaledTime - toastShownAt) / toastFadeInSeconds);
            toastGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            // Hint: shown when a stage starts, then fades so it stays out of the way.
            hintLabel.alpha = 1f - Mathf.Clamp01((Time.unscaledTime - hintShownAt - hintSeconds) / hintFadeSeconds);
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
            wantedCoinBonus = message.coinBonus;
            wantedGoldPerStage = message.goldPerStage;
            if (course == null || message.seed == 0 || message.seed == builtSeed) return;
            course.Build(message.seed, message.stageCount);
            builtSeed = message.seed;
            hasProgress = false;
            splitUntil = 0f;
            if (raceStrip != null) raceStrip.Clear();
            if (fx != null) fx.CoinBonus = wantedCoinBonus;
            resultsPanel.SetActive(false);
            SetProgress(0f, true);
            if (backdrop != null) backdrop.Arrange(course.Layout, course.LobbySpawn);
            RefreshStage();
        }

        /// <summary>Builds the announced course if a message for it arrived before the course itself was built.</summary>
        void CatchUp(int seed, Channel channel) {
            if (seed != builtSeed && seed == wantedSeed && wantedSeed != 0)
                OnCourse(new OB_CourseBroadcast {
                    seed = wantedSeed, stageCount = wantedStages, coinBonus = wantedCoinBonus, goldPerStage = wantedGoldPerStage
                }, channel);
        }

        void OnProgress(OB_ProgressBroadcast message, Channel channel) {
            CatchUp(message.seed, channel);
            if (message.seed != builtSeed) return;
            int previous = hasProgress ? progress.checkpoint : 0;
            OB_Tier tierBefore = TierAt(previous);
            progress = message;
            hasProgress = true;
            ApplyMarkers(message.reason == OB_ProgressReason.Checkpoint, message.reason == OB_ProgressReason.Coin);
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
                    ShowSplit(message.checkpoint, message.raceTime, OB_Scoring.CountCoins(message.coins));
                    Punch.Pop(stageCard, stagePopFrom, stagePopSeconds);
                    break;
                }
                case OB_ProgressReason.Coin:
                    Punch.Pop(coinPill, coinPopFrom, coinPopSeconds);
                    break;
                case OB_ProgressReason.Finish:
                    // No medal is not gold: a plain finish gets the "go" green.
                    Toast(FinishToast(message), message.medal == OB_Medal.None ? OB_Palette.Go : OB_Palette.Medal(message.medal), 5f);
                    Punch.Pop(stageCard, stagePopFrom, stagePopSeconds);
                    if (fx != null) fx.Finish(course.Layout.finishTop, message.personalBest);
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
            }
        }

        static string FinishToast(in OB_ProgressBroadcast message) {
            var text = new StringBuilder();
            text.Append(message.medal != OB_Medal.None ? message.medal.ToString().ToUpperInvariant() : "FINISHED");
            text.Append("  ").Append(OB_Scoring.Clock(message.scoredTime, true));
            float bonus = message.finishTime - message.scoredTime;
            if (bonus > 0.05f) text.Append($"  <size=70%>coins -{bonus:0.0}s</size>");
            if (message.personalBest) text.Append("  <size=70%>NEW BEST!</size>");
            return text.ToString();
        }

        /// <summary>
        /// Shows the split for <paramref name="checkpoint"/> under the clock against the gold medal pace,
        /// which exists from the first checkpoint of the first race (coins count, as in the medal).
        /// </summary>
        void ShowSplit(int checkpoint, float raceTime, int coins) {
            if (raceTime <= 0f || splitSeconds <= 0f) return;
            string clock = OB_Scoring.Clock(raceTime, true);
            if (wantedGoldPerStage > 0f) {
                float delta = OB_Scoring.GoldPaceDelta(raceTime, coins, wantedCoinBonus, checkpoint, wantedGoldPerStage);
                Color color = delta <= 0f ? OB_Palette.Go : OB_Palette.Lava;
                splitText = $"{clock}  <color=#{ColorUtility.ToHtmlStringRGB(color)}>{(delta <= 0f ? "-" : "+")}{Mathf.Abs(delta):0.0} <size=80%>vs gold</size></color>";
            } else {
                splitText = $"split {clock}";
            }
            splitUntil = Time.unscaledTime + splitSeconds;
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
            // Too many racers for the board: the leaders, then your own row in its real place.
            int listed = Mathf.Min(entries.Length, resultsMaxRows);
            bool appendMine = mine >= resultsMaxRows;
            if (appendMine) listed = resultsMaxRows - 1;
            rows.Clear();
            for (int i = 0; i < listed; i++) AppendRow(entries[i], i + 1, i == mine);
            if (appendMine) AppendRow(entries[mine], mine + 1, true);
            resultsBody.text = rows.Length > 0 ? rows.ToString() : "No racers.";
            resultsFooter.text = MedalTargets(message);
            // The board is as tall as its rows, so one racer is not an empty board and eight still fit.
            var board = (RectTransform)resultsPanel.transform;
            float height = resultsFrameHeight + resultsBody.GetPreferredValues(resultsBody.text, resultsBody.rectTransform.rect.width, 0f).y;
            board.sizeDelta = new Vector2(board.sizeDelta.x, height);
            resultsPanel.SetActive(true);
            // The board repeats your finish line, so the finish toast makes way for it.
            toastUntil = Mathf.Min(toastUntil, Time.unscaledTime);
            resultsUntil = Time.unscaledTime + resultsSeconds;
            if (raceStrip != null) raceStrip.Refresh(me, StageCount, true);
        }

        void AppendRow(in OB_ResultEntry entry, int place, bool mine) {
            string rank = ColorUtility.ToHtmlStringRGB(place switch {
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
            rows.Append($"<pos=87%><color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Gold)}>{entry.coins}</color> <size=75%>{(entry.coins == 1 ? "coin" : "coins")}</size>\n");
        }

        static string MedalTargets(in OB_ResultsBroadcast message) {
            if (message.goldTime <= 0f) return "";
            string Medal(OB_Medal medal, float seconds) =>
                $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Medal(medal))}>• {medal.ToString().ToUpperInvariant()}</color> {OB_Scoring.Clock(seconds)}";
            string bonus = message.coinBonus > 0f ? $"     Each coin takes {message.coinBonus:0.0#} s off your time." : "";
            return $"{Medal(OB_Medal.Gold, message.goldTime)}   {Medal(OB_Medal.Silver, message.silverTime)}   {Medal(OB_Medal.Bronze, message.bronzeTime)}{bonus}";
        }

        // ---------------------------------------------------------------- course markers and HUD
        void ApplyMarkers(bool celebrate, bool coinCollected) {
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
                        break;
                    }
                    case OB_PieceKind.Coin:
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

        OB_Tier TierAt(int checkpoint) {
            var layout = course != null ? course.Layout : null;
            return layout == null || layout.StageCount == 0 ? OB_Tier.Basic : layout.stages[Mathf.Clamp(checkpoint, 0, layout.StageCount - 1)].tier;
        }

        void RefreshStage() {
            if (course == null || course.Layout == null) return;
            var layout = course.Layout;
            int checkpoint = hasProgress ? progress.checkpoint : 0;
            bool finished = hasProgress && progress.finished;
            int hintFor = finished ? -2 : checkpoint;
            if (hintFor != hintStage) {
                hintStage = hintFor;
                hintShownAt = Time.unscaledTime;
            }
            OB_Tier tier = finished ? OB_Tier.Extreme : TierAt(checkpoint);
            Color accent = OB_Themes.Get(tier).accent;
            tierChip.enabled = true;
            tierChip.color = accent;
            tierLabel.text = OB_Tiers.Name(tier).ToUpperInvariant();
            tierLabel.color = accent.grayscale > 0.55f ? OB_Palette.Ink : OB_Palette.Paper;
            if (atmosphere != null) atmosphere.SetTier(tier);
            var current = layout.stages[Mathf.Clamp(checkpoint, 0, layout.StageCount - 1)];
            if (feel != null) feel.KillHeight = finished ? layout.finishTop.y - OB_CourseGenerator.KillDepth : current.killY;
            if (finished) {
                stageLabel.text = "FINISHED";
                stageNameLabel.text = progress.medal != OB_Medal.None ? $"{progress.medal} medal!" : "Course complete!";
                hintLabel.text = "Wait here for the next course, or watch the others.";
                progressTarget = 1f;
            } else {
                stageLabel.text = $"STAGE {checkpoint + 1}<alpha=#88> / {layout.StageCount}";
                stageNameLabel.text = StageName(current.kind);
                hintLabel.text = StageHint(current.kind);
                progressTarget = layout.StageCount > 0 ? (float)checkpoint / layout.StageCount : 0f;
            }
            int coins = OB_Scoring.CountCoins(hasProgress ? progress.coins : 0);
            coinsLabel.text = $"{coins}<size=70%><alpha=#99> / {layout.StageCount}</size>";
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
        }

        /// <summary>Shows a short message in the toast pill.</summary>
        void Toast(string message, Color accent, float seconds = -1f) {
            toastLabel.text = message;
            toastBackground.color = accent.WithAlpha(toastOpacity);
            toastLabel.color = accent.grayscale > 0.55f ? OB_Palette.Ink : OB_Palette.Paper;
            toastShownAt = Time.unscaledTime;
            toastUntil = Time.unscaledTime + (seconds > 0f ? seconds : toastSeconds);
            Punch.Pop(toast, toastPopFrom, toastPopSeconds);
        }
    }
}
