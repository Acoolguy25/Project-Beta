using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Tests {
    /// <summary>The closet-door rules (<see cref="HS_Doors"/>) and the seekers' room search (<see cref="HS_SearchLog"/>).</summary>
    public sealed class HS_DoorsTests {
        static readonly Vector3 From = new(0f, 0f, 0f), To = new(4f, 0f, 0f);

        [Test]
        public void HidersWorkDoorsBothWaysWhileTheyHideAndWhileTheSeekersHunt() {
            foreach (HS_Phase phase in new[] { HS_Phase.Hiding, HS_Phase.Seeking }) {
                Assert.That(HS_Doors.Refusal(phase, HS_Role.Hider, false), Is.Null, $"{phase}: shut");
                Assert.That(HS_Doors.Refusal(phase, HS_Role.Hider, true), Is.Null, $"{phase}: open");
            }
        }

        [Test]
        public void SeekersOnlyOpenDoorsAndOnlyOnceReleased() {
            foreach (HS_Role seeker in new[] { HS_Role.Seeker, HS_Role.Caught }) {
                Assert.That(HS_Doors.Refusal(HS_Phase.Seeking, seeker, true), Is.Null, $"{seeker} opens");
                Assert.That(HS_Doors.Refusal(HS_Phase.Seeking, seeker, false), Is.Not.Null, $"{seeker} cannot shut a hider in or bluff");
                Assert.That(HS_Doors.Refusal(HS_Phase.Hiding, seeker, true), Is.Not.Null, $"{seeker} is blindfolded");
            }
            Assert.That(HS_Doors.Refusal(HS_Phase.Seeking, HS_Role.None, true), Is.Not.Null, "a spectator mid-round");
        }

        [Test]
        public void AnyoneMayPlayWithDoorsBetweenRounds() {
            foreach (HS_Phase phase in new[] { HS_Phase.Waiting, HS_Phase.Intermission, HS_Phase.Results })
            foreach (HS_Role role in new[] { HS_Role.None, HS_Role.Hider, HS_Role.Seeker })
                Assert.That(HS_Doors.Refusal(phase, role, false), Is.Null, $"{phase} {role}");
        }

        [Test]
        public void ReachIsMeasuredToTheLeafOnTheSameFloor() {
            Assert.That(HS_Doors.InReach(new Vector3(2f, 0f, 3f), From, To, 4f, 3f), Is.True, "in front of the middle");
            Assert.That(HS_Doors.InReach(new Vector3(2f, 0f, -1.5f), From, To, 4f, 3f), Is.True, "inside the closet");
            Assert.That(HS_Doors.InReach(new Vector3(7f, 0f, 0f), From, To, 4f, 3f), Is.True, "beside the free edge");
            Assert.That(HS_Doors.InReach(new Vector3(9f, 0f, 0f), From, To, 4f, 3f), Is.False, "too far past the edge");
            Assert.That(HS_Doors.InReach(new Vector3(2f, 9f, 1f), From, To, 4f, 3f), Is.False, "the floor above");
            Assert.That(HS_Doors.FlatDistanceToLeaf(new Vector3(-3f, 5f, 4f), From, To), Is.EqualTo(5f).Within(1e-4f), "to the hinge end, ignoring height");
        }

        [Test]
        public void ADoorWillNotShutOnSomeoneInTheDoorway() {
            Assert.That(HS_Doors.InDoorway(new Vector3(2f, 0f, 0.5f), From, To, 1.1f, 3f), Is.True);
            Assert.That(HS_Doors.InDoorway(new Vector3(2f, 0f, -1.6f), From, To, 1.1f, 3f), Is.False, "well inside");
            Assert.That(HS_Doors.InDoorway(new Vector3(2f, 9f, 0f), From, To, 1.1f, 3f), Is.False, "another floor");
        }

        [Test]
        public void ABluffPaysOnlyWhenASeekerOpensAnEmptyClosetItsHiderStillHidesBehind() {
            Assert.That(HS_Doors.PaysBluff(true, false, true, 0, 3), Is.True);
            Assert.That(HS_Doors.PaysBluff(false, false, true, 0, 3), Is.False, "a hider opened it");
            Assert.That(HS_Doors.PaysBluff(true, true, true, 0, 3), Is.False, "someone was inside");
            Assert.That(HS_Doors.PaysBluff(true, false, false, 0, 3), Is.False, "the closer was found");
            Assert.That(HS_Doors.PaysBluff(true, false, true, 3, 3), Is.False, "the round's bluffs are spent");
        }

        [Test]
        public void DoorStatesRideInABitMaskAndSpotNamesReadWell() {
            ulong mask = (1UL << 0) | (1UL << 5) | (1UL << 63);
            Assert.That(HS_Doors.IsClosed(mask, 0) && HS_Doors.IsClosed(mask, 5) && HS_Doors.IsClosed(mask, 63), Is.True);
            Assert.That(HS_Doors.IsClosed(mask, 1) || HS_Doors.IsClosed(mask, 64) || HS_Doors.IsClosed(mask, -1), Is.False);
            Assert.That(HS_Doors.ClosedMask(null), Is.Zero);
            Assert.That(HS_Doors.Readable("CoatCloset"), Is.EqualTo("Coat Closet"));
            Assert.That(HS_Doors.Readable("Outhouse"), Is.EqualTo("Outhouse"));
            Assert.That(HS_Doors.Readable(""), Is.Empty);
        }

        [Test]
        public void TuningIsKeptSane() {
            var tuning = new HS_DoorTuning { reach = -1f, verticalReach = 0f, doorwayClearance = -2f, useCooldown = -1f, bluffsPerRound = 0 };
            tuning.Validate();
            Assert.That(tuning.reach, Is.GreaterThan(0f));
            Assert.That(tuning.verticalReach, Is.GreaterThan(0f));
            Assert.That(tuning.doorwayClearance, Is.Zero);
            Assert.That(tuning.useCooldown, Is.Zero);
            Assert.That(tuning.bluffsPerRound, Is.EqualTo(1));
        }
    }

    public sealed class HS_SearchLogTests {
        [Test]
        public void WhereASeekerStartsIsFreeAndEachNewRoomPaysOnce() {
            var log = new HS_SearchLog();
            Assert.That(log.Visit("Foyer", 8), Is.False, "the start room");
            Assert.That(log.Visit("Kitchen", 8), Is.True);
            Assert.That(log.Visit("Kitchen", 8), Is.False, "again");
            Assert.That(log.Visit("Foyer", 8), Is.False, "back to the start");
            Assert.That(log.Visit("", 8), Is.False, "between areas");
            Assert.That(log.Visit("Library", 8), Is.True);
            Assert.That(log.Paid, Is.EqualTo(2));
        }

        [Test]
        public void TheRoundsCapHolds() {
            var log = new HS_SearchLog();
            log.Visit("Start", 2);
            Assert.That(log.Visit("A", 2), Is.True);
            Assert.That(log.Visit("B", 2), Is.True);
            Assert.That(log.Visit("C", 2), Is.False);
            Assert.That(log.Paid, Is.EqualTo(2));
        }
    }
}
