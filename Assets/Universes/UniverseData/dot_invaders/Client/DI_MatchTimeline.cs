using UnityEngine;
#if !UNITY_SERVER
using System;
using RyanAssets.UI.GameHud;
using TMPro;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// The "how the match went" card shown when a match ends: every team's power share over time
    /// (<see cref="DI_PowerGraph"/>), the match length, and a line on your peak and how often the lead
    /// changed. Records from the snapshots the HUD is fed, so it needs nothing extra from the server.
    /// Generated into the HUD by DI_Authoring.
    /// </summary>
    public sealed class DI_MatchTimeline : MonoBehaviour {
#if !UNITY_SERVER
        [SerializeField] HudPopup popup;
        [SerializeField] DI_PowerGraph graph;
        [SerializeField] TMP_Text endLabel;
        [SerializeField] TMP_Text summary;
        [Tooltip("Seconds between samples at the start of a match (fewer once a long match fills the history).")]
        [SerializeField, Min(0.1f)] float sampleSeconds = 2f;
        [SerializeField, Min(4)] int maxSamples = DI_PowerHistory.DefaultMaxSamples;
        [Tooltip("{0} your peak share, {1} when, {2} lead changes.")]
        [SerializeField] string summaryFormat = "Your peak <b>{0}</b> at {1}  ·  Lead changed {2}x";
        [Tooltip("Shown instead when your team never held power (you watched this match).")]
        [SerializeField] string spectatorFormat = "Lead changed {0}x";

        DI_PowerHistory history;
        // Board and length of the match being recorded, to tell a new match from the running one.
        string recordingLayout;
        int startSeconds = -1;
        bool ended;
        int yourTeam = -1;
        Func<int, Color> teamColor;

        // The card starts hidden (inactive), so nothing here may rely on Awake or Update.
        DI_PowerHistory History => history ??= new DI_PowerHistory(sampleSeconds, maxSamples);

        /// <summary>Feeds one snapshot: samples it while the match runs and shows the card once it ends.</summary>
        public void Record(in DI_StateBroadcast state, Func<int, Color> colorOf) {
            teamColor = colorOf;
            if (!state.matchEnded && (ended || startSeconds < 0 || state.layoutId != recordingLayout)) {
                History.Clear();
                recordingLayout = state.layoutId;
                startSeconds = state.secondsRemaining;
                ended = false;
                if (popup != null && popup.Visible)
                    popup.Hide();
            }
            if (ended || startSeconds < 0)
                return;

            // Match seconds from the clock: the first snapshot this client saw is time 0 if it joined late.
            float elapsed = Mathf.Max(0, startSeconds - state.secondsRemaining);
            yourTeam = state.yourTeamId;
            History.Record(elapsed + (state.matchEnded ? 1f : 0f), DI_Rules.CalculatePower(state), state.matchEnded);
            if (state.matchEnded) {
                ended = true;
                if (History.Count >= 2)
                    Show();
            }
        }

        void Show() {
            if (graph != null)
                graph.SetData(history, yourTeam, teamColor);
            if (endLabel != null)
                endLabel.text = DI_Rules.FormatClock(history.Duration);
            if (summary != null) {
                int changes = history.LeadChanges();
                summary.text = yourTeam >= 0 && history.TryPeak(yourTeam, out float share, out float time) && share > 0f
                    ? string.Format(summaryFormat, $"{Mathf.RoundToInt(share * 100f)}%", DI_Rules.FormatClock(time), changes)
                    : string.Format(spectatorFormat, changes);
            }
            popup?.Show();
        }
#endif
    }
}
