using NUnit.Framework;

namespace Universes.UniverseData.hide_and_seek.Tests {
    public sealed class HS_ScoringTests {
        static HS_ScoreTuning Tuning() {
            var t = new HS_ScoreTuning { closeCallRange = 9f, closeCallEscapeRange = 22f, closeCallCooldown = 8f };
            t.Validate();
            return t;
        }

        [Test]
        public void CloseCallPaysOnceWhenTheSeekerLeaves() {
            HS_ScoreTuning t = Tuning();
            HS_CloseCall call = HS_CloseCall.Fresh;
            Assert.That(call.Step(30f, 0f, t), Is.False, "nobody close");
            Assert.That(call.Step(8f, 1f, t), Is.False, "close: armed, not paid yet");
            Assert.That(call.Step(15f, 2f, t), Is.False, "between ranges: still close");
            Assert.That(call.Step(25f, 3f, t), Is.True, "got away");
            Assert.That(call.Step(30f, 4f, t), Is.False, "paid once");
        }

        [Test]
        public void CloseCallWaitsForItsCooldown() {
            HS_ScoreTuning t = Tuning();
            HS_CloseCall call = HS_CloseCall.Fresh;
            call.Step(5f, 0f, t);
            Assert.That(call.Step(25f, 1f, t), Is.True);
            call.Step(5f, 3f, t);
            Assert.That(call.Step(25f, 4f, t), Is.False, "within the cooldown");
            call.Step(5f, 10f, t);
            Assert.That(call.Step(25f, 11f, t), Is.True, "after the cooldown");
        }

        [Test]
        public void SurvivalPointsCountWholeIntervals() {
            var t = new HS_ScoreTuning { survivalPoints = 5, survivalInterval = 10f };
            Assert.That(t.SurvivalPointsFor(9.9f), Is.Zero);
            Assert.That(t.SurvivalPointsFor(10f), Is.EqualTo(5));
            Assert.That(t.SurvivalPointsFor(149f), Is.EqualTo(70));
            Assert.That(t.SurvivalPointsFor(-3f), Is.Zero);
        }

        [Test]
        public void WinStreakBonusStartsAtTwoAndCaps() {
            var t = new HS_ScoreTuning { winStreakXpPerWin = 10, winStreakCap = 5 };
            Assert.That(t.WinStreakXp(0), Is.Zero);
            Assert.That(t.WinStreakXp(1), Is.Zero);
            Assert.That(t.WinStreakXp(2), Is.EqualTo(20));
            Assert.That(t.WinStreakXp(9), Is.EqualTo(50));
        }

        [Test]
        public void ValidateKeepsEscapeBeyondCloseRange() {
            var t = new HS_ScoreTuning { closeCallRange = 30f, closeCallEscapeRange = 10f, survivalInterval = 0f, xpPerPoint = float.NaN };
            t.Validate();
            Assert.That(t.closeCallEscapeRange, Is.GreaterThanOrEqualTo(t.closeCallRange));
            Assert.That(t.survivalInterval, Is.GreaterThanOrEqualTo(1f));
            Assert.That(t.Xp(10), Is.EqualTo(10));
        }

        [Test]
        public void MvpIsTheTopScorerAndTiesGoToWhoGotThereFirst() {
            var ledger = new HS_RoundLedger<string>();
            Assert.That(ledger.TryGetMvp(out _, out _), Is.False, "nobody scored");
            ledger.Add("a", 0);
            Assert.That(ledger.TryGetMvp(out _, out _), Is.False, "zero points is no MVP");
            ledger.Add("a", 30);
            ledger.Add("b", 20);
            ledger.Add("b", 10);
            Assert.That(ledger.TryGetMvp(out string mvp, out int points), Is.True);
            Assert.That(mvp, Is.EqualTo("a"));
            Assert.That(points, Is.EqualTo(30));
            ledger.Add("b", 5);
            Assert.That(ledger.TryGetMvp(out mvp, out _), Is.True);
            Assert.That(mvp, Is.EqualTo("b"));
            Assert.That(ledger.Add("c", -5), Is.Zero, "negative points are ignored");
            ledger.Clear();
            Assert.That(ledger.PointsOf("b"), Is.Zero);
        }

        [Test]
        public void EventsNeverRepeatBackToBackAndRespectChance() {
            var tuning = new HS_EventTuning { chance = 1f };
            var random = new System.Random(11);
            HS_RoundEvent previous = HS_RoundEvent.None;
            for (int i = 0; i < 200; i++) {
                HS_RoundEvent next = HS_RoundEvents.Pick(random, tuning, previous);
                Assert.That(next, Is.Not.EqualTo(HS_RoundEvent.None));
                Assert.That(next, Is.Not.EqualTo(previous));
                previous = next;
            }
            var never = new HS_EventTuning { chance = 0f };
            for (int i = 0; i < 50; i++) Assert.That(HS_RoundEvents.Pick(random, never, HS_RoundEvent.None), Is.EqualTo(HS_RoundEvent.None));
        }

        [Test]
        public void OnlyWeightedEventsArePickedAndARepeatBeatsNothing() {
            var onlyBlackout = new HS_EventTuning { chance = 1f, weights = new UnityEngine.Vector3(1f, 0f, 0f) };
            var random = new System.Random(3);
            Assert.That(HS_RoundEvents.Pick(random, onlyBlackout, HS_RoundEvent.None), Is.EqualTo(HS_RoundEvent.Blackout));
            Assert.That(HS_RoundEvents.Pick(random, onlyBlackout, HS_RoundEvent.Blackout), Is.EqualTo(HS_RoundEvent.Blackout));
        }

        [Test]
        public void EventStartsInsideTheWindowAndLeavesTimeToFinish() {
            var tuning = new HS_EventTuning { startWindow = new UnityEngine.Vector2(0.25f, 0.55f), blackoutSeconds = 18 };
            var random = new System.Random(5);
            for (int i = 0; i < 100; i++) {
                float t = HS_RoundEvents.StartAfter(random, tuning, HS_RoundEvent.Blackout, 150f);
                Assert.That(t, Is.InRange(37.5f, 82.5f));
            }
            Assert.That(HS_RoundEvents.StartAfter(random, tuning, HS_RoundEvent.Blackout, 20f), Is.LessThanOrEqualTo(1f));
        }
    }
}
