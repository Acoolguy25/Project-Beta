using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>The night's rules: items never vanish, each act needs exactly its own items, and the endings.</summary>
    public sealed class CH_NightTests {
        const int Ana = 1, Ben = 2;

        static CH_Night.Layout Layout() {
            Pose At(float x) => new(new Vector3(x, 0f, 0f), Quaternion.identity);
            return new CH_Night.Layout {
                boatPartSpots = new[] { At(10), At(20), At(30) },
                relicSpots = new[] { At(40), At(50), At(60) },
                musicBoxSpot = At(70),
                bellSpot = At(80),
                boatSlots = new[] { At(1), At(2), At(3), At(4) },
                graves = Enumerable.Range(0, CH_Story.SingerCount).Select(i => At(100 + i)).ToArray(),
                lilyMarker = At(5)
            };
        }

        static CH_Night Started(int seed = 7) {
            var night = new CH_Night(seed, Layout());
            night.Enter(CH_Phase.Stranded);
            return night;
        }

        static CH_Night.Item Item(CH_Night night, CH_ItemKind kind) => night.Items.First(item => item.Kind == kind);
        static CH_Night.Slot Slot(CH_Night night, CH_SlotKind kind, int singer = -1) =>
            night.Slots.First(slot => slot.Kind == kind && (singer < 0 || slot.Singer == singer));

        static void Deliver(CH_Night night, int carrier, CH_Night.Item item, CH_Night.Slot slot) {
            Assert.IsTrue(night.TryPickUp(carrier, item.Id, out _, out _), $"pick up {item.Kind}");
            Assert.IsTrue(night.TryPlace(carrier, slot.Id, out _), $"place {item.Kind} in {slot.Kind}");
        }

        static void FixBoat(CH_Night night) {
            Deliver(night, Ana, Item(night, CH_ItemKind.Fuel), Slot(night, CH_SlotKind.BoatFuel));
            Deliver(night, Ana, Item(night, CH_ItemKind.Toolbox), Slot(night, CH_SlotKind.BoatToolbox));
            Deliver(night, Ben, Item(night, CH_ItemKind.Lantern), Slot(night, CH_SlotKind.BoatLantern));
        }

        static void FreeSingers(CH_Night night) {
            foreach (CH_ItemKind relic in CH_Story.Relics) {
                CH_Night.Item item = Item(night, relic);
                Deliver(night, Ana, item, Slot(night, CH_SlotKind.Grave, item.Singer));
            }
        }

        [Test] public void SameSeedChoosesTheSameThreeDifferentSingers() {
            var a = new CH_Night(51793, Layout());
            var b = new CH_Night(51793, Layout());
            CollectionAssert.AreEqual(a.Singers, b.Singers);
            Assert.AreEqual(CH_Story.SingersPerNight, a.Singers.Distinct().Count());
            Assert.IsTrue(a.Singers.All(singer => singer >= 0 && singer < CH_Story.SingerCount));
            var seen = Enumerable.Range(1, 200).SelectMany(seed => new CH_Night(seed * 7919, Layout()).Singers).Distinct().Count();
            Assert.AreEqual(CH_Story.SingerCount, seen, "Every singer turns up across nights.");
        }

        [Test] public void OnlyTheBoatPartsAreOutInActOne() {
            CH_Night night = Started();
            foreach (CH_Night.Item item in night.Items) {
                bool part = CH_Story.BoatParts.Contains(item.Kind);
                Assert.AreEqual(part ? CH_ItemState.Resting : CH_ItemState.Hidden, item.State, item.Kind.ToString());
            }
            Assert.IsFalse(night.TryPickUp(Ana, Item(night, CH_ItemKind.Bell).Id, out _, out _));
        }

        [Test] public void NothingCanBeTakenDuringACutscene() {
            var night = new CH_Night(3, Layout());
            night.Enter(CH_Phase.Prologue);
            Assert.IsFalse(night.TryPickUp(Ana, Item(night, CH_ItemKind.Fuel).Id, out _, out _));
        }

        [Test] public void PickingUpWithFullHandsSwapsAndNothingVanishes() {
            CH_Night night = Started();
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel), toolbox = Item(night, CH_ItemKind.Toolbox);
            Vector3 toolboxSpot = toolbox.Position;
            Assert.IsTrue(night.TryPickUp(Ana, fuel.Id, out var none, out bool first));
            Assert.IsNull(none);
            Assert.IsTrue(first);
            Assert.IsTrue(night.TryPickUp(Ana, toolbox.Id, out var swapped, out _));
            Assert.AreSame(fuel, swapped);
            Assert.AreEqual(CH_ItemState.Resting, fuel.State, "The fuel is set down, not lost.");
            Assert.AreEqual(toolboxSpot, fuel.Position, "It is set down where the toolbox lay.");
            Assert.AreSame(toolbox, night.CarriedBy(Ana));
            Assert.IsTrue(night.TryPickUp(Ben, fuel.Id, out _, out bool again));
            Assert.IsFalse(again, "Its first pick-up line plays once a night.");
        }

        [Test] public void AnItemInSomeoneElsesHandsCannotBeTaken() {
            CH_Night night = Started();
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel);
            Assert.IsTrue(night.TryPickUp(Ana, fuel.Id, out _, out _));
            Assert.IsFalse(night.TryPickUp(Ben, fuel.Id, out _, out _));
        }

        [Test] public void ADeathDropsTheItemWhereTheyFell() {
            CH_Night night = Started();
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel);
            night.TryPickUp(Ana, fuel.Id, out _, out _);
            var fell = new Vector3(3f, 1f, 4f);
            Assert.AreSame(fuel, night.Drop(Ana, fell, 90f));
            Assert.AreEqual(CH_ItemState.Resting, fuel.State);
            Assert.AreEqual(fell, fuel.Position);
            Assert.IsNull(night.Drop(Ana, fell, 0f), "Empty hands drop nothing.");
        }

        [Test] public void AnItemPutDownCanBePickedUpByATeammate() {
            CH_Night night = Started();
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel);
            night.TryPickUp(Ana, fuel.Id, out _, out _);
            var feet = new Vector3(12f, 2f, 5f);
            Assert.AreSame(fuel, night.Drop(Ana, feet, 0f), "R puts down what you hold.");
            Assert.IsNull(night.CarriedBy(Ana));
            Assert.IsTrue(night.TryPickUp(Ben, fuel.Id, out _, out bool firstTouch), "Passed on: a teammate takes it from the ground.");
            Assert.IsFalse(firstTouch, "Its first pick-up line does not play twice.");
            Assert.AreEqual(Ben, fuel.Carrier);
        }

        [Test] public void ItemsRememberWhereTheyWereFound() {
            CH_Night night = Started();
            foreach (CH_Night.Item item in night.Items) Assert.AreEqual(item.Position, item.Origin, $"{item.Kind} starts where it was found.");
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel);
            Vector3 found = fuel.Origin;
            night.TryPickUp(Ana, fuel.Id, out _, out _);
            night.Drop(Ana, new Vector3(1f, 1f, 1f), 0f);
            Assert.AreEqual(found, fuel.Origin, "Moving it does not move where it was found.");
        }

        [Test] public void TheLakeIsNeverAShortcut() {
            Vector3 feet = new(5f, 20f, 5f), shore = new(8f, 17f, 2f), found = new(40f, 18f, 40f);
            Assert.AreEqual(feet, CH_Night.DropPosition(feet, true, shore, found), "On dry ground it lies where it was let go.");
            Assert.AreEqual(shore, CH_Night.DropPosition(feet, false, shore, found), "In the water it washes up where they last stood on dry ground.");
            Assert.AreEqual(found, CH_Night.DropPosition(feet, false, null, found), "Without any dry ground, it goes back where it was found.");
        }

        [Test] public void DrowningIsOnlyFreeWithEmptyHandsAndNobodyChasing() {
            Assert.IsFalse(CH_Night.DrowningCostsLife(false, false), "A swim with empty hands, unchased, is forgiven.");
            Assert.IsTrue(CH_Night.DrowningCostsLife(true, false), "Carrying something into the lake costs a life.");
            Assert.IsTrue(CH_Night.DrowningCostsLife(false, true), "Jumping in to escape him costs a life.");
            Assert.IsTrue(CH_Night.DrowningCostsLife(true, true));
        }

        [Test] public void PartsOnlyFitTheirOwnPlaceAndStayThere() {
            CH_Night night = Started();
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel);
            night.TryPickUp(Ana, fuel.Id, out _, out _);
            Assert.IsFalse(night.TryPlace(Ana, Slot(night, CH_SlotKind.BoatToolbox).Id, out _));
            Assert.IsTrue(night.TryPlace(Ana, Slot(night, CH_SlotKind.BoatFuel).Id, out var placed));
            Assert.AreSame(fuel, placed);
            Assert.AreEqual(CH_ItemState.Placed, fuel.State);
            Assert.AreEqual(Slot(night, CH_SlotKind.BoatFuel).Position, fuel.Position);
            Assert.IsFalse(night.TryPickUp(Ben, fuel.Id, out _, out _), "A fitted part stays fitted.");
        }

        [Test] public void FixingTheBoatEarnsTheTakingAndRevealsTheRelicsAndMusicBox() {
            CH_Night night = Started();
            Deliver(night, Ana, Item(night, CH_ItemKind.Fuel), Slot(night, CH_SlotKind.BoatFuel));
            Assert.AreEqual(CH_Phase.Stranded, night.EarnedPhase());
            FixBoat2(night);
            Assert.AreEqual(CH_Phase.Taking, night.EarnedPhase());
            night.Enter(CH_Phase.Taking);
            night.Enter(CH_Phase.Choir);
            foreach (CH_ItemKind kind in CH_Story.Relics.Append(CH_ItemKind.MusicBox))
                Assert.AreEqual(CH_ItemState.Resting, Item(night, kind).State, kind.ToString());
            Assert.AreEqual(CH_ItemState.Hidden, Item(night, CH_ItemKind.Bell).State);

            static void FixBoat2(CH_Night n) {
                Deliver(n, Ana, Item(n, CH_ItemKind.Toolbox), Slot(n, CH_SlotKind.BoatToolbox));
                Deliver(n, Ben, Item(n, CH_ItemKind.Lantern), Slot(n, CH_SlotKind.BoatLantern));
            }
        }

        [Test] public void ARelicOnlyRestsOnItsOwnSingersGrave() {
            CH_Night night = Started();
            FixBoat(night);
            night.Enter(CH_Phase.Choir);
            CH_Night.Item relic = Item(night, CH_ItemKind.Hymnal);
            int wrong = Enumerable.Range(0, CH_Story.SingerCount).First(singer => singer != relic.Singer);
            night.TryPickUp(Ana, relic.Id, out _, out _);
            Assert.IsFalse(night.TryPlace(Ana, Slot(night, CH_SlotKind.Grave, wrong).Id, out _));
            Assert.AreSame(Slot(night, CH_SlotKind.Grave, relic.Singer), night.DestinationFor(relic));
            Assert.IsTrue(night.TryPlace(Ana, Slot(night, CH_SlotKind.Grave, relic.Singer).Id, out _));
        }

        [Test] public void GravesOfSingersNotOutTonightStayClosed() {
            CH_Night night = Started();
            FixBoat(night);
            night.Enter(CH_Phase.Choir);
            foreach (CH_Night.Slot grave in night.Slots.Where(slot => slot.Kind == CH_SlotKind.Grave))
                Assert.AreEqual(night.Singers.Contains(grave.Singer), night.IsOpen(grave), CH_Story.SingerName(grave.Singer));
        }

        [Test] public void TheMusicBoxIsASecretWithoutAnArrow() {
            CH_Night night = Started();
            FixBoat(night);
            night.Enter(CH_Phase.Choir);
            CH_Night.Item box = Item(night, CH_ItemKind.MusicBox);
            Assert.IsFalse(night.IsObjective(box));
            Assert.IsTrue(night.IsObjective(Item(night, CH_ItemKind.Hymnal)));
        }

        [Test] public void FullNightWithTheMusicBoxEarnsTheTrueEnding() {
            CH_Night night = Started();
            FixBoat(night);
            night.Enter(CH_Phase.Taking);
            night.Enter(CH_Phase.Choir);
            Deliver(night, Ben, Item(night, CH_ItemKind.MusicBox), Slot(night, CH_SlotKind.LilyMarker));
            FreeSingers(night);
            Assert.AreEqual(CH_Phase.ChapelOpens, night.EarnedPhase());
            night.Enter(CH_Phase.ChapelOpens);
            Assert.AreEqual(CH_ItemState.Resting, Item(night, CH_ItemKind.Bell).State, "The bell is on the altar when the doors open.");
            night.Enter(CH_Phase.Sermon);
            Deliver(night, Ana, Item(night, CH_ItemKind.Bell), Slot(night, CH_SlotKind.BoatBell));
            Assert.AreEqual(CH_Phase.Boarding, night.EarnedPhase());
            night.Enter(CH_Phase.Boarding);
            Assert.AreEqual(CH_Story.EndingNone, night.Ending);
            night.Complete();
            Assert.AreEqual(CH_Story.EndingTrue, night.Ending);
            Assert.IsTrue(night.IsOver);
        }

        [Test] public void EscapingWithoutTheMusicBoxAndDrowningHaveTheirOwnEndings() {
            CH_Night escaped = Started();
            escaped.Complete();
            Assert.AreEqual(CH_Story.EndingEscaped, escaped.Ending);
            CH_Night drowned = Started();
            drowned.Fail();
            Assert.AreEqual(CH_Story.EndingDrowned, drowned.Ending);
            drowned.Complete();
            Assert.AreEqual(CH_Story.EndingDrowned, drowned.Ending, "A finished night keeps its ending.");
        }

        [Test] public void NothingIsAcceptedOnceTheNightIsOver() {
            CH_Night night = Started();
            CH_Night.Item fuel = Item(night, CH_ItemKind.Fuel);
            night.TryPickUp(Ana, fuel.Id, out _, out _);
            night.Fail();
            Assert.IsFalse(night.TryPlace(Ana, Slot(night, CH_SlotKind.BoatFuel).Id, out _));
        }

        [Test] public void BoatSlotsAreReachedAboveTheGunwale() {
            // From the jetty a point on the boat's boards is hidden by the hull's side; graves are reached at hand height.
            Vector3 boards = new(10f, 16.36f, 5f);
            foreach (CH_SlotKind kind in new[] { CH_SlotKind.BoatFuel, CH_SlotKind.BoatToolbox, CH_SlotKind.BoatLantern, CH_SlotKind.BoatBell }) {
                Assert.IsTrue(CH_Night.IsAboard(kind), kind.ToString());
                Assert.AreEqual(boards.y + CH_Night.ReachLiftAboard, CH_Night.SlotReachPoint(kind, boards).y, 1e-4f, kind.ToString());
            }
            Assert.IsFalse(CH_Night.IsAboard(CH_SlotKind.Grave));
            Assert.IsFalse(CH_Night.IsAboard(CH_SlotKind.LilyMarker));
            Assert.AreEqual(boards.y + CH_Night.ReachLiftAshore, CH_Night.SlotReachPoint(CH_SlotKind.Grave, boards).y, 1e-4f);
            Assert.Greater(CH_Night.ReachLiftAboard, CH_Night.ReachLiftAshore);
        }
    }
}
