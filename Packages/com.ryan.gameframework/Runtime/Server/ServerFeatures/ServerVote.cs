using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    /// <summary>Outcome of a vote, with the raw totals so a game can apply its own tie rules.</summary>
    public struct VoteResult {
        /// <summary>The option with the most votes, or -1 when nobody voted or the top options tied.</summary>
        public int winner;
        public int[] totals;
        public bool tied;
        public int TopVotes => totals == null || totals.Length == 0 ? 0 : totals.Max();
    }

    public static class ServerVote {
        static int voteGeneration;
        static UniTaskCompletionSource skipVoteCompletion;
        // Who may vote in the running vote. Null means every player.
        static Func<PlayerData, bool> voterFilter;
        // Dynamic votes end as soon as every eligible voter has chosen.
        static bool completeWhenAllVoted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            skipVoteCompletion = null;
            voterFilter = null;
            completeWhenAllVoted = false;
            InstanceFinder.ServerManager.RegisterBroadcast<VoteRequest>(OnVoteRequest, true);
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
        }

        static bool CanVote(PlayerData player) => voterFilter == null || voterFilter(player);

        static int CurrentOptionCount() {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            VoteEnum voteId = events.SharedVoteHeader.Value.voteId;
            return voteId == VoteEnum.Dynamic ? events.DynamicVote.Value.OptionCount : VoteDeclarations.GetOptionCount(voteId);
        }

        static void OnVoteRequest(NetworkConnection connection, VoteRequest request, FishNet.Transporting.Channel channel) {
            if (!SharedGlobalEvents.isVoting || !PlayerData.TryGetPlayerData(connection, out PlayerData player) || !CanVote(player))
                return;

            int optionCount = CurrentOptionCount();
            if (request.optionId < VoteRequest.SkipVoteOptionId || request.optionId >= optionCount)
                return;

            if (request.optionId == VoteRequest.SkipVoteOptionId)
                SetPlayerSkipVote(player, true);
            else
                SetPlayerVote(player, request.optionId, optionCount);
            CompleteVoteIfDone();
        }

        static void OnPlayerRemoved(PlayerData player) {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            if (!SharedGlobalEvents.isVoting)
                return;

            SetPlayerVote(player, -1, events.VoteTotals.Count);
            SetPlayerSkipVote(player, false);
            CompleteVoteIfDone();
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

        static void SetPlayerSkipVote(PlayerData player, bool shouldSkip) {
            if (player.skipVote.Value == shouldSkip)
                return;

            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            events.SkipVoteCount.Value = Mathf.Max(0, events.SkipVoteCount.Value + (shouldSkip ? 1 : -1));
            player.skipVote.Value = shouldSkip;
        }

        static void CompleteVoteIfDone() {
            if (!SharedGlobalEvents.isVoting || PlayerData.Players.Count == 0)
                return;

            int voters = 0, skipped = 0, decided = 0;
            foreach (PlayerData player in PlayerData.Players.Values) {
                if (!CanVote(player))
                    continue;
                voters++;
                if (player.skipVote.Value)
                    skipped++;
                if (player.skipVote.Value || player.voteOption.Value >= 0)
                    decided++;
            }
            if (voters == 0)
                return;
            if (skipped >= voters || (completeWhenAllVoted && decided >= voters))
                skipVoteCompletion?.TrySetResult();
        }

        /// <summary>Runs a vote declared in <see cref="VoteDeclarations"/> and returns the winning option (ties broken at random).</summary>
        public static async UniTask<int> StartVote(VoteEnum voteEnum, float duration, CancellationToken token) {
            if (voteEnum == VoteEnum.Dynamic)
                throw new ArgumentException("Use StartDynamicVote for runtime-defined votes.", nameof(voteEnum));
            int optionCount = VoteDeclarations.GetOptionCount(voteEnum);
            if (voteEnum == VoteEnum.None || optionCount == 0)
                throw new ArgumentException($"Vote '{voteEnum}' has no configured options.", nameof(voteEnum));

            VoteResult result = await RunVote(voteEnum, optionCount, duration, null, false, token);
            return PickRandomTopOption(result.totals);
        }

        /// <summary>
        /// Runs a vote whose title and options are only known at runtime, such as an accusation. Only players
        /// accepted by <paramref name="canVote"/> may vote, and the vote ends early once all of them chose.
        /// The result's winner is -1 when nobody voted or the top options tied.
        /// </summary>
        public static async UniTask<VoteResult> StartDynamicVote(string title, string description, string[] options,
            string[] optionDescriptions, float duration, CancellationToken token, Func<PlayerData, bool> canVote = null) {
            if (options == null || options.Length == 0)
                throw new ArgumentException("A dynamic vote needs at least one option.", nameof(options));

            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            int[] voterIds = canVote == null ? Array.Empty<int>()
                : PlayerData.Players.Values.Where(canVote).Select(p => p.Owner.ClientId).ToArray();
            events.DynamicVote.Value = new SharedDynamicVote {
                title = title,
                description = description,
                options = options,
                optionDescriptions = optionDescriptions ?? new string[options.Length],
                voterClientIds = voterIds,
            };
            return await RunVote(VoteEnum.Dynamic, options.Length, duration, canVote, true, token);
        }

        static async UniTask<VoteResult> RunVote(VoteEnum voteEnum, int optionCount, float duration,
            Func<PlayerData, bool> canVote, bool endWhenAllVoted, CancellationToken token) {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            Debug.Assert(events != null, "Cannot start a vote without SharedGlobalEvents.");

            int generation = ++voteGeneration;
            skipVoteCompletion = new UniTaskCompletionSource();
            voterFilter = canVote;
            completeWhenAllVoted = endWhenAllVoted;
            events.VoteTotals.Clear();
            events.SkipVoteCount.Value = 0;
            for (int i = 0; i < optionCount; i++)
                events.VoteTotals.Add(0);
            foreach (PlayerData player in PlayerData.Players.Values) {
                player.voteOption.Value = -1;
                player.skipVote.Value = false;
            }

            events.SharedVoteHeader.Value = new SharedVoteHeader(voteEnum, NetworkHelper.ServerTime + duration);
            try {
                SharedGlobalEvents.Instance.TopMessage = "Voting in progress...";
                await UniTask.WhenAny(
                    ServerRunner.Instance.AwaitTime((int)(duration * 1000), token).AsUniTask(),
                    skipVoteCompletion.Task);
                token.ThrowIfCancellationRequested();
                int[] totals = events.VoteTotals.ToArray();
                int top = totals.Length == 0 ? 0 : totals.Max();
                int topCount = totals.Count(t => t == top);
                bool tied = top > 0 && topCount > 1;
                return new VoteResult {
                    totals = totals,
                    tied = tied,
                    winner = top > 0 && !tied ? Array.IndexOf(totals, top) : -1,
                };
            } finally {
                // Do not allow a cancelled/older vote to close a newer one.
                if (generation == voteGeneration) {
                    events.SharedVoteHeader.Value = new SharedVoteHeader(VoteEnum.None);
                    if (voteEnum == VoteEnum.Dynamic)
                        events.DynamicVote.Value = new SharedDynamicVote();
                    skipVoteCompletion = null;
                    voterFilter = null;
                    completeWhenAllVoted = false;
                }
            }
        }

        static int PickRandomTopOption(int[] totals) {
            int bestTotal = int.MinValue;
            int winner = -1;
            int ties = 0;
            for (int i = 0; i < totals.Length; i++) {
                if (totals[i] > bestTotal) {
                    bestTotal = totals[i];
                    winner = i;
                    ties = 1;
                } else if (totals[i] == bestTotal && UnityEngine.Random.Range(0, ++ties) == 0) {
                    winner = i;
                }
            }
            return winner;
        }
    }
}
