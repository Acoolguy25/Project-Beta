#if !UNITY_SERVER
using System.Collections.Generic;
using FishNet;
using RyanAssets.Client.ClientUI.Vote;
using RyanAssets.Core;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Requests;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// The between-match board vote, drawn by the game instead of the shared vote panel: one tile per
    /// board with a picture of the layout, its tagline and numbers, live vote counts and a countdown.
    /// A click sends the usual <see cref="VoteRequest"/>, so the server's vote is unchanged. Lives on the
    /// HUD root (always active) because the card deactivates itself while hidden. Built by DI_Authoring;
    /// one tile is cloned per option at runtime.
    /// </summary>
    public sealed class DI_BoardVote : MonoBehaviour {
        [SerializeField] HudPopup popup;
        [SerializeField] DI_BoardVoteTile tileTemplate;
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text hintText;
        [Tooltip("The match result banner: the vote takes its place in the middle of the screen.")]
        [SerializeField] HudBanner resultBanner;

        [Header("Look")]
        [SerializeField] Color accent = new(0.5f, 0.85f, 1f);
        [Tooltip("Width in pixels of each board picture (its height keeps the arena's shape).")]
        [SerializeField, Range(64, 512)] int previewWidth = 224;
        [SerializeField, Min(0f)] float votePunch = 0.06f;
        [SerializeField, Min(0.01f)] float votePunchSeconds = 0.2f;

        [Header("Text")]
        [SerializeField] string timerFormat = "{0}s";
        [SerializeField] string oneVoteText = "1 vote";
        [Tooltip("{0} = votes.")]
        [SerializeField] string votesFormat = "{0} votes";
        [SerializeField] string hint = "Click a board to vote. Every match rolls a fresh layout in that style.";
        [SerializeField] string votedHint = "Vote counted. Click another board to change it.";
        [SerializeField] string spectatorHint = "You are spectating and can't vote.";

        readonly List<DI_BoardVoteTile> tiles = new();
        string[] boundOptions;
        float boundEndTime = -1f;
        int selected = -1;
        bool canVote;
        bool shown;
        int shownSeconds = -1;

        void OnEnable() => ClientVote.CustomDynamicVote = IsBoardVote;

        void OnDisable() {
            // Only clears the hook if it is still ours.
            if (ClientVote.CustomDynamicVote == (System.Predicate<SharedDynamicVote>)IsBoardVote)
                ClientVote.CustomDynamicVote = null;
        }

        static bool IsBoardVote(SharedDynamicVote vote) => vote.title == DI_BoardLayouts.VoteTitle;

        void Update() {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            bool active = events != null && SharedGlobalEvents.isVoting &&
                events.SharedVoteHeader.Value.voteId == VoteEnum.Dynamic && IsBoardVote(events.DynamicVote.Value);
            if (active != shown) {
                shown = active;
                if (popup != null) popup.SetVisible(active);
                if (active && resultBanner != null) resultBanner.Hide();
            }
            if (!active) return;

            SharedVoteHeader header = events.SharedVoteHeader.Value;
            SharedDynamicVote vote = events.DynamicVote.Value;
            if (!Mathf.Approximately(header.endTime, boundEndTime) || !SameOptions(vote.options))
                Bind(vote, header);

            int seconds = Mathf.CeilToInt(Mathf.Max(0f, header.endTime - NetworkHelper.ServerTime));
            if (seconds != shownSeconds && timerText != null) {
                shownSeconds = seconds;
                timerText.text = string.Format(timerFormat, seconds);
            }
            RefreshTiles(events);
        }

        bool SameOptions(string[] options) {
            if (boundOptions == null || options == null || boundOptions.Length != options.Length) return false;
            for (int i = 0; i < options.Length; i++)
                if (boundOptions[i] != options[i]) return false;
            return true;
        }

        void Bind(SharedDynamicVote vote, SharedVoteHeader header) {
            boundOptions = vote.options;
            boundEndTime = header.endTime;
            selected = -1;
            shownSeconds = -1;
            int localId = InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection != null
                ? InstanceFinder.ClientManager.Connection.ClientId : -1;
            canVote = vote.CanVote(localId);
            if (tileTemplate == null) return;

            tileTemplate.gameObject.SetActive(false);
            int count = vote.OptionCount;
            while (tiles.Count < count) {
                DI_BoardVoteTile tile = Instantiate(tileTemplate, tileTemplate.transform.parent);
                tiles.Add(tile);
            }
            for (int i = 0; i < tiles.Count; i++) {
                bool used = i < count;
                tiles[i].gameObject.SetActive(used);
                if (!used) continue;
                DI_BoardLayout layout = DI_BoardLayouts.FindByTitle(vote.options[i]);
                tiles[i].Bind(i, vote.options[i], layout, DI_BoardPreview.For(layout, previewWidth), OnTileClicked);
            }
            UpdateHint();
        }

        void RefreshTiles(SharedGlobalEvents events) {
            int top = 0;
            for (int i = 0; i < events.VoteTotals.Count; i++)
                top = Mathf.Max(top, events.VoteTotals[i]);
            foreach (DI_BoardVoteTile tile in tiles) {
                if (!tile.gameObject.activeSelf) continue;
                int votes = tile.Option < events.VoteTotals.Count ? events.VoteTotals[tile.Option] : 0;
                string text = votes == 1 ? oneVoteText : string.Format(votesFormat, votes);
                tile.SetState(tile.Option == selected, top > 0 && votes == top, canVote, text, accent);
            }
        }

        void OnTileClicked(int option) {
            if (!canVote || InstanceFinder.ClientManager == null) return;
            selected = option;
            // The button's own feel plays the click sound.
            InstanceFinder.ClientManager.Broadcast(new VoteRequest { optionId = option });
            foreach (DI_BoardVoteTile tile in tiles)
                if (tile.Option == option)
                    Punch.Scale(tile.transform, votePunch, votePunchSeconds);
            UpdateHint();
        }

        void UpdateHint() {
            if (hintText == null) return;
            hintText.text = !canVote ? spectatorHint : selected >= 0 ? votedHint : hint;
        }
    }
}
#endif
