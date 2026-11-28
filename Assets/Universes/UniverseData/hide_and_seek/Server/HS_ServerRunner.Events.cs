using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Server {
    /// <summary>
    /// Mid-round map events, so no two rounds play the same: a Blackout (the house goes dark on every
    /// client), a seekers' Frenzy (they run faster) or a Shuffle (hiders must reach another room in time
    /// or the seekers get a fix on them). At most one per round, never the same kind twice in a row,
    /// except today's featured event (<see cref="HS_Daily"/>), which comes most rounds and pays double for its play.
    /// </summary>
    public sealed partial class HS_ServerRunner {
        [Header("Map events")]
        [SerializeField] HS_EventTuning events = new();

        HS_RoundEvent plannedEvent, activeEvent, lastEvent;
        float plannedAfter, eventEndsAt;
        /// <summary>Shuffle: the room each hider was in when it started, and the hiders who have moved since.</summary>
        readonly Dictionary<NetworkConnection, string> shuffleRooms = new();
        readonly HashSet<NetworkConnection> shuffled = new();

        int EventSecondsLeft => activeEvent == HS_RoundEvent.None ? 0 : Mathf.Max(0, Mathf.CeilToInt(eventEndsAt - Time.time));

        /// <summary>Picks this round's event (or none) and when, in seconds after the seekers are released.</summary>
        void PlanEvent(float seekLength) {
            activeEvent = HS_RoundEvent.None;
            plannedEvent = HS_RoundEvents.Pick(random, events, lastEvent, FeaturedToday);
            plannedAfter = plannedEvent == HS_RoundEvent.None ? 0f : HS_RoundEvents.StartAfter(random, events, plannedEvent, seekLength);
        }

        void TickEvent(float seekElapsed) {
            if (activeEvent == HS_RoundEvent.None) {
                if (plannedEvent != HS_RoundEvent.None && seekElapsed >= plannedAfter) StartEvent(plannedEvent);
                return;
            }
            if (activeEvent == HS_RoundEvent.Shuffle) CheckShuffle();
            if (Time.time >= eventEndsAt) EndEvent(false);
        }

        void StartEvent(HS_RoundEvent kind) {
            plannedEvent = HS_RoundEvent.None;
            activeEvent = lastEvent = kind;
            int seconds = events.SecondsOf(kind);
            eventEndsAt = Time.time + seconds;
            if (kind == HS_RoundEvent.Shuffle) {
                shuffleRooms.Clear();
                shuffled.Clear();
                foreach (var pair in roles)
                    if (pair.Value == HS_Role.Hider && TryGetLiveCharacter(pair.Key, out LocalCharacter hider))
                        shuffleRooms[pair.Key] = map.AreaAt(hider.transform.position);
            }
            if (kind == HS_RoundEvent.Frenzy) ApplyAll();
            InstanceFinder.ServerManager.Broadcast(new HS_EventBroadcast { round = round, kind = kind, started = true, seconds = seconds });
            ServerChat.SendSystemMessage(new(Announcement(kind), SystemMessageSource.CustomMessage));
            MarkDirty();
        }

        static string Announcement(HS_RoundEvent kind) => kind switch {
            HS_RoundEvent.Blackout => "BLACKOUT! The lights are out.",
            HS_RoundEvent.Frenzy => "FRENZY! The seekers are faster for a while.",
            HS_RoundEvent.Shuffle => "SHUFFLE! Hiders, get to another room or be pinged.",
            _ => "",
        };

        /// <summary>A hider who reaches a room other than the one they started the shuffle in is paid once.</summary>
        void CheckShuffle() {
            foreach (var pair in shuffleRooms) {
                if (shuffled.Contains(pair.Key) || RoleOf(pair.Key) != HS_Role.Hider || !TryGetLiveCharacter(pair.Key, out LocalCharacter hider)) continue;
                string room = map.AreaAt(hider.transform.position);
                if (room.Length == 0 || room == pair.Value) continue;
                shuffled.Add(pair.Key);
                AddFeaturedPoints(pair.Key, scoring.shufflePoints, HS_RoundEvent.Shuffle, "Changed rooms", "Changed rooms (Shuffle Day)");
            }
        }

        /// <summary>Ends the running event. A shuffle that ran its course pings every hider who stayed put.</summary>
        void EndEvent(bool roundOver) {
            HS_RoundEvent kind = activeEvent;
            if (kind == HS_RoundEvent.None) return;
            activeEvent = HS_RoundEvent.None;
            if (kind == HS_RoundEvent.Shuffle && !roundOver) PingStayers();
            if (kind == HS_RoundEvent.Frenzy) ApplyAll();
            InstanceFinder.ServerManager.Broadcast(new HS_EventBroadcast { round = round, kind = kind, started = false });
            MarkDirty();
        }

        void PingStayers() {
            var positions = new List<Vector3>();
            var stayers = new HashSet<NetworkConnection>();
            foreach (var pair in shuffleRooms) {
                if (!HS_RoundEnd.PingedAfterShuffle(shuffled.Contains(pair.Key), RoleOf(pair.Key) == HS_Role.Hider, IsBlended(pair.Key))
                    || !TryGetLiveCharacter(pair.Key, out LocalCharacter hider)) continue;
                positions.Add(HS_Rules.Blur(hider.transform.position, random));
                stayers.Add(pair.Key);
            }
            SendPing(positions, "You didn't move! The seekers got a fix on you.", pair => stayers.Contains(pair.Key));
        }

        /// <summary>The round is over: end whatever runs (without its penalty) and forget the plan.</summary>
        void StopEvent() {
            plannedEvent = HS_RoundEvent.None;
            EndEvent(true);
        }
    }
}
