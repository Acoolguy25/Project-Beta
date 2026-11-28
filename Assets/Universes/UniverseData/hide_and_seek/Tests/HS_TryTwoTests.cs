using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Tests {
    /// <summary>The rules added in the second retention pass: win counting, blend cancel, close-call cap, the daily twist and the round's end.</summary>
    public sealed class HS_WinRuleTests {
        [Test]
        public void StartingAndEarlySeekersWinWithTheirSide() {
            Assert.That(HS_Rules.SeekerWinCounts(false, 0f, 0), Is.True, "a starting seeker");
            Assert.That(HS_Rules.SeekerWinCounts(false, HS_Rules.LateJoinWinShare, 0), Is.True, "joined exactly at the limit");
        }

        [Test]
        public void ALateJoinerWinsOnlyWithATag() {
            Assert.That(HS_Rules.SeekerWinCounts(false, 0.99f, 0), Is.False, "joined in the last second");
            Assert.That(HS_Rules.SeekerWinCounts(false, 0.99f, 1), Is.True, "joined late but found someone");
        }

        [Test]
        public void ADealtHiderOrALeaverNeverWinsWithTheSeekers() {
            Assert.That(HS_Rules.SeekerWinCounts(true, 0f, 0), Is.False, "dealt in as a hider, rejoined as a seeker");
            Assert.That(HS_Rules.SeekerWinCounts(true, 0f, 3), Is.False, "tags do not undo a forfeit");
        }

        [Test]
        public void LeavingARoundEndsTheWinStreak() {
            var save = new HS_SaveData();
            save.RecordResult(false, true, 10, false);
            save.RecordResult(false, true, 10, false);
            Assert.That(save.winStreak, Is.EqualTo(2));
            save.RecordLeft();
            Assert.That(save.winStreak, Is.Zero);
            Assert.That(save.bestWinStreak, Is.EqualTo(2), "the record stays");
            Assert.That(save.roundsLeft, Is.EqualTo(1));
            save.roundsLeft = -3;
            save.Validate();
            Assert.That(save.roundsLeft, Is.Zero);
        }

        [Test]
        public void TauntMasterRaisesTheAllowance() {
            int more = HS_Rules.TauntsPerRound + HS_Retention.TauntMasterExtra;
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Hider, 99f, HS_Rules.TauntsPerRound), Is.False);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Hider, 99f, HS_Rules.TauntsPerRound, more), Is.True);
            Assert.That(HS_Rules.TauntRefusal(HS_Phase.Seeking, HS_Role.Hider, 99f, more, more), Is.Not.Null);
        }
    }

    public sealed class HS_AbilityRuleTests {
        [Test]
        public void ASecondPressEndsARunningBlend() {
            Assert.That(HS_Abilities.Cancels(HS_Role.Hider, HS_Ability.Blend, 3f), Is.True);
            Assert.That(HS_Abilities.Cancels(HS_Role.Hider, HS_Ability.Blend, 0f), Is.False, "not running: a normal press");
            Assert.That(HS_Abilities.Cancels(HS_Role.Hider, HS_Ability.Dash, 2f), Is.False, "a dash cannot be cut short");
            Assert.That(HS_Abilities.Cancels(HS_Role.Caught, HS_Ability.Blend, 3f), Is.False, "not the seekers' ability");
            Assert.That(HS_Abilities.Cancels(HS_Role.None, HS_Ability.Blend, 3f), Is.False);
            // A cancel is not a new use: the cooldown from the first press keeps running, and a fresh press is still refused.
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Hider, HS_Ability.Blend, true, 20f), Does.Contain("recharging"));
        }

        [Test]
        public void SpeedBurstsNeverSlowAPlayer() {
            var tuning = new HS_AbilityTuning { dash = null, rush = new HS_AbilitySpec(new HS_AbilityStats(10f, 2f, 0f), new HS_AbilityStats(8f, 2f, 0.5f)) };
            tuning.Validate();
            foreach (HS_Ability ability in new[] { HS_Ability.Dash, HS_Ability.Rush })
            foreach (bool upgraded in new[] { false, true })
                Assert.That(tuning.Stats(ability, upgraded).power, Is.GreaterThanOrEqualTo(1f), $"{ability} upgraded={upgraded}");
            Assert.That(new HS_AbilityTuning().Stats(HS_Ability.Dash, false).power, Is.GreaterThan(1f), "the defaults are real bursts");
        }
    }

    public sealed class HS_CloseCallCapTests {
        [Test]
        public void CloseCallsStopPayingAtTheRoundCap() {
            var tuning = new HS_ScoreTuning { closeCallRange = 9f, closeCallEscapeRange = 22f, closeCallCooldown = 8f, closeCallsPerRound = 4 };
            tuning.Validate();
            HS_CloseCall call = HS_CloseCall.Fresh;
            int paid = 0;
            float t = 0f;
            // An idle seeker, and a hider who steps in and out of reach every 10 s for a whole round.
            for (int i = 0; i < 15; i++) {
                call.Step(5f, t, tuning);
                if (call.Step(30f, t + 1f, tuning)) paid++;
                t += 10f;
            }
            Assert.That(paid, Is.EqualTo(4));
            Assert.That(call.paid, Is.EqualTo(4));
        }
    }

    public sealed class HS_DailyTests {
        static readonly int Start = RetentionDay.Of(new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc));

        [Test]
        public void TheFeaturedEventIsTheSameAllDayAndNeverTwoDaysRunning() {
            var seen = new HashSet<HS_RoundEvent>();
            for (int day = Start - 30; day < Start + 300; day++) {
                HS_RoundEvent today = HS_Daily.Featured(day);
                Assert.That(today, Is.Not.EqualTo(HS_RoundEvent.None));
                Assert.That(today, Is.Not.EqualTo(HS_Daily.Featured(day + 1)), $"day {day}");
                Assert.That(HS_Daily.Featured(day), Is.EqualTo(today), "the pick is pure");
                seen.Add(today);
            }
            Assert.That(seen.Count, Is.EqualTo(3), "every event gets its day");
            DateTime morning = RetentionDay.Start(Start).AddHours(1), night = RetentionDay.Start(Start).AddHours(23);
            Assert.That(HS_Daily.Today(morning), Is.EqualTo(HS_Daily.Today(night)));
        }

        [Test]
        public void TheFeaturedEventComesMostRoundsAndTheOthersStillHappen() {
            var tuning = new HS_EventTuning { chance = 1f, featuredShare = 0.6f };
            var random = new System.Random(1);
            var counts = new Dictionary<HS_RoundEvent, int>();
            HS_RoundEvent previous = HS_RoundEvent.None;
            const int rounds = 4000;
            for (int i = 0; i < rounds; i++) {
                HS_RoundEvent picked = HS_RoundEvents.Pick(random, tuning, previous, HS_RoundEvent.Blackout);
                counts[picked] = (counts.TryGetValue(picked, out int n) ? n : 0) + 1;
                previous = picked;
            }
            Assert.That(counts[HS_RoundEvent.Blackout] / (float)rounds, Is.EqualTo(0.6f).Within(0.03f), "the twist comes in most rounds");
            Assert.That(counts.GetValueOrDefault(HS_RoundEvent.Frenzy), Is.GreaterThan(rounds / 10), "frenzy still comes");
            Assert.That(counts.GetValueOrDefault(HS_RoundEvent.Shuffle), Is.GreaterThan(rounds / 10), "shuffle still comes");
            Assert.That(counts.GetValueOrDefault(HS_RoundEvent.None), Is.Zero, "chance 1 always gives an event");
        }

        [Test]
        public void TheFeaturedShareAndChanceAreRespectedAtTheirEnds() {
            var random = new System.Random(2);
            var always = new HS_EventTuning { chance = 0f, featuredShare = 1f };
            for (int i = 0; i < 20; i++)
                Assert.That(HS_RoundEvents.Pick(random, always, HS_RoundEvent.Shuffle, HS_RoundEvent.Shuffle), Is.EqualTo(HS_RoundEvent.Shuffle), "share 1: every round");
            var never = new HS_EventTuning { chance = 0f, featuredShare = 0f };
            for (int i = 0; i < 20; i++)
                Assert.That(HS_RoundEvents.Pick(random, never, HS_RoundEvent.None, HS_RoundEvent.Shuffle), Is.EqualTo(HS_RoundEvent.None), "share 0 and chance 0: nothing");
            Assert.That(HS_RoundEvents.Pick(random, new HS_EventTuning { chance = 0f }, HS_RoundEvent.None), Is.EqualTo(HS_RoundEvent.None), "no twist: the chance applies");
        }

        [Test]
        public void ItsPlayPaysDoubleOnlyWhileItRunsOnItsDay() {
            Assert.That(HS_Daily.Pay(25, HS_RoundEvent.Blackout, HS_RoundEvent.Blackout, HS_RoundEvent.Blackout, 2), Is.EqualTo(50));
            Assert.That(HS_Daily.Pay(25, HS_RoundEvent.Blackout, HS_RoundEvent.None, HS_RoundEvent.Blackout, 2), Is.EqualTo(25), "lights on");
            Assert.That(HS_Daily.Pay(25, HS_RoundEvent.Blackout, HS_RoundEvent.Blackout, HS_RoundEvent.Frenzy, 2), Is.EqualTo(25), "another day's twist");
            Assert.That(HS_Daily.Pay(15, HS_RoundEvent.Frenzy, HS_RoundEvent.Frenzy, HS_RoundEvent.Frenzy, 0), Is.EqualTo(15), "a bad multiplier pays plain");
            foreach (HS_RoundEvent kind in new[] { HS_RoundEvent.Blackout, HS_RoundEvent.Frenzy, HS_RoundEvent.Shuffle }) {
                Assert.That(HS_Daily.Title(kind), Is.Not.Empty);
                Assert.That(HS_Daily.Describe(kind), Does.Contain(HS_Daily.Bonus(kind)));
            }
        }
    }

    public sealed class HS_RoundEndTests {
        static HS_ScoreTuning Scoring() {
            var s = new HS_ScoreTuning();
            s.Validate();
            return s;
        }

        [Test]
        public void EndPointsCountTowardsTheMvp() {
            HS_ScoreTuning s = Scoring();
            var ledger = new HS_RoundLedger<string>();
            ledger.Add("seeker", s.tagPoints + s.firstFindPoints);          // 40 during the round
            ledger.Add("hider", 35);                                         // time hidden
            var results = new List<HS_RoundResult<string>> { new("seeker", false, false), new("hider", true, true) };
            var paid = new List<string>();
            bool any = HS_RoundEnd.Settle(ledger, results, s, (p, points, reason) => { ledger.Add(p, points); paid.Add($"{p}:{reason}"); }, out string mvp, out int mvpPoints);
            Assert.That(any, Is.True);
            Assert.That(mvp, Is.EqualTo("hider"), "35 + Survived beats 40");
            Assert.That(mvpPoints, Is.EqualTo(35 + s.survivedPoints));
            Assert.That(paid, Is.EqualTo(new[] { "hider:Survived" }), "a seeker who lost gets no end points");
        }

        [Test]
        public void WinningSeekersGetEveryoneFoundAndLosersNothing() {
            HS_ScoreTuning s = Scoring();
            Assert.That(HS_RoundEnd.EndPoints(false, true, s, out string reason), Is.EqualTo(s.seekerWinPoints));
            Assert.That(reason, Is.EqualTo("Everyone found"));
            Assert.That(HS_RoundEnd.EndPoints(false, false, s, out reason), Is.Zero);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void TheCardPinsTheResultAndTheMvpAndAlwaysPaysSomething() {
            HS_ScoreTuning s = Scoring();
            List<RewardLine> won = HS_RoundEnd.Lines(true, true, true, 3, 120, true, 120, s);
            Assert.That(won.Where(l => l.Pinned).Select(l => l.Label), Is.EqualTo(new[] { "Survived", "Round MVP" }));
            Assert.That(won.Any(l => l.Label == "Win streak" && l.Xp == s.WinStreakXp(3)), Is.True);
            Assert.That(won.Single(l => l.Label == "Round points").Detail, Does.Contain("new best"));
            List<RewardLine> lost = HS_RoundEnd.Lines(false, false, false, 0, 0, false, 120, s);
            Assert.That(lost.Any(l => l.Pinned), Is.False);
            Assert.That(lost.Sum(l => l.Xp), Is.EqualTo(s.playedXp), "a lost round still pays Round played");
            Assert.That(HS_RoundEnd.Title(false, true, false), Is.EqualTo("Everyone found!"));
            Assert.That(HS_RoundEnd.Title(false, false, false), Is.EqualTo("Round over"));
        }

        [Test]
        public void TheLastHiderIsPaidOnceAndOnlyFromTwoHiders() {
            Assert.That(HS_RoundEnd.LastHiderDue(false, 3, 1), Is.True);
            Assert.That(HS_RoundEnd.LastHiderDue(true, 3, 1), Is.False, "paid already");
            Assert.That(HS_RoundEnd.LastHiderDue(false, 1, 1), Is.False, "a lone hider was always the last");
            Assert.That(HS_RoundEnd.LastHiderDue(false, 3, 2), Is.False);
        }

        [Test]
        public void AFlashlightAssistNeedsSomeoneElsesBeamInTime() {
            Assert.That(HS_RoundEnd.FlashAssist(false, true, 10f, 9f, 3f), Is.True);
            Assert.That(HS_RoundEnd.FlashAssist(false, true, 12.5f, 9f, 3f), Is.False, "the dazzle wore off too long ago");
            Assert.That(HS_RoundEnd.FlashAssist(true, true, 10f, 9f, 3f), Is.False, "your own beam is not an assist");
            Assert.That(HS_RoundEnd.FlashAssist(false, false, 10f, 9f, 3f), Is.False, "the beam's owner left");
        }

        [Test]
        public void AShufflePingsOnlyHidersWhoStayedPutInTheOpen() {
            Assert.That(HS_RoundEnd.PingedAfterShuffle(false, true, false), Is.True);
            Assert.That(HS_RoundEnd.PingedAfterShuffle(true, true, false), Is.False, "moved rooms");
            Assert.That(HS_RoundEnd.PingedAfterShuffle(false, false, false), Is.False, "found meanwhile");
            Assert.That(HS_RoundEnd.PingedAfterShuffle(false, true, true), Is.False, "blended in");
        }
    }
}
