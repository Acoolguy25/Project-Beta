#if UNITY_SERVER
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RyanAssets.DataService;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Match setup: which board is played (rotation and the vote between matches) and building it.
    public sealed partial class DI_ServerRunner {
        const string BoardVoteTitle = "Choose the next board";
        const string BoardVoteDescription = "The board with the most votes is played next.";

        // Seeds every generated board; boards differ every match even on the same layout.
        readonly System.Random boardRandom = new(unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Ticks.GetHashCode()));
        DI_BoardLayout layout = DI_BoardLayouts.All[0];
        int layoutIndex = -1;
        // Chosen by the vote (or rotation) at the end of a match; survives the round reset.
        int nextLayoutIndex = -1;

        internal DI_BoardLayout Layout => layout;

        void InitializeMatch() => InitializeMatch(TakeNextLayout());

        void InitializeMatch(int boardIndex) {
            // Match bonuses come only from the newly generated bases.
            teamSpeedBases.Clear();
            playerDamageMultipliers.Clear();
            playerProductionMultipliers.Clear();
            simulationAccumulator = 0f;
            snapshotTimer = 0f;
            dots.Clear();
            npcTeams.Clear();
            playerTeams.Clear();
            dotInvadersTeams.Clear();
            announcedEliminations.Clear();
            clashPositions.Clear();
            matchRecords.Clear();
            matchSummaries.Clear();
            ClearMatchProgression();
            nextTeamId = 0;
            nextDotId = 0;
            revision = 0;
            winningTeamId = -1;
            matchElapsed = 0f;
            matchInProgress = false;

            layoutIndex = Mathf.Clamp(boardIndex, 0, DI_BoardLayouts.All.Count - 1);
            layout = DI_BoardLayouts.All[layoutIndex];
            secondsRemaining = Mathf.Min(layout.matchSeconds, matchDurationSeconds);
            BuildBoard(layout, boardRandom.Next());
            initialized = true;

            ChooseNpcTier();
            foreach (PlayerData player in PlayerData.Players.Values)
                AssignPlayer(player);

            SpawnNpcTeams(layout.npcTeams);
            AnnounceNewMatch();
            matchInProgress = true;
            BeginMatchForPlayers();
            UpdateTopMessage();
            BroadcastState();
        }

        /// <summary>The voted board, else the next one in rotation (a random one for the first match).</summary>
        int TakeNextLayout() {
            int count = DI_BoardLayouts.All.Count;
            int next = nextLayoutIndex >= 0 ? nextLayoutIndex
                : layoutIndex >= 0 ? (layoutIndex + 1) % count
                : boardRandom.Next(count);
            nextLayoutIndex = -1;
            return next;
        }

        /// <summary>Replaces the board with a fresh one generated from <paramref name="board"/>.</summary>
        void BuildBoard(DI_BoardLayout board, int seed) {
            DI_BoardPlan plan = DI_BoardGenerator.Generate(board, seed);
            bases.Clear();
            links.Clear();
            for (int i = 0; i < plan.Count; i++) {
                DI_BaseKind kind = plan.kinds[i];
                bases.Add(new BaseState {
                    position = plan.positions[i],
                    spawnable = plan.spawnable[i],
                    isTurret = kind == DI_BaseKind.Turret,
                    isSuperProducer = kind == DI_BaseKind.Super,
                    isSpeedBase = kind == DI_BaseKind.Speed,
                    isFactory = kind == DI_BaseKind.Factory,
                    isFortress = kind == DI_BaseKind.Fortress,
                });
            }
            links.AddRange(plan.links);
        }

        /// <summary>
        /// Lets the players pick the next board from a few (never the one just played). Nobody voting, or a
        /// tie, falls back to a random top option; with nobody online the first option is simply played.
        /// </summary>
        async UniTask VoteNextBoardAsync(CancellationToken token) {
            int[] options = DI_BoardLayouts.VoteOptions(boardVoteOptions, layoutIndex, boardRandom);
            if (options.Length == 0)
                return;
            if (boardVoteSeconds <= 0 || options.Length == 1 || GetActivePlayersCount() == 0) {
                nextLayoutIndex = options[0];
                return;
            }

            var titles = new string[options.Length];
            var descriptions = new string[options.Length];
            for (int i = 0; i < options.Length; i++) {
                DI_BoardLayout option = DI_BoardLayouts.All[options[i]];
                titles[i] = option.title;
                descriptions[i] = option.Describe();
            }
            VoteResult result = await ServerVote.StartDynamicVote(BoardVoteTitle, BoardVoteDescription, titles, descriptions,
                boardVoteSeconds, token);
            nextLayoutIndex = options[PickVotedOption(result)];
            DI_BoardLayout chosen = DI_BoardLayouts.All[nextLayoutIndex];
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                $"<color={DI_Rules.ChatAccentHex}>Next board: {chosen.title}.</color> {chosen.tagline}", SystemMessageSource.CustomMessage));
        }

        /// <summary>The winning option, a random one among tied leaders, or the first when nobody voted.</summary>
        int PickVotedOption(VoteResult result) {
            if (result.winner >= 0)
                return result.winner;
            int top = result.TopVotes;
            if (top <= 0 || result.totals == null)
                return 0;
            int leaders = 0;
            foreach (int total in result.totals)
                if (total == top) leaders++;
            int pick = boardRandom.Next(leaders);
            for (int i = 0; i < result.totals.Length; i++)
                if (result.totals[i] == top && pick-- == 0)
                    return i;
            return 0;
        }

        /// <summary>Moves the next match to a board (NextBoard command); false for an unknown id.</summary>
        internal bool SetNextBoard(string boardId) {
            int index = DI_BoardLayouts.IndexOf(boardId);
            if (index < 0)
                return false;
            nextLayoutIndex = index;
            return true;
        }

        static bool IsSpecial(BaseState state) =>
            state.isTurret || state.isSuperProducer || state.isSpeedBase || state.isFactory || state.isFortress;

        int Capacity(BaseState state) => state.isSuperProducer ? superMaxCapacity : maxCapacity;

        /// <summary>Troops per second the base trains right now, perks and Overdrive included.</summary>
        float ProductionRate(BaseState state) => (state.isSuperProducer
            ? DI_Rules.SuperProductionRate(state.uninterruptedSeconds, superProductionSpeed, superSpeedupSeconds)
            : DI_Rules.BaseProductionRate(productionSpeed, state.isFactory, factoryMultiplier)) *
            ProductionMultiplier(state);

        /// <summary>Every multiplier on a base's training: the admin's player multiplier, perks and Overdrive.</summary>
        float ProductionMultiplier(BaseState state) =>
            GetPlayerProductionMultiplier(state.ownerClientId) * TeamProductionMultiplier(state.teamId);

        /// <summary>Attackers this garrison needs per defender: fortress and the holder's defence perk.</summary>
        float DefenceOf(BaseState state) =>
            DI_Rules.DefenceMultiplier(state.isFortress, fortressMultiplier, EffectsOf(state.teamId).defence);

        static void InterruptProduction(BaseState state) {
            state.uninterruptedSeconds = 0f;
            state.actionTimer = 0f;
        }
    }
}
#endif
