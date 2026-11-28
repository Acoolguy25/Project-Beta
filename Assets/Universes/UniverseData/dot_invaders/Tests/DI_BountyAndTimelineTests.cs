using System.Collections.Generic;
using NUnit.Framework;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    /// <summary>Send amounts, bounty placement and payout, and the match timeline's power history.</summary>
    public class DI_BountyAndTimelineTests {
        [Test]
        public void PartialSendsRoundUpAndNeverEmptyAGarrisonByAccident() {
            Assert.That(DI_Rules.TroopsToSend(30, 50), Is.EqualTo(15));
            Assert.That(DI_Rules.TroopsToSend(7, 25), Is.EqualTo(2));
            Assert.That(DI_Rules.TroopsToSend(1, 25), Is.EqualTo(1));
            Assert.That(DI_Rules.TroopsToSend(40, 75), Is.EqualTo(30));
            Assert.That(DI_Rules.TroopsToSend(0, 50), Is.Zero);
        }

        [Test]
        public void ZeroOrFullPercentSendsTheWholeGarrison() {
            Assert.That(DI_Rules.TroopsToSend(30, 0), Is.EqualTo(30), "older clients send no percent");
            Assert.That(DI_Rules.TroopsToSend(30, 100), Is.EqualTo(30));
            Assert.That(DI_Rules.TroopsToSend(30, 250), Is.EqualTo(30));
            Assert.That(DI_Rules.SendPercents[DI_Rules.SendPercents.Length - 1], Is.EqualTo(DI_Rules.FullSendPercent));
        }

        [Test]
        public void BountyGoesOnTheBaseBetweenTwoTeams() {
            Vector2[] positions = { new(0f, 0f), new(10f, 0f), new(20f, 0f), new(0f, 40f) };
            int[] teams = { 0, -1, 1, -1 };
            bool[] eligible = { false, true, false, true };
            Assert.That(DI_Bounty.PickBase(positions, teams, eligible, 1, new System.Random(1)), Is.EqualTo(1));
        }

        [Test]
        public void NoBountyWhileOnlyOneTeamIsOnTheBoard() {
            Vector2[] positions = { new(0f, 0f), new(10f, 0f) };
            Assert.That(DI_Bounty.PickBase(positions, new[] { 0, -1 }, new[] { false, true }, 3, new System.Random(1)), Is.EqualTo(-1));
        }

        [Test]
        public void BountyPicksOnlyAmongTheMostContestedBases() {
            Vector2[] positions = { new(0f, 0f), new(10f, 0f), new(20f, 0f), new(10f, 5f), new(10f, 60f), new(10f, 90f) };
            int[] teams = { 0, -1, 1, -1, -1, -1 };
            bool[] eligible = { false, true, false, true, true, true };
            var picked = new HashSet<int>();
            var random = new System.Random(7);
            for (int i = 0; i < 200; i++)
                picked.Add(DI_Bounty.PickBase(positions, teams, eligible, 2, random));
            Assert.That(picked, Is.EquivalentTo(new[] { 1, 3 }));
        }

        [Test]
        public void BountiesPayXpAndGoldPerClaim() {
            var rewards = new DI_MatchRewards();
            List<RewardLine> lines = rewards.Lines(new DI_MatchResult { placement = 2, teamCount = 4, bounties = 2 });
            RewardLine line = lines.Find(l => l.Label == "Bounties claimed");
            Assert.That(line.Xp, Is.EqualTo(2 * rewards.bountyXp));
            Assert.That(line.Gold, Is.EqualTo(2 * rewards.bountyGold));
            Assert.That(rewards.Lines(new DI_MatchResult { placement = 2, teamCount = 4 }).Exists(l => l.Label == "Bounties claimed"), Is.False);
        }

        [Test]
        public void BountyChallengeIsInThePool() {
            Assert.That(System.Array.Exists(DI_Retention.ChallengePool(), c => c.statKey == DI_Retention.Stats.Bounties), Is.True);
        }

        static Dictionary<int, DI_TeamPower> Power(params (int team, int troops)[] teams) {
            var power = new Dictionary<int, DI_TeamPower>();
            foreach ((int team, int troops) in teams)
                power[team] = new DI_TeamPower { troops = troops };
            return power;
        }

        [Test]
        public void HistorySamplesAtItsIntervalAndKeepsSharesSummingToOne() {
            var history = new DI_PowerHistory(2f);
            Assert.That(history.Record(0f, Power((0, 30), (100, 10))), Is.True);
            Assert.That(history.Record(1f, Power((0, 30), (100, 10))), Is.False);
            Assert.That(history.Record(2f, Power((0, 10), (100, 10), (101, 20))), Is.True);
            Assert.That(history.Teams, Is.EqualTo(new[] { 0, 100, 101 }));
            Assert.That(history.ShareAt(0, 0), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(history.ShareAt(0, 2), Is.Zero);
            float sum = 0f;
            for (int t = 0; t < history.Teams.Count; t++)
                sum += history.ShareAt(1, t);
            Assert.That(sum, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(history.Record(2.5f, Power((0, 1)), force: true), Is.True, "the final snapshot is always kept");
        }

        [Test]
        public void HistoryThinsItselfButKeepsTheStartAndTheEnd() {
            var history = new DI_PowerHistory(1f, 8);
            for (int s = 0; s < 40; s++)
                history.Record(s, Power((0, s + 1), (1, 41 - s)));
            history.Record(40f, Power((0, 41), (1, 1)), force: true);
            Assert.That(history.Count, Is.LessThanOrEqualTo(8));
            Assert.That(history.TimeAt(0), Is.Zero);
            Assert.That(history.Duration, Is.EqualTo(40f));
        }

        [Test]
        public void PeakAndLeadChangesDescribeTheMatch() {
            var history = new DI_PowerHistory(1f);
            history.Record(0f, Power((0, 10), (1, 10)));
            history.Record(1f, Power((0, 30), (1, 10)));
            history.Record(2f, Power((0, 10), (1, 30)));
            history.Record(3f, Power((0, 40), (1, 0)));
            Assert.That(history.TryPeak(0, out float share, out float time), Is.True);
            Assert.That(share, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(time, Is.EqualTo(3f));
            Assert.That(history.LeadChanges(), Is.EqualTo(2));
            Assert.That(history.TryPeak(5, out _, out _), Is.False);
        }

        [Test]
        public void TwoTeamsTradingTheTopSpotByATroopIsNotALeadChange() {
            var history = new DI_PowerHistory(1f);
            for (int s = 0; s < 20; s++)
                history.Record(s, s % 2 == 0 ? Power((0, 101), (1, 100)) : Power((0, 100), (1, 101)));
            Assert.That(history.LeadChanges(), Is.Zero);
            Assert.That(history.LeadChanges(0f), Is.EqualTo(19));
        }

        [Test]
        public void ClockShowsMinutesAndSeconds() {
            Assert.That(DI_Rules.FormatClock(187f), Is.EqualTo("3:07"));
            Assert.That(DI_Rules.FormatClock(0.2f), Is.EqualTo("0:01"));
            Assert.That(DI_Rules.FormatClock(-4f), Is.EqualTo("0:00"));
        }
    }
}
