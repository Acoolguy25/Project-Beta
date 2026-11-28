using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>The three escape minigames: what counts as a hit, what a mistake costs, and what each unlocks.</summary>
    public sealed class CH_PuzzleTests {
        const int Seed = 40213, Ana = 1;
        static readonly CH_PuzzleTuning Tuning = new();

        static CH_Night.Layout Layout() {
            Pose At(float x) => new(new Vector3(x, 0f, 0f), Quaternion.identity);
            return new CH_Night.Layout {
                boatPartSpots = new[] { At(10), At(20), At(30) }, relicSpots = new[] { At(40), At(50), At(60) }, musicBoxSpot = At(70), bellSpot = At(80),
                boatSlots = new[] { At(1), At(2), At(3), At(4) }, graves = Enumerable.Range(0, CH_Story.SingerCount).Select(i => At(100 + i)).ToArray(),
                lilyMarker = At(5), engine = At(6), bellLock = At(7)
            };
        }

        static CH_Night Night(CH_Phase phase) {
            var night = new CH_Night(Seed, Layout());
            night.Enter(CH_Phase.Stranded);
            if (phase != CH_Phase.Stranded) night.Enter(phase);
            return night;
        }

        static CH_Night.Station Station(CH_Night night, CH_PuzzleKind kind, int singer = -1) =>
            night.Stations.First(station => station.Kind == kind && (singer < 0 || station.Singer == singer));

        static void Fit(CH_Night night, params CH_ItemKind[] parts) {
            foreach (CH_ItemKind part in parts) {
                CH_Night.Item item = night.Items.First(i => i.Kind == part);
                CH_Night.Slot slot = night.Slots.First(s => s.Accepts == part && s.Kind != CH_SlotKind.Grave);
                Assert.IsTrue(night.TryPickUp(Ana, item.Id, out _, out _));
                Assert.IsTrue(night.TryPlace(Ana, slot.Id, out _));
            }
        }

        // ---- The engine --------------------------------------------------------------------------------------------------

        [Test] public void TheNeedleSweepsTheGaugeBackAndForth() {
            Assert.AreEqual(0f, CH_PuzzleRun.NeedleAt(0f, 1f), 1e-4f);
            Assert.AreEqual(1f, CH_PuzzleRun.NeedleAt(1f, 1f), 1e-4f);
            Assert.AreEqual(0.5f, CH_PuzzleRun.NeedleAt(1.5f, 1f), 1e-4f, "On its way back.");
            Assert.AreEqual(0.5f, CH_PuzzleRun.NeedleAt(0.25f, 2f), 1e-4f, "Twice as fast.");
        }

        [Test] public void TheEngineNeedsThreeHitsAndEachIsHarderThanTheLast() {
            Assert.Less(Tuning.ZoneWidth(2), Tuning.ZoneWidth(0), "The zone shrinks.");
            Assert.Greater(Tuning.SweepSpeed(2), Tuning.SweepSpeed(0), "The needle speeds up.");
            CH_Night night = Night(CH_Phase.Stranded);
            CH_Night.Station station = Station(night, CH_PuzzleKind.Engine);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            float now = 0f;
            for (int hit = 0; hit < Tuning.engineStrikes; hit++) {
                now += CH_PuzzleRun.ZoneCenter(Seed, hit, Tuning.ZoneWidth(hit)) / Tuning.SweepSpeed(hit);
                CH_PuzzleStep step = run.Strike(now);
                Assert.AreEqual(hit + 1 == Tuning.engineStrikes ? CH_PuzzleEvent.Solved : CH_PuzzleEvent.Progress, step.evt, $"hit {hit + 1}");
            }
            Assert.IsTrue(station.Done);
            Assert.IsTrue(night.EngineStarted);
        }

        [Test] public void AMissedStrikeCostsAHitAndCountsAsAMistake() {
            var station = Station(Night(CH_Phase.Stranded), CH_PuzzleKind.Engine);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            float centre = CH_PuzzleRun.ZoneCenter(Seed, 0, Tuning.ZoneWidth(0)), speed = Tuning.SweepSpeed(0);
            run.Strike(centre / speed);
            Assert.AreEqual(1, station.Progress);
            // Strike at the very start of the next sweep: the needle is at 0, far from the zone.
            CH_PuzzleStep miss = run.Strike(centre / speed + 0.01f);
            Assert.AreEqual(CH_PuzzleEvent.Mistake, miss.evt);
            Assert.IsTrue(miss.mistake);
            Assert.AreEqual(0, station.Progress, "The cord frays: back a hit.");
            Assert.AreEqual(1, run.Mistakes);
        }

        [Test] public void ALaggyStrikeStillCountsInsideTheForgivenessWindow() {
            var station = Station(Night(CH_Phase.Stranded), CH_PuzzleKind.Engine);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            float width = Tuning.ZoneWidth(0), centre = CH_PuzzleRun.ZoneCenter(Seed, 0, width), speed = Tuning.SweepSpeed(0);
            // The needle left the zone's far edge 0.05 s ago on the server, but the player pulled within the window.
            float leftZoneAt = (centre + width * 0.5f) / speed;
            Assert.AreEqual(CH_PuzzleEvent.Progress, run.Strike(leftZoneAt + Tuning.engineForgiveness * 0.5f).evt);
        }

        [Test] public void AStrikeStillCountsWhenTheNeedleCrossedAThinZoneInsideTheWindowButWasOutAtBothEnds() {
            var station = Station(Night(CH_Phase.Stranded), CH_PuzzleKind.Engine);
            station.Progress = 2; // the last, fastest and thinnest strike
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            float width = Tuning.ZoneWidth(2), centre = CH_PuzzleRun.ZoneCenter(Seed, 2, width), speed = Tuning.SweepSpeed(2);
            // The zone crosses the needle's path in width / speed seconds; pull when the needle's zone passage lies mid-window.
            float crossing = width / speed;
            Assert.Less(crossing, Tuning.engineForgiveness, "The case needs a zone thinner than the window.");
            float centred = centre / speed;
            float pull = centred + Tuning.engineForgiveness * 0.5f;
            Assert.Greater(Mathf.Abs(CH_PuzzleRun.NeedleAt(pull, speed) - centre), width * 0.5f, "Out of the zone at the pull.");
            Assert.Greater(Mathf.Abs(CH_PuzzleRun.NeedleAt(pull - Tuning.engineForgiveness, speed) - centre), width * 0.5f, "Out of the zone at the window's start.");
            Assert.AreEqual(CH_PuzzleEvent.Solved, run.Strike(pull).evt);
        }

        [Test] public void APullDeadOnTheCentreIsAHitForAnyDelayInsideTheWindowOnEveryStrike() {
            for (int strike = 0; strike < Tuning.engineStrikes; strike++) {
                float width = Tuning.ZoneWidth(strike), centre = CH_PuzzleRun.ZoneCenter(Seed, strike, width, Tuning.engineZoneMargin), speed = Tuning.SweepSpeed(strike);
                for (float delay = 0f; delay <= Tuning.engineForgiveness + 1e-3f; delay += 0.01f) {
                    var station = Station(Night(CH_Phase.Stranded), CH_PuzzleKind.Engine);
                    station.Progress = strike;
                    var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
                    CH_PuzzleStep step = run.Strike(centre / speed + delay);
                    Assert.AreNotEqual(CH_PuzzleEvent.Mistake, step.evt, $"Strike {strike + 1}, {delay * 1000f:0} ms after the needle crossed the zone's centre.");
                }
            }
        }

        [Test] public void HotMeansTheRingWouldOpenAndNothingElseDoes() {
            float range = Tuning.lockWarmRange, tolerance = Tuning.lockTolerance;
            Assert.AreEqual(1f, CH_PuzzleRun.Warmth(100f + tolerance, 100f, range, tolerance), 1e-5f, "Inside the tolerance is hot.");
            Assert.GreaterOrEqual(CH_PuzzleRun.Warmth(100f + tolerance, 100f, range, tolerance), CH_PuzzleRun.HotWarmth, "The edge of the tolerance reads HOT.");
            Assert.Less(CH_PuzzleRun.Warmth(100f + tolerance + 1f, 100f, range, tolerance), CH_PuzzleRun.HotWarmth, "One degree outside it does not (a live night set a ring 'HOT' while it was still refused).");
            Assert.Less(CH_PuzzleRun.Warmth(100f + tolerance + 4f, 100f, range, tolerance), 0.999f, "Just outside it is not hot.");
            Assert.Less(CH_PuzzleRun.Warmth(129f, 100f, range, tolerance), 0.999f, "29 degrees off is not hot (it used to read HOT).");
        }

        [Test] public void TheEngineGaugeTellsTheClientTheZoneButTheSweepRestartsEveryStrike() {
            var station = Station(Night(CH_Phase.Stranded), CH_PuzzleKind.Engine);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            CH_PuzzleStep opening = run.Opening(3f);
            Assert.AreEqual(CH_PuzzleEvent.Opened, opening.evt);
            Assert.AreEqual(Tuning.SweepSpeed(0), opening.sweepSpeed, 1e-4f);
            Assert.AreEqual(Tuning.ZoneWidth(0), opening.zoneWidth, 1e-4f);
            Assert.That(opening.zoneCenter, Is.InRange(0.1f, 0.9f));
        }

        // ---- The hymns ---------------------------------------------------------------------------------------------------

        [Test] public void ASingersHymnIsTheSameAllNightAndGrowsAsMoreAreFreed() {
            CH_Night night = Night(CH_Phase.Choir);
            int singer = night.Singers[0];
            CollectionAssert.AreEqual(CH_PuzzleRun.HymnSequence(Seed, singer, 4, 4), CH_PuzzleRun.HymnSequence(Seed, singer, 4, 4));
            var first = new CH_PuzzleRun(Station(night, CH_PuzzleKind.Hymn, singer), Seed, Tuning, 0f, hymnOrder: 0);
            var third = new CH_PuzzleRun(Station(night, CH_PuzzleKind.Hymn, singer), Seed, Tuning, 0f, hymnOrder: 2);
            Assert.AreEqual(Tuning.hymnBaseLength, first.Sequence.Length);
            Assert.AreEqual(Tuning.hymnBaseLength + 2, third.Sequence.Length);
        }

        [Test] public void NoHymnRepeatsOneNoteThreeTimes() {
            for (int seed = 1; seed < 300; seed++)
                for (int singer = 0; singer < CH_Story.SingerCount; singer++) {
                    int[] notes = CH_PuzzleRun.HymnSequence(seed, singer, 7, Tuning.hymnNotes);
                    Assert.IsTrue(notes.All(note => note >= 0 && note < Tuning.hymnNotes));
                    for (int i = 2; i < notes.Length; i++) Assert.IsFalse(notes[i] == notes[i - 1] && notes[i] == notes[i - 2], $"seed {seed}, singer {singer}");
                }
        }

        [Test] public void SingingTheHymnBackFreesTheSingerAndAWrongNoteStartsItAgain() {
            CH_Night night = Night(CH_Phase.Choir);
            int singer = night.Singers[0];
            CH_Night.Station station = Station(night, CH_PuzzleKind.Hymn, singer);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            int[] hymn = run.Sequence;
            Assert.AreEqual(CH_PuzzleEvent.Progress, run.Press(hymn[0]).evt);
            Assert.AreEqual(CH_PuzzleEvent.Progress, run.Press(hymn[1]).evt);
            CH_PuzzleStep wrong = run.Press((hymn[2] + 1) % Tuning.hymnNotes);
            Assert.AreEqual(CH_PuzzleEvent.Mistake, wrong.evt);
            Assert.AreEqual(0, station.Progress, "A wrong note starts the hymn again.");
            foreach (int note in hymn.Take(hymn.Length - 1)) Assert.AreEqual(CH_PuzzleEvent.Progress, run.Press(note).evt);
            Assert.AreEqual(CH_PuzzleEvent.Solved, run.Press(hymn[hymn.Length - 1]).evt);
            Assert.AreEqual(1, run.Mistakes);
            Assert.AreEqual(1, night.SingersFreed);
        }

        [Test] public void AHymnCanOnlyBeSungOnceTheRelicLiesOnTheGraveAndOnlyForTonightsSingers() {
            CH_Night night = Night(CH_Phase.Stranded);
            Fit(night, CH_Story.BoatParts);
            night.Enter(CH_Phase.Choir);
            int singer = night.Singers[0];
            Assert.IsFalse(night.IsAvailable(Station(night, CH_PuzzleKind.Hymn, singer)), "No relic yet.");
            CH_Night.Item relic = night.Items.First(i => i.Singer == singer);
            night.TryPickUp(Ana, relic.Id, out _, out _);
            night.TryPlace(Ana, night.Slots.First(s => s.Kind == CH_SlotKind.Grave && s.Singer == singer).Id, out _);
            Assert.IsTrue(night.IsAvailable(Station(night, CH_PuzzleKind.Hymn, singer)));
            Assert.AreEqual(0, night.SingersFreed, "Laying the relic does not free anyone.");
            int notTonight = Enumerable.Range(0, CH_Story.SingerCount).First(s => !night.IsSingerTonight(s));
            Assert.IsFalse(night.IsAvailable(Station(night, CH_PuzzleKind.Hymn, notTonight)));
        }

        // ---- The bell's locks --------------------------------------------------------------------------------------------

        [Test] public void TheDialIsColdFarFromTheAngleAndHotOnIt() {
            Assert.AreEqual(1f, CH_PuzzleRun.Warmth(200f, 200f, 110f), 1e-4f);
            Assert.AreEqual(0f, CH_PuzzleRun.Warmth(0f, 180f, 110f), 1e-4f);
            float previous = 1f;
            for (float away = 5f; away <= 110f; away += 15f) {
                float warmth = CH_PuzzleRun.Warmth(100f + away, 100f, 110f);
                Assert.Less(warmth, previous, $"{away} degrees away");
                previous = warmth;
            }
            Assert.AreEqual(CH_PuzzleRun.Warmth(10f, 350f, 110f), CH_PuzzleRun.Warmth(350f, 10f, 110f), 1e-4f, "It wraps round zero.");
        }

        [Test] public void EveryRingSitsFarFromTheOneBeforeSoTheDialCannotBeSpun() {
            for (int seed = 1; seed < 400; seed++) {
                float[] angles = CH_PuzzleRun.LockAngles(seed, Tuning.lockRings, Tuning.lockMinimumSeparation);
                Assert.AreEqual(Tuning.lockRings, angles.Length);
                for (int i = 1; i < angles.Length; i++)
                    Assert.GreaterOrEqual(CH_PuzzleRun.CircularDistance(angles[i], angles[i - 1]), Tuning.lockMinimumSeparation - 0.01f, $"seed {seed}");
            }
        }

        [Test] public void ALockOnlyTakesWithinTheToleranceAndAWrongOneClanks() {
            CH_Night night = Night(CH_Phase.Sermon);
            CH_Night.Station station = Station(night, CH_PuzzleKind.BellLock);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            float[] angles = CH_PuzzleRun.LockAngles(Seed, Tuning.lockRings, Tuning.lockMinimumSeparation);
            CH_PuzzleStep clank = run.Set(angles[0] + Tuning.lockTolerance + 3f);
            Assert.AreEqual(CH_PuzzleEvent.Mistake, clank.evt);
            Assert.AreEqual(0, station.Progress, "A wrong lock does not undo the ones already open.");
            Assert.AreEqual(CH_PuzzleEvent.Progress, run.Set(angles[0] + Tuning.lockTolerance * 0.9f).evt);
            Assert.AreEqual(CH_PuzzleEvent.Mistake, run.Set(angles[1] + 90f).evt);
            Assert.AreEqual(1, station.Progress);
            Assert.AreEqual(CH_PuzzleEvent.Progress, run.Set(angles[1]).evt);
            Assert.AreEqual(CH_PuzzleEvent.Solved, run.Set(angles[2]).evt);
            Assert.IsTrue(night.BellUnchained);
        }

        [Test] public void TheServerNeverSendsTheLocksAngles() {
            var station = Station(Night(CH_Phase.Sermon), CH_PuzzleKind.BellLock);
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            CH_PuzzleStep probe = run.Probe(10f);
            Assert.AreEqual(CH_PuzzleEvent.Warmth, probe.evt);
            Assert.That(probe.warmth, Is.InRange(0f, 1f));
            Assert.IsEmpty(probe.sequence ?? new int[0]);
        }

        [Test] public void TheBellStaysChainedUntilItsLocksArePicked() {
            CH_Night night = Night(CH_Phase.Sermon);
            CH_Night.Item bell = night.Items.First(i => i.Kind == CH_ItemKind.Bell);
            bell.State = CH_ItemState.Resting;
            Assert.IsFalse(night.CanTake(bell));
            Assert.IsFalse(night.TryPickUp(Ana, bell.Id, out _, out _));
            Assert.IsFalse(night.IsObjective(bell), "No arrow to a bell you cannot take.");
            CH_Night.Station station = Station(night, CH_PuzzleKind.BellLock);
            Assert.IsTrue(night.IsAvailable(station));
            var run = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            foreach (float angle in CH_PuzzleRun.LockAngles(Seed, Tuning.lockRings, Tuning.lockMinimumSeparation)) run.Set(angle);
            Assert.IsTrue(night.CanTake(bell));
            Assert.IsTrue(night.TryPickUp(Ana, bell.Id, out _, out _));
            Assert.IsFalse(night.IsAvailable(station), "A solved station is done.");
        }

        // ---- The night around them ----------------------------------------------------------------------------------------

        [Test] public void TheEngineWaitsForAllThreePartsAndTheLocksForActThree() {
            CH_Night night = Night(CH_Phase.Stranded);
            CH_Night.Station engine = Station(night, CH_PuzzleKind.Engine);
            Assert.IsFalse(night.IsAvailable(engine));
            Fit(night, CH_ItemKind.Fuel, CH_ItemKind.Toolbox);
            Assert.IsFalse(night.IsAvailable(engine), "Two of three.");
            Fit(night, CH_ItemKind.Lantern);
            Assert.IsTrue(night.IsAvailable(engine));
            Assert.IsFalse(night.IsAvailable(Station(night, CH_PuzzleKind.BellLock)), "Not before Act III.");
        }

        [Test] public void ProgressSurvivesSteppingAwayAndComingBack() {
            CH_Night night = Night(CH_Phase.Stranded);
            CH_Night.Station station = Station(night, CH_PuzzleKind.Engine);
            var first = new CH_PuzzleRun(station, Seed, Tuning, 0f);
            first.Strike(CH_PuzzleRun.ZoneCenter(Seed, 0, Tuning.ZoneWidth(0)) / Tuning.SweepSpeed(0));
            var second = new CH_PuzzleRun(station, Seed, Tuning, 50f);
            Assert.AreEqual(1, second.Progress, "A new run picks up where the station was left.");
        }

        [Test] public void SavedRecordCountsMinigamesAndNeverGoesNegative() {
            var save = new CH_SaveData();
            save.RecordPuzzle(CH_PuzzleKind.Hymn);
            save.RecordPuzzle(CH_PuzzleKind.Engine);
            save.RecordPuzzle(CH_PuzzleKind.BellLock);
            Assert.AreEqual(3, save.puzzlesSolved);
            Assert.AreEqual(1, save.hymnsSung);
            Assert.AreEqual(0, save.mistakesMade, "Mistakes are counted as they are made, not again when a minigame is solved.");
            save.mistakesMade = -4;
            save.hymnsSung = 99;
            save.Validate();
            Assert.AreEqual(3, save.hymnsSung, "No more hymns than minigames.");
            Assert.AreEqual(0, save.mistakesMade);
            Assert.AreEqual(3, save.ToBroadcast().puzzlesSolved);
        }

        [Test] public void TheHushMakesHimHearWalkersAndEvenThoseStandingStill() {
            Assert.Greater(Tuning.HushHearing(0f, 1.5f, 6f), 0f, "Standing still is heard in a Hush.");
            Assert.Greater(Tuning.HushHearing(3f, 1.5f, 6f), Tuning.HushHearing(0f, 1.5f, 6f));
            Assert.Greater(Tuning.HushHearing(9f, 1.5f, 6f), Tuning.HushHearing(3f, 1.5f, 6f));
            var monster = new CH_MonsterTuning();
            Assert.Greater(Tuning.HushHearing(3f, 1.5f, 6f), monster.HearingRange(3f, false), "A walker is heard further in a Hush.");
        }

        [Test] public void EveryStationKindHasATitleAPromptAndAHint() {
            foreach (CH_PuzzleKind kind in System.Enum.GetValues(typeof(CH_PuzzleKind))) {
                Assert.IsNotEmpty(CH_Story.StationTitle(kind, 2), kind.ToString());
                Assert.IsNotEmpty(CH_Story.StationPrompt(kind, 2), kind.ToString());
                Assert.IsNotEmpty(CH_Story.StationHint(kind), kind.ToString());
            }
            StringAssert.Contains(CH_Story.SingerName(2), CH_Story.StationPrompt(CH_PuzzleKind.Hymn, 2));
        }
    }
}
