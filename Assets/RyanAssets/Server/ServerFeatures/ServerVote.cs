using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerVote {
        static int voteGeneration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.RegisterBroadcast<VoteRequest>(OnVoteRequest, true);
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
        }

        static void OnVoteRequest(NetworkConnection connection, VoteRequest request, FishNet.Transporting.Channel channel) {
            if (!SharedGlobalEvents.isVoting || !PlayerData.TryGetPlayerData(connection, out PlayerData player))
                return;

            int optionCount = VoteDeclarations.GetOptionCount(SharedGlobalEvents.Instance.SharedVoteHeader.Value.voteId);
            if (request.optionId < -1 || request.optionId >= optionCount)
                return;

            SetPlayerVote(player, request.optionId, optionCount);
        }

        static void OnPlayerRemoved(NetworkConnection connection, PlayerData player) {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            if (!SharedGlobalEvents.isVoting)
                return;

            SetPlayerVote(player, -1, events.VoteTotals.Count);
        }

        static void SetPlayerVote(PlayerData player, int newOption, int optionCount) {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            int previousOption = player.voteOption.Value;
            if (previousOption == newOption)
                return;

            if (previousOption >= 0 && previousOption < events.VoteTotals.Count)
                events.VoteTotals[previousOption] = Mathf.Max(0, events.VoteTotals[previousOption] - 1);

            if (newOption >= 0 && newOption < optionCount)
                events.VoteTotals[newOption]++;

            player.voteOption.Value = newOption;
        }

        public static async UniTask<int> StartVote(VoteEnum voteEnum, float duration, CancellationToken token) {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            Debug.Assert(events != null, "Cannot start a vote without SharedGlobalEvents.");

            int optionCount = VoteDeclarations.GetOptionCount(voteEnum);
            if (voteEnum == VoteEnum.None || optionCount == 0)
                throw new ArgumentException($"Vote '{voteEnum}' has no configured options.", nameof(voteEnum));

            int generation = ++voteGeneration;
            events.VoteTotals.Clear();
            for (int i = 0; i < optionCount; i++)
                events.VoteTotals.Add(0);
            foreach (PlayerData player in PlayerData.Players.Values)
                player.voteOption.Value = -1;

            events.SharedVoteHeader.Value = new SharedVoteHeader(voteEnum, NetworkHelper.ServerTime + duration);
            try {
                SharedGlobalEvents.Instance.TopMessage = "Voting in progress...";
                await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: token);
                return GetWinningOption(events);
            } finally {
                // Do not allow a cancelled/older vote to close a newer one.
                if (generation == voteGeneration)
                    events.SharedVoteHeader.Value = new SharedVoteHeader(VoteEnum.None);
            }
        }

        static int GetWinningOption(SharedGlobalEvents events) {
            int bestTotal = int.MinValue;
            int winner = -1;
            int ties = 0;
            for (int i = 0; i < events.VoteTotals.Count; i++) {
                if (events.VoteTotals[i] > bestTotal) {
                    bestTotal = events.VoteTotals[i];
                    winner = i;
                    ties = 1;
                } else if (events.VoteTotals[i] == bestTotal && UnityEngine.Random.Range(0, ++ties) == 0) {
                    winner = i;
                }
            }
            return winner;
        }
    }
}
