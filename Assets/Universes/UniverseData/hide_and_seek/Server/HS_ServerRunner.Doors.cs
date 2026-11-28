using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.hide_and_seek.Server {
    /// <summary>
    /// The walk-in hiding spots' doors (<see cref="HS_HideDoor"/> on <see cref="HS_Map.doors"/>). A hider can
    /// shut themselves in, which blocks sight, tags and the flashlight, or shut an empty closet as a bluff;
    /// a seeker who opens an empty closet a hider shut pays that hider. Seekers can only open doors. Every
    /// door stands open when the hiders are let loose. The rules are <see cref="HS_Doors"/>.
    /// </summary>
    public sealed partial class HS_ServerRunner {
        [Header("Closet doors")]
        [SerializeField] HS_DoorTuning doorTuning = new();

        readonly Dictionary<NetworkConnection, float> lastDoorUse = new();
        readonly Dictionary<NetworkConnection, int> bluffsPaid = new();
        /// <summary>Per door: the hider who last shut it this round (null once it opens).</summary>
        NetworkConnection[] doorClosers = System.Array.Empty<NetworkConnection>();

        int DoorCount => map != null && map.doors != null ? map.doors.Length : 0;

        ulong ClosedDoors => map != null ? HS_Doors.ClosedMask(map.doors) : 0;

        /// <summary>Opens every door and forgets who shut them (a new round, or the server starting).</summary>
        void ResetDoors() {
            if (doorClosers.Length != DoorCount) doorClosers = new NetworkConnection[DoorCount];
            System.Array.Clear(doorClosers, 0, doorClosers.Length);
            bluffsPaid.Clear();
            for (int i = 0; i < DoorCount; i++) {
                HS_HideDoor door = map.doors[i];
                if (door == null || door.IsOpen) continue;
                door.SetOpen(true, false);
                InstanceFinder.ServerManager.Broadcast(new HS_DoorBroadcast { door = i, open = true, clientId = -1 });
            }
            MarkDirty();
        }

        void OnDoorRequest(NetworkConnection connection, HS_DoorRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            HS_HideDoor door = map != null ? map.Door(request.door) : null;
            if (door == null || door.IsOpen == request.open) return;
            if (lastDoorUse.TryGetValue(connection, out float at) && Time.time - at < doorTuning.useCooldown) return;
            if (!TryGetLiveCharacter(connection, out LocalCharacter actor)) return;
            HS_Role role = RoleOf(connection);
            string refusal = HS_Doors.Refusal(phase, role, request.open);
            if (refusal == null && !HS_Doors.InReach(actor.transform.position, door.closedFrom, door.closedTo, doorTuning.reach, doorTuning.verticalReach))
                refusal = "Too far from the door.";
            if (refusal == null && !request.open && SomeoneInDoorway(door))
                refusal = "Someone is in the doorway.";
            if (refusal != null) {
                Refuse(connection, refusal);
                return;
            }
            lastDoorUse[connection] = Time.time;
            door.SetOpen(request.open, false);
            bool found = false;
            if (request.open) found = OnDoorOpened(request.door, door, connection, role);
            else doorClosers[request.door] = role == HS_Role.Hider ? connection : null;
            InstanceFinder.ServerManager.Broadcast(new HS_DoorBroadcast { door = request.door, open = request.open, clientId = connection.ClientId, found = found });
            MarkDirty();
        }

        /// <summary>
        /// A door was opened: warn whoever hid behind it, or pay its closer for a bluff that fooled a seeker.
        /// Returns whether a seeker found someone inside.
        /// </summary>
        bool OnDoorOpened(int index, HS_HideDoor door, NetworkConnection opener, HS_Role role) {
            NetworkConnection closer = doorClosers[index];
            doorClosers[index] = null;
            bool bySeeker = phase == HS_Phase.Seeking && Seeks(role);
            var inside = new List<NetworkConnection>();
            foreach (var pair in roles)
                if (pair.Value == HS_Role.Hider && pair.Key.IsActive && TryGetLiveCharacter(pair.Key, out LocalCharacter hider) && door.Contains(hider.transform.position))
                    inside.Add(pair.Key);
            if (bySeeker)
                foreach (NetworkConnection hider in inside)
                    InstanceFinder.ServerManager.Broadcast(hider, new HS_ResultBroadcast { accepted = true, message = $"{NameOf(opener)} opened your door! Run!" });
            int paid = closer != null && bluffsPaid.TryGetValue(closer, out int n) ? n : 0;
            if (closer != null && HS_Doors.PaysBluff(bySeeker, inside.Count > 0, RoleOf(closer) == HS_Role.Hider && closer.IsActive, paid, doorTuning.bluffsPerRound)) {
                bluffsPaid[closer] = paid + 1;
                AddPoints(closer, doorTuning.bluffPoints, $"Empty {door.spotName} fooled a seeker", true);
                retention.Count(closer, HS_Retention.Stats.Bluffs);
                PlayerProgress.Modify<HS_SaveData>(closer, save => save.bluffs++);
            }
            return bySeeker && inside.Count > 0;
        }

        bool SomeoneInDoorway(HS_HideDoor door) {
            foreach (var pair in LocalCharacter.Characters) {
                LocalCharacter character = pair.Value;
                if (character == null || character.IsDead) continue;
                if (HS_Doors.InDoorway(character.transform.position, door.closedFrom, door.closedTo, doorTuning.doorwayClearance, doorTuning.verticalReach)) return true;
            }
            return false;
        }

        void Refuse(NetworkConnection c, string message) =>
            InstanceFinder.ServerManager.Broadcast(c, new HS_ResultBroadcast { accepted = false, message = message });

        void ForgetDoors(NetworkConnection c) {
            lastDoorUse.Remove(c);
            bluffsPaid.Remove(c);
            for (int i = 0; i < doorClosers.Length; i++) if (doorClosers[i] == c) doorClosers[i] = null;
        }
    }
}
