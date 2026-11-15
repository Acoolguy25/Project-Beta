using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Platformer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Builds the course the server announces and presents the race: HUD, effects and the sky around
    /// the course. Decides nothing; every rule lives on the server.
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
        [Tooltip("Course progress bar fill; its anchor max x is set to the fraction of checkpoints reached.")]
        public RectTransform progressFill;
        public RectTransform stageCard;

        [Header("Race status")]
        public TMP_Text timerLabel;
        public TMP_Text statusLabel, coinsLabel, leaderLabel;
        public RectTransform coinPill;

        [Header("Announcements")]
        public TMP_Text toastLabel;
        public RectTransform toast;
        public CanvasGroup toastGroup;
        public Image toastBackground;
        public TMP_Text centerLabel;

        [Header("Results")]
        public GameObject resultsPanel;
        public TMP_Text resultsBody;

        [Header("Presentation")]
        public OB_Fx fx;
        public OB_Backdrop backdrop;
        public OB_Atmosphere atmosphere;
        public AudioSource sfx;
        public AudioClip coinClip, checkpointClip, finishClip, fallClip;
        [Tooltip("Seconds the stage progress bar takes to catch up.")]
        [Min(0.01f)] public float progressSmoothing = 0.25f;

        OB_Course course;
        ClientManager manager;
        int builtSeed, wantedSeed, wantedStages;
        OB_ProgressBroadcast progress;
        bool hasProgress;
        OB_RoundBroadcast round;
        float roundReceivedAt, toastShownAt, toastUntil, resultsUntil, nextRequest;
        float progressShown, progressTarget, progressVelocity, hintShownAt;
        int hintStage = -1;
        Pop stagePop, coinPop, toastPop, centerPop;

        /// <summary>A scale "pop" with overshoot, for HUD elements that change.</summary>
        struct Pop {
            RectTransform target;
            float start, duration, from;

            public Pop(RectTransform target, float duration, float from) {
                this.target = target;
                this.duration = duration;
                this.from = from;
                start = -1f;
            }

            public void Play() => start = Time.unscaledTime;

            public void Tick() {
                if (target == null || start < 0f) return;
                float t = (Time.unscaledTime - start) / duration;
                if (t >= 1f) {
                    target.localScale = Vector3.one;
                    start = -1f;
                    return;
                }
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float back = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                target.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, back);
            }
        }

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
            OB_StageKind.Hops => "Jump from platform to platform. Hold Shift to sprint.",
            OB_StageKind.Springs => "Land on a spring to launch, then steer onto the ledge.",
            OB_StageKind.MoonLeap => "Low gravity! Your jumps go much higher and farther.",
            OB_StageKind.Conveyors => "Belts push you around. Walk against them.",
            OB_StageKind.Crumble => "Cracked blocks fall soon after you land. Keep moving.",
            OB_StageKind.Beat => "Pink and blue blocks swap on the beat. They blink before vanishing.",
            OB_StageKind.WallJump => "Jump into a striped wall, then press Jump again to kick off it.",
            OB_StageKind.Movers => "Ride the moving platforms across.",
            OB_StageKind.Spinner => "Jump over the sweeping lava bar.",
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
            stagePop = new Pop(stageCard, 0.35f, 0.92f);
            coinPop = new Pop(coinPill, 0.3f, 1.35f);
            toastPop = new Pop(toast, 0.4f, 0.6f);
            centerPop = new Pop(centerLabel.rectTransform, 0.45f, 1.8f);
            if (backdrop != null && course != null) backdrop.Arrange(null, course.LobbySpawn);
        }

        void OnDestroy() {
            if (manager != null) {
                manager.UnregisterBroadcast<OB_CourseBroadcast>(OnCourse);
                manager.UnregisterBroadcast<OB_ProgressBroadcast>(OnProgress);
                manager.UnregisterBroadcast<OB_RoundBroadcast>(OnRound);
                manager.UnregisterBroadcast<OB_ResultsBroadcast>(OnResults);
            }
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<OB_CourseBroadcast>(OnCourse);
                manager.RegisterBroadcast<OB_ProgressBroadcast>(OnProgress);
                manager.RegisterBroadcast<OB_RoundBroadcast>(OnRound);
                manager.RegisterBroadcast<OB_ResultsBroadcast>(OnResults);
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
            // Toast: pop in, hold, fade out over its last quarter second.
            float fadeOut = Mathf.Clamp01((toastUntil - Time.unscaledTime) / 0.25f);
            float fadeIn = Mathf.Clamp01((Time.unscaledTime - toastShownAt) / 0.08f);
            toastGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            // Hint: shown when a stage starts, then fades so it stays out of the way.
            hintLabel.alpha = 1f - Mathf.Clamp01((Time.unscaledTime - hintShownAt - hintSeconds) / 0.8f);
            progressShown = Mathf.SmoothDamp(progressShown, progressTarget, ref progressVelocity, progressSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
            SetProgress(progressShown, false);
            stagePop.Tick();
            coinPop.Tick();
            toastPop.Tick();
            centerPop.Tick();
        }

        void SetProgress(float fraction, bool snap) {
            if (snap) progressShown = progressTarget = fraction;
            if (progressFill == null) return;
            var max = progressFill.anchorMax;
            max.x = Mathf.Clamp01(progressShown);
            progressFill.anchorMax = max;
        }

        void OnCourse(OB_CourseBroadcast message, Channel channel) {
            wantedSeed = message.seed;
            wantedStages = message.stageCount;
            if (course == null || message.seed == 0 || message.seed == builtSeed) return;
            course.Build(message.seed, message.stageCount);
            builtSeed = message.seed;
            hasProgress = false;
            resultsPanel.SetActive(false);
            SetProgress(0f, true);
            if (backdrop != null) backdrop.Arrange(course.Layout, course.LobbySpawn);
            RefreshStage();
        }

        void OnProgress(OB_ProgressBroadcast message, Channel channel) {
            if (message.seed != builtSeed) {
                if (message.seed == wantedSeed && wantedSeed != 0) OnCourse(new OB_CourseBroadcast { seed = wantedSeed, stageCount = wantedStages }, channel);
                if (message.seed != builtSeed) return;
            }
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
                    if (tier > tierBefore)
                        Toast($"{OB_Tiers.Name(tier).ToUpperInvariant()}  <size=75%><alpha=#CC>{OB_Tiers.Theme(tier)}</size>",
                            OB_Themes.Get(tier).accent, checkpointClip, 3f);
                    else
                        Toast($"CHECKPOINT {message.checkpoint}/{total - 1}" + (message.checkpoint - previous > 1 ? "  <size=70%>skipped ahead!</size>" : ""),
                            OB_Palette.Go, checkpointClip);
                    stagePop.Play();
                    break;
                }
                case OB_ProgressReason.Coin:
                    Toast("+1 COIN", OB_Palette.Gold, coinClip);
                    coinPop.Play();
                    break;
                case OB_ProgressReason.Finish:
                    Toast($"FINISHED {Ordinal(message.place)}!  {Clock(message.finishTime, true)}", OB_Palette.Gold, finishClip, 5f);
                    stagePop.Play();
                    if (fx != null) fx.Finish(course.Layout.finishTop);
                    break;
                case OB_ProgressReason.Hazard:
                    Toast("Ouch! Back to your checkpoint.", OB_Palette.Lava, fallClip, 1.5f);
                    if (fx != null) fx.RespawnAtPlayer();
                    break;
                case OB_ProgressReason.Fell:
                    Toast("Back to your checkpoint.", OB_Palette.InkSoft, fallClip, 1.5f);
                    if (fx != null) fx.RespawnAtPlayer();
                    break;
            }
        }

        void OnRound(OB_RoundBroadcast message, Channel channel) {
            round = message;
            roundReceivedAt = Time.unscaledTime;
            if (course != null) course.SetGateClosed(message.phase is OB_Phase.Waiting or OB_Phase.Countdown or OB_Phase.Results);
            if (message.seed != builtSeed && message.seed == wantedSeed && wantedSeed != 0)
                OnCourse(new OB_CourseBroadcast { seed = wantedSeed, stageCount = wantedStages }, channel);
            string center = message.phase switch {
                OB_Phase.Countdown => message.secondsLeft.ToString(),
                OB_Phase.Racing when message.raceTime < 1.5f => "GO!",
                _ => ""
            };
            if (center != centerLabel.text) {
                centerLabel.text = center;
                centerLabel.color = center == "GO!" ? OB_Palette.Go : OB_Palette.Paper;
                if (center.Length > 0) centerPop.Play();
            }
            leaderLabel.text = message.phase switch {
                OB_Phase.Racing or OB_Phase.Closing when !string.IsNullOrEmpty(message.leaderName) =>
                    $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Gold)}>LEADER</color>  {message.leaderName}  <alpha=#AA>{(message.finishedCount > 0 ? "finished" : "stage " + message.leaderStage)}",
                OB_Phase.Results => message.headline,
                _ => ""
            };
        }

        void OnResults(OB_ResultsBroadcast message, Channel channel) {
            var text = new StringBuilder();
            int place = 0;
            foreach (var entry in message.entries) {
                place++;
                string medal = ColorUtility.ToHtmlStringRGB(place switch {
                    1 => OB_Palette.Gold,
                    2 => OB_Palette.Hex(0xD3DCEF),
                    3 => OB_Palette.Hex(0xE89A5E),
                    _ => OB_Palette.Paper
                });
                string result = entry.finished ? Clock(entry.time, true) : $"<alpha=#99>stage {entry.stage}<alpha=#FF>";
                text.Append($"<color=#{medal}><b>{Ordinal(place)}</b></color><pos=12%>{entry.name}<pos=62%>{result}<pos=84%><color=#{medal}>{entry.coins}</color> <size=75%>coins</size>\n");
            }
            resultsBody.text = text.Length > 0 ? text.ToString() : "No racers.";
            resultsPanel.SetActive(true);
            resultsUntil = Time.unscaledTime + 9f;
        }

        void ApplyMarkers(bool celebrate, bool coinCollected) {
            if (course == null || course.Layout == null) return;
            var pieces = course.Layout.pieces;
            var instances = course.Instances;
            for (int i = 0; i < pieces.Count && i < instances.Count; i++) {
                if (instances[i] == null) continue;
                switch (pieces[i].kind) {
                    case OB_PieceKind.CheckpointPad:
                        var marker = instances[i].GetComponentInChildren<CheckpointMarker>();
                        if (marker != null) marker.SetReached(pieces[i].stage <= progress.checkpoint, celebrate && pieces[i].stage == progress.checkpoint);
                        break;
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
            int hintFor = hasProgress && progress.finished ? -2 : checkpoint;
            if (hintFor != hintStage) {
                hintStage = hintFor;
                hintShownAt = Time.unscaledTime;
            }
            OB_Tier tier = hasProgress && progress.finished ? OB_Tier.Extreme : TierAt(checkpoint);
            Color accent = OB_Themes.Get(tier).accent;
            tierChip.enabled = true;
            tierChip.color = accent;
            tierLabel.text = OB_Tiers.Name(tier).ToUpperInvariant();
            tierLabel.color = accent.grayscale > 0.55f ? OB_Palette.Ink : OB_Palette.Paper;
            if (atmosphere != null) atmosphere.SetTier(tier);
            if (hasProgress && progress.finished) {
                stageLabel.text = "FINISHED";
                stageNameLabel.text = $"{Ordinal(progress.place)} place!";
                hintLabel.text = "Wait here for the next course, or watch the others.";
                progressTarget = 1f;
            } else {
                var stage = layout.stages[Mathf.Clamp(checkpoint, 0, layout.StageCount - 1)];
                stageLabel.text = $"STAGE {checkpoint + 1}<alpha=#88> / {layout.StageCount}";
                stageNameLabel.text = StageName(stage.kind);
                hintLabel.text = StageHint(stage.kind);
                progressTarget = layout.StageCount > 0 ? (float)checkpoint / layout.StageCount : 0f;
            }
            int coins = 0;
            for (ulong c = hasProgress ? progress.coins : 0; c != 0; c &= c - 1) coins++;
            coinsLabel.text = $"{coins}<size=70%><alpha=#99> / {layout.StageCount}</size>";
        }

        void RefreshTimer() {
            float elapsed = Time.unscaledTime - roundReceivedAt;
            bool racing = round.phase is OB_Phase.Racing or OB_Phase.Closing;
            if (hasProgress && progress.finished) timerLabel.text = Clock(progress.finishTime, true);
            else timerLabel.text = racing ? Clock(round.raceTime + elapsed, true) : "0:00.0";
            string status = round.phase switch {
                OB_Phase.Closing => $"<color=#{ColorUtility.ToHtmlStringRGB(OB_Palette.Lava)}>closes in {Mathf.Max(0, round.secondsLeft - (int)elapsed)}s</color>",
                OB_Phase.Racing => hasProgress && progress.finished ? "your time" : "race time",
                OB_Phase.Countdown => "get ready",
                OB_Phase.Results => "race over",
                _ => "waiting for players"
            };
            if (statusLabel.text != status) statusLabel.text = status;
        }

        void Toast(string message, Color accent, AudioClip clip, float seconds = 2.2f) {
            toastLabel.text = message;
            toastBackground.color = accent.WithAlpha(0.9f);
            toastLabel.color = accent.grayscale > 0.55f ? OB_Palette.Ink : OB_Palette.Paper;
            toastShownAt = Time.unscaledTime;
            toastUntil = Time.unscaledTime + seconds;
            toastPop.Play();
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }

        static string Clock(float seconds, bool tenths) {
            seconds = Mathf.Max(0f, seconds);
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return tenths ? $"{minutes}:{rest:00.0}" : $"{minutes}:{(int)rest:00}";
        }

        static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
