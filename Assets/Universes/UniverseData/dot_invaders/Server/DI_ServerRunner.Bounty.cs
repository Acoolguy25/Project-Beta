#if UNITY_SERVER
using FishNet;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Bounties: a contested plain base is marked for a short while; the team that captures it gets bonus troops.
    public sealed partial class DI_ServerRunner {
        [Header("Bounties")]
        [SerializeField] DI_BountyTuning bounty = new();

        /// <summary>The base the bounty is on, or -1 while none is up.</summary>
        int bountyBaseId = -1;
        float bountySecondsLeft;
        /// <summary>Match time (<see cref="matchElapsed"/>) at which the next bounty may be posted.</summary>
        float nextBountyAt;

        void ResetBounty() {
            bountyBaseId = -1;
            bountySecondsLeft = 0f;
            nextBountyAt = bounty.firstAfterSeconds;
        }

        void UpdateBounty(float deltaTime) {
            if (!bounty.enabled)
                return;
            if (bountyBaseId >= 0) {
                bountySecondsLeft -= deltaTime;
                if (bountySecondsLeft <= 0f)
                    EndBounty(BountyMessage("The bounty expired unclaimed.", false));
                return;
            }
            if (matchElapsed >= nextBountyAt && secondsRemaining >= bounty.minSecondsLeft)
                TryPostBounty();
        }

        /// <summary>Marks the most contested plain base. Returns false when no base qualifies (it retries shortly).</summary>
        bool TryPostBounty() {
            int count = bases.Count;
            var positions = new Vector2[count];
            var teams = new int[count];
            var eligible = new bool[count];
            for (int i = 0; i < count; i++) {
                positions[i] = bases[i].position;
                teams[i] = bases[i].teamId;
                // Plain bases only (special bases already have labels and are objectives in their own right), and
                // never a team's last base: a bounty there would just mark who is about to be eliminated.
                eligible[i] = !IsSpecial(bases[i]) && (bases[i].teamId < 0 || TeamBaseCount(bases[i].teamId) > 1);
            }
            int picked = DI_Bounty.PickBase(positions, teams, eligible, bounty.candidatePool, boardRandom);
            if (picked < 0) {
                nextBountyAt = matchElapsed + BountyRetrySeconds;
                return false;
            }
            bountyBaseId = picked;
            bountySecondsLeft = bounty.durationSeconds;
            AnnounceBounty(BountyMessage(
                $"A bounty is up: capture the marked base within {Mathf.RoundToInt(bounty.durationSeconds)}s for +{bounty.bonusTroops} troops.", true));
            BroadcastState();
            return true;
        }

        const float BountyRetrySeconds = 5f;

        /// <summary>Called when <paramref name="captured"/> changes hands: pays the bounty if it was on that base.</summary>
        void ClaimBountyIfMarked(int capturedBaseId, int ownerClientId, int teamId) {
            if (capturedBaseId != bountyBaseId)
                return;
            BaseState captured = bases[capturedBaseId];
            captured.troops += bounty.bonusTroops;
            if (ownerClientId >= 0) {
                matchRecords.TryGetValue(ownerClientId, out MatchRecord record);
                record.bounties++;
                matchRecords[ownerClientId] = record;
                CountStat(ownerClientId, DI_Retention.Stats.Bounties);
            }
            string team = ColoredTeamName(teamId);
            EndBounty($"{team} <color={DI_Rules.ChatMutedHex}>claimed the bounty:</color> <color={BountyHex}>+{bounty.bonusTroops} troops</color>");
        }

        void EndBounty(string announcement) {
            bountyBaseId = -1;
            bountySecondsLeft = 0f;
            nextBountyAt = matchElapsed + bounty.intervalSeconds;
            AnnounceBounty(announcement);
        }

        /// <summary>A chat line to everyone; skipped while no server runs (EditMode tests drive the rules directly).</summary>
        static void AnnounceBounty(string text) {
            if (InstanceFinder.IsServerStarted)
                ServerChat.SendSystemMessage(new SystemMessageBroadcast(text, SystemMessageSource.CustomMessage));
        }

        int TeamBaseCount(int teamId) {
            int count = 0;
            foreach (BaseState state in bases)
                if (state.teamId == teamId)
                    count++;
            return count;
        }

        /// <summary>Gold, like the bounty mark on the board.</summary>
        const string BountyHex = "#FFC94A";

        static string BountyMessage(string text, bool highlight) =>
            $"<color={(highlight ? BountyHex : DI_Rules.ChatMutedHex)}>{text}</color>";

        /// <summary>Admin test hook (Bounty command): posts a bounty now, replacing any that is up.</summary>
        internal bool PostBountyNow() {
            if (!matchInProgress)
                return false;
            bountyBaseId = -1;
            return TryPostBounty();
        }

        void WriteBounty(ref DI_StateBroadcast state) {
            state.bountyActive = bountyBaseId >= 0;
            state.bountyBaseId = bountyBaseId;
            state.bountySeconds = Mathf.Max(0f, bountySecondsLeft);
            state.bountyTroops = bounty.bonusTroops;
        }
    }
}
#endif
