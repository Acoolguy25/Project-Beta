using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerVote {
        public static float DefaultDurationSeconds = 30f;

        static readonly Dictionary<NetworkConnection, int> PlayerVotes = new();
        static Coroutine closeRoutine;
        static int nextVoteId = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.RegisterBroadcast<VoteRequest>(OnVoteRequest, true);
            ServerPlayerEvents.OnPlayerRemovedEvent += OnPlayerRemoved;
        }

        public static void StartVote(string title, IReadOnlyList<SharedVoteOption> options, float durationSeconds = -1f, string description = "") {
            if (SharedGlobalEvents.Instance == null) {
                Debug.LogError("Cannot start vote. SharedGlobalEvents.Instance is missing.");
                return;
            }

            int voteId = nextVoteId++;
            float duration = durationSeconds > 0f ? durationSeconds : DefaultDurationSeconds;
            PlayerVotes.Clear();
            SharedGlobalEvents.Instance.VoteOptions.Clear();

            for (int i = 0; i < options.Count; i++) {
                SharedVoteOption option = options[i];
                option.voteId = voteId;
                option.optionId = option.optionId == 0 ? i + 1 : option.optionId;
                option.count = 0;
                SharedGlobalEvents.Instance.VoteOptions.Add(option);
            }

            SharedGlobalEvents.Instance.CurrentVote = new SharedVoteInfo {
                voteId = voteId,
                title = title,
                description = description,
                endUtcTicks = DateTime.UtcNow.AddSeconds(duration).Ticks,
                isActive = true
            };

            if (closeRoutine != null)
                SharedGlobalEvents.Instance.StopCoroutine(closeRoutine);
            closeRoutine = SharedGlobalEvents.Instance.StartCoroutine(CloseVoteAfter(duration, voteId));
        }

        public static void StartVote(string title, IReadOnlyList<string> optionTitles, float durationSeconds = -1f, string description = "") {
            List<SharedVoteOption> options = new(optionTitles.Count);
            for (int i = 0; i < optionTitles.Count; i++)
                options.Add(new SharedVoteOption { optionId = i + 1, title = optionTitles[i] });

            StartVote(title, options, durationSeconds, description);
        }

        public static void EndCurrentVote() {
            if (SharedGlobalEvents.Instance == null)
                return;

            SharedVoteInfo vote = SharedGlobalEvents.Instance.CurrentVote;
            vote.isActive = false;
            vote.endUtcTicks = DateTime.UtcNow.Ticks;
            SharedGlobalEvents.Instance.CurrentVote = vote;
            PlayerVotes.Clear();
            closeRoutine = null;
        }

        static IEnumerator CloseVoteAfter(float seconds, int voteId) {
            yield return new WaitForSeconds(seconds);
            if (SharedGlobalEvents.Instance.CurrentVote.voteId == voteId)
                EndCurrentVote();
        }

        static void OnVoteRequest(NetworkConnection conn, VoteRequest request, Channel channel) {
            SharedVoteInfo vote = SharedGlobalEvents.Instance.CurrentVote;
            if (!vote.isActive || request.voteId != vote.voteId)
                return;

            if (request.optionId != 0 && FindOptionIndex(vote.voteId, request.optionId) < 0)
                return;

            if (PlayerVotes.TryGetValue(conn, out int previousOptionId))
                ChangeVoteCount(vote.voteId, previousOptionId, -1);

            if (request.optionId == 0) {
                PlayerVotes.Remove(conn);
                return;
            }

            PlayerVotes[conn] = request.optionId;
            ChangeVoteCount(vote.voteId, request.optionId, 1);
        }

        static void OnPlayerRemoved(NetworkConnection conn) {
            SharedVoteInfo vote = SharedGlobalEvents.Instance.CurrentVote;
            if (!vote.isActive || !PlayerVotes.TryGetValue(conn, out int optionId))
                return;

            PlayerVotes.Remove(conn);
            ChangeVoteCount(vote.voteId, optionId, -1);
        }

        static int FindOptionIndex(int voteId, int optionId) {
            for (int i = 0; i < SharedGlobalEvents.Instance.VoteOptions.Count; i++) {
                SharedVoteOption option = SharedGlobalEvents.Instance.VoteOptions[i];
                if (option.voteId == voteId && option.optionId == optionId)
                    return i;
            }

            return -1;
        }

        static void ChangeVoteCount(int voteId, int optionId, int delta) {
            int index = FindOptionIndex(voteId, optionId);
            if (index < 0)
                return;

            SharedVoteOption option = SharedGlobalEvents.Instance.VoteOptions[index];
            option.count = Mathf.Max(0, option.count + delta);
            SharedGlobalEvents.Instance.VoteOptions[index] = option;
        }
    }
}
