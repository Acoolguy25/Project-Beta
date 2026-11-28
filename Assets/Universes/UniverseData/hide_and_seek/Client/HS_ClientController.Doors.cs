using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.UI;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// HUD controller, closet doors: the E prompt for the nearest door you may work (shut yourself in, shut
    /// an empty closet to bluff, or as a seeker search one), the request, and every client's side of a door
    /// moving: the swing, its creak or thud, and the jolt when a seeker finds someone behind it. The doors'
    /// open/shut states also ride on the round state, so a late joiner's doors match the server's.
    /// </summary>
    public sealed partial class HS_ClientController {
        [Header("Closet doors")]
        [Tooltip("The E prompt for a door, a row above the tag and taunt prompts.")]
        public CanvasGroupController doorPrompt;
        public TMP_Text doorLabel;
        [Tooltip("A door swinging open (3D, at the door).")]
        public SfxSound doorOpenSound = new();
        [Tooltip("A door banging shut (3D, at the door).")]
        public SfxSound doorCloseSound = new();
        [Tooltip("Shake for the seeker who opens a door on a hider, and the hider behind it.")]
        [Range(0f, 1f)] public float doorFoundShake = 0.3f;
        [Tooltip("The prompt offers a door a little inside the server's reach, so a press at the edge is never refused.")]
        [Range(0.5f, 1f)] public float doorReachShare = 0.92f;

        const float DoorRetrySeconds = 0.3f;

        /// <summary>What E does right now: tag the hider in reach, work a door, or nothing in particular.</summary>
        enum PromptTarget { None, Hider, Door }

        PromptTarget promptTarget;
        int promptDoor = -1, labelledDoor = -1;
        bool labelledDoorOpen;
        float doorPendingUntil;

        /// <summary>E: a hider in reach is tagged first; otherwise the door in reach is worked; a seeker's E with neither still asks to tag (the server says why not).</summary>
        void OnInteract() {
            if (promptTarget == PromptTarget.Door && promptDoor >= 0) RequestDoor(promptDoor);
            else TryTag();
        }

        void RequestDoor(int index) {
            HS_HideDoor door = map != null ? map.Door(index) : null;
            if (manager == null || door == null || Time.unscaledTime < doorPendingUntil) return;
            doorPendingUntil = Time.unscaledTime + DoorRetrySeconds;
            manager.Broadcast(new HS_DoorRequest { round = state.round, door = index, open = !door.IsOpen });
        }

        /// <summary>The nearest door you may work from where you stand, or -1.</summary>
        int DoorInReach() {
            var me = LocalPlayer.Character;
            if (me == null || map == null || map.doors == null || blindfold.activeSelf) return -1;
            Vector3 feet = me.transform.position;
            float reach = HS_Doors.DefaultReach * doorReachShare, best = float.MaxValue;
            int found = -1;
            HS_Role role = MyRole;
            for (int i = 0; i < map.doors.Length; i++) {
                HS_HideDoor door = map.doors[i];
                if (door == null || !HS_Doors.InReach(feet, door.closedFrom, door.closedTo, reach, HS_Doors.DefaultVerticalReach)) continue;
                if (HS_Doors.Refusal(state.phase, role, !door.IsOpen) != null) continue;
                float d = HS_Doors.FlatDistanceToLeaf(feet, door.closedFrom, door.closedTo);
                if (d < best) { best = d; found = i; }
            }
            return found;
        }

        /// <summary>"Shut yourself in", "Search the Coat Closet": what pressing E will do to <paramref name="index"/>.</summary>
        string DoorPrompt(int index) {
            HS_HideDoor door = map.doors[index];
            var me = LocalPlayer.Character;
            bool inside = me != null && door.Contains(me.transform.position);
            string spot = string.IsNullOrEmpty(door.spotName) ? "door" : door.spotName;
            if (!door.IsOpen) return Seeks(MyRole) && state.phase == HS_Phase.Seeking ? $"Search the <b>{spot}</b>" : inside ? "Open the door" : $"Open the <b>{spot}</b>";
            if (inside) return "Shut yourself in";
            return MyRole == HS_Role.Hider ? $"Shut the <b>{spot}</b> <size=75%>(bluff)</size>" : $"Shut the <b>{spot}</b>";
        }

        /// <summary>Shows the door prompt for <paramref name="index"/>, rewriting the label only when the door or its state changed.</summary>
        void ShowDoorPrompt(int index) {
            bool open = map.doors[index].IsOpen;
            if (index != labelledDoor || open != labelledDoorOpen) {
                labelledDoor = index;
                labelledDoorOpen = open;
                doorLabel.text = DoorPrompt(index);
            }
            Show(doorPrompt, true);
        }

        // ---------------------------------------------------------------- server messages
        void OnDoor(HS_DoorBroadcast message, Channel channel) {
            HS_HideDoor door = map != null ? map.Door(message.door) : null;
            if (door == null) return;
            if (message.clientId == MyId) doorPendingUntil = 0f;
            if (door.IsOpen == message.open) return;
            door.SetOpen(message.open, true);
            GameSfx.PlayAt(message.open ? doorOpenSound : doorCloseSound, door.Centre + Vector3.up * 2f);
            // A forced label refresh: the prompt now offers the opposite.
            labelledDoor = -1;
            if (!message.found) return;
            var me = LocalPlayer.Character;
            bool opener = message.clientId == MyId, behind = me != null && door.Contains(me.transform.position);
            if (opener || behind) CameraShake.Kick(doorFoundShake, 0.35f);
            if (opener) Toast($"Someone is hiding in the {door.spotName}! Tag them!", tagIcon, null, ToastShort);
        }

        /// <summary>Matches every door to the round state's mask (a late joiner, or a door message that was missed).</summary>
        void SyncDoors(ulong closed) {
            if (map == null || map.doors == null) return;
            for (int i = 0; i < map.doors.Length; i++) {
                HS_HideDoor door = map.doors[i];
                bool open = !HS_Doors.IsClosed(closed, i);
                if (door != null && door.IsOpen != open) door.SetOpen(open, true);
            }
        }
    }
}
