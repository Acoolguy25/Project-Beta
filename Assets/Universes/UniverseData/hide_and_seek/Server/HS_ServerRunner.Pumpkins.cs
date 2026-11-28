using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Server {
    /// <summary>
    /// The pumpkin hunt (<see cref="HS_Pumpkins"/>): a few glowing jack-o'-lanterns light up on the map's hide
    /// points while the seekers hunt. A hider who walks into one grabs it for points (a full bag of three pays
    /// a bonus), but everyone hears it and the seekers see where. A seeker who reaches one first snuffs it out.
    /// Each take lights another one somewhere else a little later. Pickups are checked every frame, so a
    /// sprinting player never runs through one between two seek ticks.
    /// </summary>
    public sealed partial class HS_ServerRunner {
        [Header("Pumpkin hunt")]
        [SerializeField] HS_PumpkinTuning pumpkinTuning = new();

        readonly List<HS_Pumpkin> pumpkins = new();
        /// <summary>When the next pumpkins light up (Time.time), one entry per pumpkin owed.</summary>
        readonly List<float> pumpkinSchedule = new();
        /// <summary>Pumpkins each player took this round (grabbed or snuffed), for the bag and the per-round cap.</summary>
        readonly Dictionary<NetworkConnection, int> pumpkinsTaken = new();
        readonly List<Vector3> pumpkinSpots = new(), playerSpots = new();
        int nextPumpkinId = 1;

        HS_Pumpkin[] LivePumpkins => pumpkins.ToArray();

        /// <summary>Puts every pumpkin out and forgets the round's counts (a new round, or the round is over).</summary>
        void ResetPumpkins() {
            foreach (HS_Pumpkin pumpkin in pumpkins)
                InstanceFinder.ServerManager.Broadcast(new HS_PumpkinBroadcast { id = pumpkin.id, position = pumpkin.position, take = HS_PumpkinTake.Snuffed, clientId = -1 });
            pumpkins.Clear();
            pumpkinSchedule.Clear();
            pumpkinsTaken.Clear();
            MarkDirty();
        }

        /// <summary>The seekers are released: the first pumpkins light up a little later.</summary>
        void StartPumpkins() {
            pumpkinSchedule.Clear();
            for (int i = 0; i < pumpkinTuning.liveCount; i++) pumpkinSchedule.Add(Time.time + pumpkinTuning.firstSpawnDelay);
        }

        /// <summary>Lights the pumpkins that are due and hands out the ones players walked into (every frame, seek phase only).</summary>
        void TickPumpkins() {
            if (phase != HS_Phase.Seeking || map == null) return;
            for (int due = HS_Pumpkins.TakeDue(pumpkinSchedule, Time.time); due > 0; due--) SpawnPumpkin();
            if (pumpkins.Count == 0) return;
            foreach (var pair in roles) {
                HS_PumpkinTake take = HS_Pumpkins.TakeBy(pair.Value);
                if (take == HS_PumpkinTake.None || !pair.Key.IsActive || !TryGetLiveCharacter(pair.Key, out LocalCharacter character)) continue;
                Vector3 feet = character.transform.position;
                for (int i = 0; i < pumpkins.Count; i++) {
                    if (!HS_Pumpkins.InReach(feet, pumpkins[i].position, pumpkinTuning.pickupRadius, pumpkinTuning.verticalReach)) continue;
                    HS_Pumpkin taken = pumpkins[i];
                    pumpkins.RemoveAt(i);
                    // Paying changes the leaderboard and saves, never roles, so the walk over roles can go on.
                    OnPumpkinTaken(pair.Key, taken, take);
                    break;
                }
                if (pumpkins.Count == 0) break;
            }
        }

        void SpawnPumpkin() {
            if (pumpkins.Count >= pumpkinTuning.liveCount) return;
            pumpkinSpots.Clear();
            foreach (HS_Pumpkin live in pumpkins) pumpkinSpots.Add(live.position);
            playerSpots.Clear();
            foreach (var pair in LocalCharacter.Characters)
                if (pair.Value != null && !pair.Value.IsDead) playerSpots.Add(pair.Value.transform.position);
            int spot = HS_Pumpkins.PickSpot(map.hidePoints, pumpkinSpots, playerSpots, pumpkinTuning.minSpacing, pumpkinTuning.minPlayerDistance, random);
            if (spot < 0) return;
            var pumpkin = new HS_Pumpkin { id = nextPumpkinId++, position = map.hidePoints[spot] };
            pumpkins.Add(pumpkin);
            InstanceFinder.ServerManager.Broadcast(new HS_PumpkinBroadcast { id = pumpkin.id, position = pumpkin.position, take = HS_PumpkinTake.None, clientId = -1 });
            MarkDirty();
        }

        void OnPumpkinTaken(NetworkConnection taker, HS_Pumpkin pumpkin, HS_PumpkinTake take) {
            pumpkinSchedule.Add(Time.time + pumpkinTuning.respawnSeconds);
            int count = (pumpkinsTaken.TryGetValue(taker, out int n) ? n : 0) + 1;
            pumpkinsTaken[taker] = count;
            InstanceFinder.ServerManager.Broadcast(new HS_PumpkinBroadcast { id = pumpkin.id, position = pumpkin.position, take = take, clientId = taker.ClientId, count = count });
            MarkDirty();
            bool paid = count <= pumpkinTuning.paidPerRound;
            if (take == HS_PumpkinTake.Collected) {
                retention.Count(taker, HS_Retention.Stats.Pumpkins);
                PlayerProgress.Modify<HS_SaveData>(taker, save => save.pumpkins++);
                if (!paid) return;
                int inBag = HS_Pumpkins.InBag(count, pumpkinTuning.bagSize);
                AddPoints(taker, pumpkinTuning.collectPoints, $"Pumpkin {inBag}/{pumpkinTuning.bagSize}", true);
                if (HS_Pumpkins.FillsBag(count, pumpkinTuning.bagSize)) AddPoints(taker, pumpkinTuning.bagPoints, "Full pumpkin bag", true);
            } else {
                PlayerProgress.Modify<HS_SaveData>(taker, save => save.pumpkinsSnuffed++);
                if (paid) AddPoints(taker, pumpkinTuning.snuffPoints, "Snuffed a pumpkin", true);
            }
        }

        void ForgetPumpkins(NetworkConnection c) => pumpkinsTaken.Remove(c);
    }
}
