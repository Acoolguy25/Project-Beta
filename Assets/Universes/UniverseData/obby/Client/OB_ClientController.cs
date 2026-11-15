using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Platformer;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>Builds the course the server announces and shows race progress. Decides nothing.</summary>
    public sealed class OB_ClientController : MonoBehaviour {
        public TMP_Text stageLabel, stageNameLabel, hintLabel, coinsLabel, timerLabel, toastLabel, centerLabel, leaderLabel;
        public GameObject resultsPanel;
        public TMP_Text resultsBody;
        public AudioSource sfx;
        public AudioClip coinClip, checkpointClip, finishClip, fallClip;

        OB_Course course;
        ClientManager manager;
        int builtSeed, wantedSeed, wantedStages;
        OB_ProgressBroadcast progress;
        bool hasProgress;
        OB_RoundBroadcast round;
        float roundReceivedAt, toastUntil, resultsUntil, nextRequest;

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
            centerLabel.text = "";
            stageLabel.text = "OBBY";
            stageNameLabel.text = "Connecting...";
            hintLabel.text = "";
            coinsLabel.text = "";
            timerLabel.text = "";
            leaderLabel.text = "";
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
            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = "";
            if (resultsPanel.activeSelf && Time.unscaledTime > resultsUntil) resultsPanel.SetActive(false);
            RefreshTimer();
        }

        void OnCourse(OB_CourseBroadcast message, Channel channel) {
            wantedSeed = message.seed;
            wantedStages = message.stageCount;
            if (course == null || message.seed == 0 || message.seed == builtSeed) return;
            course.Build(message.seed, message.stageCount);
            builtSeed = message.seed;
            hasProgress = false;
            resultsPanel.SetActive(false);
            RefreshStage();
        }

        void OnProgress(OB_ProgressBroadcast message, Channel channel) {
            if (message.seed != builtSeed) {
                if (message.seed == wantedSeed && wantedSeed != 0) OnCourse(new OB_CourseBroadcast { seed = wantedSeed, stageCount = wantedStages }, channel);
                if (message.seed != builtSeed) return;
            }
            int previous = hasProgress ? progress.checkpoint : 0;
            progress = message;
            hasProgress = true;
            ApplyMarkers(message.reason == OB_ProgressReason.Checkpoint);
            RefreshStage();
            int total = course.Layout.StageCount;
            switch (message.reason) {
                case OB_ProgressReason.Checkpoint:
                    Toast($"CHECKPOINT {message.checkpoint}/{total - 1}" + (message.checkpoint - previous > 1 ? "  (skipped ahead!)" : ""), checkpointClip);
                    break;
                case OB_ProgressReason.Coin:
                    Toast("+1 COIN", coinClip);
                    break;
                case OB_ProgressReason.Finish:
                    Toast($"FINISHED {Ordinal(message.place)}!  {Clock(message.finishTime, true)}", finishClip, 5f);
                    break;
                case OB_ProgressReason.Hazard:
                    Toast("Ouch! Back to your checkpoint.", fallClip, 1.5f);
                    break;
                case OB_ProgressReason.Fell:
                    Toast("Back to your checkpoint.", fallClip, 1.5f);
                    break;
            }
        }

        void OnRound(OB_RoundBroadcast message, Channel channel) {
            round = message;
            roundReceivedAt = Time.unscaledTime;
            if (course != null) course.SetGateClosed(message.phase is OB_Phase.Waiting or OB_Phase.Countdown or OB_Phase.Results);
            if (message.seed != builtSeed && message.seed == wantedSeed && wantedSeed != 0)
                OnCourse(new OB_CourseBroadcast { seed = wantedSeed, stageCount = wantedStages }, channel);
            centerLabel.text = message.phase switch {
                OB_Phase.Countdown => message.secondsLeft.ToString(),
                OB_Phase.Racing when message.raceTime < 1.5f => "GO!",
                _ => ""
            };
            leaderLabel.text = message.phase switch {
                OB_Phase.Racing or OB_Phase.Closing when !string.IsNullOrEmpty(message.leaderName) =>
                    $"Leader: {message.leaderName} ({(message.finishedCount > 0 ? "finished" : "stage " + message.leaderStage)})",
                OB_Phase.Results => message.headline,
                _ => ""
            };
        }

        void OnResults(OB_ResultsBroadcast message, Channel channel) {
            var text = new StringBuilder();
            int place = 0;
            foreach (var entry in message.entries) {
                place++;
                string result = entry.finished ? Clock(entry.time, true) : $"stage {entry.stage}";
                text.AppendLine($"{place}. {entry.name}   {result}   {entry.coins} coins");
            }
            resultsBody.text = text.Length > 0 ? text.ToString() : "No racers.";
            resultsPanel.SetActive(true);
            resultsUntil = Time.unscaledTime + 9f;
        }

        void ApplyMarkers(bool celebrate) {
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
                        if (instances[i].activeSelf == collected) instances[i].SetActive(!collected);
                        break;
                }
            }
        }

        void RefreshStage() {
            if (course == null || course.Layout == null) return;
            var layout = course.Layout;
            int checkpoint = hasProgress ? progress.checkpoint : 0;
            if (hasProgress && progress.finished) {
                stageLabel.text = "FINISHED";
                stageNameLabel.text = $"{Ordinal(progress.place)} place";
                hintLabel.text = "Wait here for the next course, or watch the others.";
            } else {
                var stage = layout.stages[Mathf.Clamp(checkpoint, 0, layout.StageCount - 1)];
                stageLabel.text = $"STAGE {checkpoint + 1}/{layout.StageCount}";
                stageNameLabel.text = StageName(stage.kind);
                hintLabel.text = StageHint(stage.kind);
            }
            int coins = 0;
            for (ulong c = hasProgress ? progress.coins : 0; c != 0; c &= c - 1) coins++;
            coinsLabel.text = $"Coins {coins}/{layout.StageCount}";
        }

        void RefreshTimer() {
            if (hasProgress && progress.finished) { timerLabel.text = Clock(progress.finishTime, true); return; }
            timerLabel.text = round.phase switch {
                OB_Phase.Racing or OB_Phase.Closing => Clock(round.raceTime + (Time.unscaledTime - roundReceivedAt), true)
                    + (round.phase == OB_Phase.Closing ? $"   closes in {Mathf.Max(0, round.secondsLeft - (int)(Time.unscaledTime - roundReceivedAt))}s" : ""),
                OB_Phase.Countdown => "Get ready",
                OB_Phase.Results => "Race over",
                _ => "Waiting for players"
            };
        }

        void Toast(string message, AudioClip clip, float seconds = 2.2f) {
            toastLabel.text = message;
            toastUntil = Time.unscaledTime + seconds;
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
