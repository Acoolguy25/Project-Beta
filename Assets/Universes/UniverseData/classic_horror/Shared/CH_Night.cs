using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// One night at Hollowmere as plain state, free of networking and physics so it can be tested: which items exist,
    /// where they are, who carries what, which slots are filled, and when an act is complete. The server runner owns
    /// one and turns its answers into broadcasts, voice lines and cutscenes.
    /// </summary>
    public sealed class CH_Night {
        public sealed class Item {
            public int Id;
            public CH_ItemKind Kind;
            public CH_ItemState State;
            public Vector3 Position;
            public float Yaw;
            /// <summary>Where it was found: its last resort when it is dropped somewhere it cannot lie.</summary>
            public Vector3 Origin;
            /// <summary>Who carries it (the runner's investigator key), or -1.</summary>
            public int Carrier = -1;
            /// <summary>The slot it was placed in, or -1.</summary>
            public int Slot = -1;
            /// <summary>The drowned singer a relic belonged to (<see cref="CH_Story.Singers"/>), or -1.</summary>
            public int Singer = -1;
            /// <summary>Has anyone picked it up yet (its first pick-up line plays once).</summary>
            public bool Touched;
        }

        public sealed class Slot {
            public int Id;
            public CH_SlotKind Kind;
            public CH_ItemKind Accepts;
            public Vector3 Position;
            public float Yaw;
            /// <summary>The item placed here, or -1.</summary>
            public int Item = -1;
            /// <summary>For a grave: whose it is (<see cref="CH_Story.Singers"/>).</summary>
            public int Singer = -1;
            public bool Filled => Item >= 0;
        }

        /// <summary>
        /// A place where an investigator stops to work a minigame (<see cref="CH_PuzzleRun"/>): the ferry's engine, the
        /// bell's locks, and a hymn at each singer's grave. <see cref="Progress"/> is kept here, so stepping away or being
        /// chased off loses nothing.
        /// </summary>
        public sealed class Station {
            public int Id;
            public CH_PuzzleKind Kind;
            public Vector3 Position;
            public float Yaw;
            /// <summary>For a hymn: whose it is (<see cref="CH_Story.Singers"/>); otherwise -1.</summary>
            public int Singer = -1;
            public bool Done;
            public int Progress;
        }

        /// <summary>Where the night's items and slots are, chosen by the runner from the map's anchors.</summary>
        public struct Layout {
            /// <summary>Resting places for the three boat parts, in <see cref="CH_Story.BoatParts"/> order.</summary>
            public Pose[] boatPartSpots;
            /// <summary>Resting places for the three relics.</summary>
            public Pose[] relicSpots;
            /// <summary>Where Lily's music box waits; it appears in Act II.</summary>
            public Pose musicBoxSpot;
            /// <summary>The bell on the chapel altar.</summary>
            public Pose bellSpot;
            /// <summary>The boat's fuel, toolbox, lantern and bell places.</summary>
            public Pose[] boatSlots;
            /// <summary>One grave per singer, in <see cref="CH_Story.Singers"/> order.</summary>
            public Pose[] graves;
            /// <summary>Lily's marker on the jetty steps.</summary>
            public Pose lilyMarker;
            /// <summary>The ferry's engine, which has to be turned over once its parts are fitted.</summary>
            public Pose engine;
            /// <summary>The locks that chain the bell to the altar.</summary>
            public Pose bellLock;
        }

        public readonly int Seed;
        /// <summary>The three singers freed tonight (indices into <see cref="CH_Story.Singers"/>).</summary>
        public readonly int[] Singers = new int[CH_Story.SingersPerNight];
        public readonly List<Item> Items = new();
        public readonly List<Slot> Slots = new();
        public readonly List<Station> Stations = new();
        public CH_Phase Phase { get; private set; } = CH_Phase.Waiting;
        /// <summary>Boat parts fitted so far.</summary>
        public int PartsFixed => CountFilled(CH_SlotKind.BoatFuel, CH_SlotKind.BoatToolbox, CH_SlotKind.BoatLantern);
        /// <summary>Singers whose relic is laid on their grave (the hymn is still to be sung).</summary>
        public int RelicsLaid => CountFilled(CH_SlotKind.Grave);
        /// <summary>Singers set free: relic laid and hymn sung.</summary>
        public int SingersFreed => CountDone(CH_PuzzleKind.Hymn);
        public bool EngineStarted => CountDone(CH_PuzzleKind.Engine) > 0;
        public bool BellUnchained => CountDone(CH_PuzzleKind.BellLock) > 0;
        public bool MusicBoxReturned => CountFilled(CH_SlotKind.LilyMarker) > 0;
        public bool BellAboard => CountFilled(CH_SlotKind.BoatBell) > 0;
        public bool IsOver => Phase is CH_Phase.Complete or CH_Phase.Failed;

        public CH_Night(int seed, Layout layout) {
            Seed = seed;
            var random = new System.Random(seed);
            // Three of the five singers, in a seeded order.
            var pool = new List<int> { 0, 1, 2, 3, 4 };
            for (int i = 0; i < Singers.Length; i++) {
                int pick = random.Next(pool.Count);
                Singers[i] = pool[pick];
                pool.RemoveAt(pick);
            }
            for (int i = 0; i < CH_Story.BoatParts.Length; i++)
                AddItem(CH_Story.BoatParts[i], layout.boatPartSpots[i], CH_ItemState.Resting);
            for (int i = 0; i < CH_Story.Relics.Length; i++)
                AddItem(CH_Story.Relics[i], layout.relicSpots[i], CH_ItemState.Hidden).Singer = Singers[i];
            AddItem(CH_ItemKind.MusicBox, layout.musicBoxSpot, CH_ItemState.Hidden);
            AddItem(CH_ItemKind.Bell, layout.bellSpot, CH_ItemState.Hidden);

            for (int i = 0; i < CH_Story.BoatParts.Length; i++)
                AddSlot((CH_SlotKind)i, CH_Story.BoatParts[i], layout.boatSlots[i]);
            // A grave takes whichever relic belonged to its singer (see Fits), so its Accepts is only nominal.
            for (int singer = 0; singer < layout.graves.Length && singer < CH_Story.SingerCount; singer++)
                AddSlot(CH_SlotKind.Grave, CH_ItemKind.Hymnal, layout.graves[singer]).Singer = singer;
            AddSlot(CH_SlotKind.LilyMarker, CH_ItemKind.MusicBox, layout.lilyMarker);
            AddSlot(CH_SlotKind.BoatBell, CH_ItemKind.Bell, layout.boatSlots[3]);

            AddStation(CH_PuzzleKind.Engine, layout.engine);
            AddStation(CH_PuzzleKind.BellLock, layout.bellLock);
            for (int singer = 0; singer < layout.graves.Length && singer < CH_Story.SingerCount; singer++)
                AddStation(CH_PuzzleKind.Hymn, layout.graves[singer]).Singer = singer;
        }

        Station AddStation(CH_PuzzleKind kind, Pose pose) {
            var station = new Station { Id = Stations.Count, Kind = kind, Position = pose.position, Yaw = YawOf(pose.rotation) };
            Stations.Add(station);
            return station;
        }

        Item AddItem(CH_ItemKind kind, Pose pose, CH_ItemState state) {
            var item = new Item { Id = Items.Count, Kind = kind, State = state, Position = pose.position, Origin = pose.position, Yaw = YawOf(pose.rotation) };
            Items.Add(item);
            return item;
        }

        Slot AddSlot(CH_SlotKind kind, CH_ItemKind accepts, Pose pose) {
            var slot = new Slot { Id = Slots.Count, Kind = kind, Accepts = accepts, Position = pose.position, Yaw = YawOf(pose.rotation) };
            Slots.Add(slot);
            return slot;
        }

        /// <summary>Heading in degrees [0, 360), from managed maths only so the night's rules run without the engine.</summary>
        static float YawOf(Quaternion rotation) {
            Vector3 forward = rotation * Vector3.forward;
            return Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
        }

        int CountFilled(params CH_SlotKind[] kinds) {
            int count = 0;
            foreach (Slot slot in Slots)
                if (slot.Filled && System.Array.IndexOf(kinds, slot.Kind) >= 0) count++;
            return count;
        }

        int CountDone(CH_PuzzleKind kind) {
            int count = 0;
            foreach (Station station in Stations) if (station.Done && station.Kind == kind) count++;
            return count;
        }

        public Station GetStation(int id) => id >= 0 && id < Stations.Count ? Stations[id] : null;

        /// <summary>
        /// Whether a minigame can be worked now: the engine once its three parts are fitted, a hymn once the singer's relic
        /// lies on their grave (and only for tonight's singers), the bell's locks once Act III has begun.
        /// </summary>
        public bool IsAvailable(Station station) {
            if (station == null || station.Done || IsOver) return false;
            switch (station.Kind) {
                case CH_PuzzleKind.Engine:
                    return Phase == CH_Phase.Stranded && PartsFixed == CH_Story.BoatParts.Length;
                case CH_PuzzleKind.Hymn:
                    if (Phase != CH_Phase.Choir || !IsSingerTonight(station.Singer)) return false;
                    foreach (Slot slot in Slots) if (slot.Kind == CH_SlotKind.Grave && slot.Singer == station.Singer) return slot.Filled;
                    return false;
                default:
                    return Phase == CH_Phase.Sermon;
            }
        }

        /// <summary>The point an investigator reaches for (and the client focuses) at a station.</summary>
        public static Vector3 StationReachPoint(CH_PuzzleKind kind, Vector3 position) =>
            position + Vector3.up * (kind == CH_PuzzleKind.Engine ? ReachLiftAboard : kind == CH_PuzzleKind.Hymn ? ReachLiftAshore : BellLockLift);

        /// <summary>The bell's locks sit on the altar a little above the table's top.</summary>
        public const float BellLockLift = 1.2f;

        /// <summary>Whether <paramref name="item"/> may be picked up now: the bell stays chained to the altar until its locks are open.</summary>
        public bool CanTake(Item item) => item != null && (item.Kind != CH_ItemKind.Bell || BellUnchained);

        /// <summary>For a hymn, how many singers were set free before this one: each hymn is one note longer than the last.</summary>
        public int HymnOrder(Station station) => station.Kind == CH_PuzzleKind.Hymn ? SingersFreed : 0;

        /// <summary>Moves the night to <paramref name="next"/> and reveals that act's items.</summary>
        public void Enter(CH_Phase next) {
            Phase = next;
            foreach (Item item in Items) {
                if (item.State != CH_ItemState.Hidden) continue;
                bool reveal = next switch {
                    CH_Phase.Choir => item.Kind is CH_ItemKind.Hymnal or CH_ItemKind.Photograph or CH_ItemKind.Candle or CH_ItemKind.MusicBox,
                    CH_Phase.ChapelOpens or CH_Phase.Sermon => item.Kind == CH_ItemKind.Bell,
                    _ => false
                };
                if (reveal) item.State = CH_ItemState.Resting;
            }
        }

        public void Fail() { if (!IsOver) Phase = CH_Phase.Failed; }
        public void Complete() { if (!IsOver) Phase = CH_Phase.Complete; }

        public Item CarriedBy(int carrier) {
            foreach (Item item in Items) if (item.State == CH_ItemState.Carried && item.Carrier == carrier) return item;
            return null;
        }

        public Item GetItem(int id) => id >= 0 && id < Items.Count ? Items[id] : null;
        public Slot GetSlot(int id) => id >= 0 && id < Slots.Count ? Slots[id] : null;

        /// <summary>
        /// Picks a resting item up. Hands already full swap: the held item is set down where the new one lay.
        /// Returns the item that was set down, if any, through <paramref name="swapped"/>.
        /// </summary>
        public bool TryPickUp(int carrier, int itemId, out Item swapped, out bool firstTouch) {
            swapped = null;
            firstTouch = false;
            Item item = GetItem(itemId);
            if (item == null || item.State != CH_ItemState.Resting || !CH_Story.IsPlayable(Phase) || !CanTake(item)) return false;
            Item held = CarriedBy(carrier);
            if (held != null) {
                held.State = CH_ItemState.Resting;
                held.Carrier = -1;
                held.Position = item.Position;
                held.Yaw = item.Yaw;
                swapped = held;
            }
            item.State = CH_ItemState.Carried;
            item.Carrier = carrier;
            firstTouch = !item.Touched;
            item.Touched = true;
            return true;
        }

        /// <summary>Sets whatever <paramref name="carrier"/> holds down at <paramref name="position"/> (a death, a hiding place).</summary>
        public Item Drop(int carrier, Vector3 position, float yaw) {
            Item held = CarriedBy(carrier);
            if (held == null) return null;
            held.State = CH_ItemState.Resting;
            held.Carrier = -1;
            held.Position = position;
            held.Yaw = yaw;
            return held;
        }

        /// <summary>Slots aboard the ferry (its parts and the bell). Their markers and items sit on the boat's boards.</summary>
        public static bool IsAboard(CH_SlotKind kind) =>
            kind is CH_SlotKind.BoatFuel or CH_SlotKind.BoatToolbox or CH_SlotKind.BoatLantern or CH_SlotKind.BoatBell;

        /// <summary>Height above a slot that investigators reach for. Aboard it clears the gunwale, which would otherwise hide the boards from the jetty.</summary>
        public const float ReachLiftAshore = 0.9f, ReachLiftAboard = 1.6f;

        /// <summary>The point the client focuses and the server checks reach and line of sight to, for a slot.</summary>
        public static Vector3 SlotReachPoint(CH_SlotKind kind, Vector3 position) =>
            position + Vector3.up * (IsAboard(kind) ? ReachLiftAboard : ReachLiftAshore);

        /// <summary>
        /// Where a dropped item comes to rest. It lies where it was let go if that is dry ground; dropped in the lake (a
        /// drowning, a swim) it washes up where its carrier last stood on dry ground, or, if they never did, back where it
        /// was found. Never anywhere nearer the boat than the carrier got on foot, so the water is never a shortcut.
        /// </summary>
        public static Vector3 DropPosition(Vector3 at, bool atIsDry, Vector3? lastDry, Vector3 origin) =>
            atIsDry ? at : lastDry ?? origin;

        /// <summary>The lake only spares an investigator with empty hands whom he is not chasing.</summary>
        public static bool DrowningCostsLife(bool carrying, bool chased) => carrying || chased;

        /// <summary>
        /// Whether the ferry leaves during boarding: as soon as everyone still in the night is aboard (and someone is), or
        /// when the countdown runs out, taking only those aboard; until then it waits for teammates still ashore.
        /// </summary>
        public static bool FerryLeaves(int aboard, int ashore, bool countdownOver) => countdownOver || (aboard > 0 && ashore == 0);

        /// <summary>Whether the slot is accepting its item in the current act.</summary>
        public bool IsOpen(Slot slot) {
            if (slot == null || slot.Filled || IsOver) return false;
            return slot.Kind switch {
                CH_SlotKind.BoatFuel or CH_SlotKind.BoatToolbox or CH_SlotKind.BoatLantern => Phase == CH_Phase.Stranded,
                CH_SlotKind.Grave => Phase == CH_Phase.Choir && IsSingerTonight(slot.Singer),
                CH_SlotKind.LilyMarker => Phase is CH_Phase.Choir or CH_Phase.Sermon or CH_Phase.Boarding,
                CH_SlotKind.BoatBell => Phase == CH_Phase.Sermon,
                _ => false
            };
        }

        public bool IsSingerTonight(int singer) => System.Array.IndexOf(Singers, singer) >= 0;

        /// <summary>Whether <paramref name="item"/> belongs in <paramref name="slot"/>: a relic goes on its own singer's grave.</summary>
        public static bool Fits(Item item, Slot slot) {
            if (item == null || slot == null) return false;
            if (slot.Kind == CH_SlotKind.Grave) return item.Singer >= 0 && item.Singer == slot.Singer;
            return item.Kind == slot.Accepts;
        }

        /// <summary>Places what <paramref name="carrier"/> holds in the slot. The item stays there for the rest of the night.</summary>
        public bool TryPlace(int carrier, int slotId, out Item placed) {
            placed = null;
            Slot slot = GetSlot(slotId);
            Item held = CarriedBy(carrier);
            if (held == null || !IsOpen(slot) || !Fits(held, slot)) return false;
            held.State = CH_ItemState.Placed;
            held.Carrier = -1;
            held.Slot = slot.Id;
            held.Position = slot.Position;
            held.Yaw = slot.Yaw;
            slot.Item = held.Id;
            placed = held;
            return true;
        }

        /// <summary>The act that the current state has earned, or the current phase if it is not finished yet.</summary>
        public CH_Phase EarnedPhase() => Phase switch {
            CH_Phase.Stranded when PartsFixed == CH_Story.BoatParts.Length && EngineStarted => CH_Phase.Taking,
            CH_Phase.Choir when SingersFreed == CH_Story.SingersPerNight => CH_Phase.ChapelOpens,
            CH_Phase.Sermon when BellAboard => CH_Phase.Boarding,
            _ => Phase
        };

        /// <summary>The slot an item should go to now (for the HUD arrow), or null.</summary>
        public Slot DestinationFor(Item item) {
            if (item == null) return null;
            foreach (Slot slot in Slots) if (IsOpen(slot) && Fits(item, slot)) return slot;
            return null;
        }

        /// <summary>Items the team still has to bring somewhere in this act (resting or carried, not yet placed).</summary>
        public bool IsObjective(Item item) {
            if (item.State is CH_ItemState.Hidden or CH_ItemState.Placed) return false;
            if (item.Kind == CH_ItemKind.MusicBox) return false; // a secret; it never gets an arrow
            if (item.Kind == CH_ItemKind.Bell && !BellUnchained) return false; // the locks come first
            return DestinationFor(item) != null;
        }

        /// <summary>The ending this night has earned so far.</summary>
        public byte Ending => Phase == CH_Phase.Failed ? CH_Story.EndingDrowned
            : Phase == CH_Phase.Complete ? (MusicBoxReturned ? CH_Story.EndingTrue : CH_Story.EndingEscaped)
            : CH_Story.EndingNone;
    }
}
