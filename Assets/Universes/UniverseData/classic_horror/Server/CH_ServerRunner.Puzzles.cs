using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>
    /// The minigames and the Hush. Escaping needs more than carrying things: the ferry's engine has to be turned over
    /// (a timing game), each freed singer's hymn sung back (a memory game) and the bell's three locks picked (a dial you
    /// feel your way round). Working a station roots the investigator, makes noise he can hear, and ends the moment he
    /// starts chasing them; every mistake carries to him. The rules are <see cref="CH_PuzzleRun"/>; this is the part that
    /// needs the world: who may reach a station, what a mistake sounds like, what a success earns.
    /// </summary>
    public sealed partial class CH_ServerRunner {
        [Header("Minigames and the Hush")]
        [SerializeField] CH_PuzzleTuning puzzleTuning = new();

        sealed class OpenStation {
            public NetworkConnection connection;
            public LocalCharacter character;
            public CH_Night.Station station;
            public CH_PuzzleRun run;
            public float lastInput;
            public CH_PuzzleAction lastAction;
        }

        readonly Dictionary<NetworkConnection, OpenStation> openStations = new();
        readonly List<NetworkConnection> closing = new();
        float hushUntil, nextHush;
        bool hymnTipCalled;

        /// <summary>He is in the Hush: he hears a walker from further, and even someone standing still nearby.</summary>
        public bool Hushing => Time.time < hushUntil;

        /// <summary>How far he hears someone moving at <paramref name="speed"/> right now because of the Hush (0 outside one).</summary>
        public float HushHearingOf(float speed) =>
            Hushing ? puzzleTuning.HushHearing(speed, monsterTuning.walkNoiseSpeed, monsterTuning.sprintNoiseSpeed) : 0f;

        /// <summary>How far he hears <paramref name="character"/> working a station (0 when they are not).</summary>
        public float PuzzleNoiseOf(LocalCharacter character) {
            if (character == null || !openStations.TryGetValue(character.Owner, out OpenStation open) || open.character != character) return 0f;
            return open.station.Kind switch {
                CH_PuzzleKind.Engine => puzzleTuning.engineWorkNoise,
                CH_PuzzleKind.Hymn => puzzleTuning.hymnWorkNoise,
                _ => puzzleTuning.lockWorkNoise
            };
        }

        public bool IsWorkingStation(LocalCharacter character) => character != null && openStations.TryGetValue(character.Owner, out OpenStation open) && open.character == character;

        void RegisterPuzzleHandlers() => InstanceFinder.ServerManager.RegisterBroadcast<CH_PuzzleRequest>(OnPuzzleRequest);

        void UnregisterPuzzleHandlers() {
            if (InstanceFinder.ServerManager != null) InstanceFinder.ServerManager.UnregisterBroadcast<CH_PuzzleRequest>(OnPuzzleRequest);
        }

        void ResetPuzzles() {
            openStations.Clear();
            hushUntil = nextHush = 0f;
            hymnTipCalled = false;
        }

        // ---- Requests ----------------------------------------------------------------------------------------------------

        void OnPuzzleRequest(NetworkConnection connection, CH_PuzzleRequest request, Channel channel) {
            if (night == null || !CH_Story.IsPlayable(night.Phase) || !connection.IsAuthenticated || request.seed != night.Seed
                || !LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return;
            if (request.action == CH_PuzzleAction.Open) { TryOpenStation(connection, character, night.GetStation(request.stationId)); return; }
            if (!openStations.TryGetValue(connection, out OpenStation open) || open.station.Id != request.stationId) return;
            if (request.action == CH_PuzzleAction.Close) { CloseStation(connection, CH_PuzzleEvent.Closed, ""); return; }
            // Only a repeat of the same action is rate-limited, so setting a lock right after a probe is never dropped.
            if (request.action == open.lastAction && Time.unscaledTime - open.lastInput < puzzleTuning.inputGap) return;
            open.lastInput = Time.unscaledTime;
            open.lastAction = request.action;
            CH_PuzzleStep step = request.action switch {
                CH_PuzzleAction.Strike => open.run.Strike(Time.time),
                CH_PuzzleAction.Press => open.run.Press(Mathf.RoundToInt(request.value)),
                CH_PuzzleAction.Probe => open.run.Probe(request.value),
                CH_PuzzleAction.Set => open.run.Set(request.value),
                _ => default
            };
            if (step.total == 0) return;
            if (step.evt == CH_PuzzleEvent.Mistake) {
                // The card says whether he heard it, so "He heard that." is never shown when he did not.
                bool heard = OnPuzzleMistake(open);
                Send(open, step, heard ? CH_Story.MistakeHeard : CH_Story.MistakeUnheard);
                return;
            }
            Send(open, step);
            if (step.evt == CH_PuzzleEvent.Solved) OnStationSolved(open);
        }

        void TryOpenStation(NetworkConnection connection, LocalCharacter character, CH_Night.Station station) {
            string refusal = null;
            if (openStations.ContainsKey(connection)) return;
            if (station == null || !night.IsAvailable(station)) refusal = "Not now.";
            else if (hidden.ContainsKey(character)) refusal = "Leave your hiding place first.";
            else if (!CanReach(character, CH_Night.StationReachPoint(station.Kind, station.Position), puzzleTuning.reach)) refusal = "Move closer.";
            else if (station.Kind != CH_PuzzleKind.BellLock && night.CarriedBy(CarrierKey(connection)) is { } held && held.Kind == CH_ItemKind.Bell) refusal = "The bell is ringing.";
            else foreach (OpenStation other in openStations.Values) if (other.station == station) { refusal = "Someone is already at it."; break; }
            if (refusal != null) {
                InstanceFinder.ServerManager.Broadcast(connection, new CH_PuzzleBroadcast { seed = night.Seed, stationId = station?.Id ?? -1, evt = CH_PuzzleEvent.Closed, message = refusal });
                return;
            }
            var run = new CH_PuzzleRun(station, night.Seed, puzzleTuning, Time.time, night.HymnOrder(station));
            var open = new OpenStation { connection = connection, character = character, station = station, run = run, lastInput = Time.unscaledTime };
            openStations[connection] = open;
            if (PlayerData.TryGetPlayerData(connection, out var player)) ApplyMovement(player);
            Debug.Log($"[Drowned Choir] client {connection.ClientId} works the {station.Kind} (step {station.Progress}/{run.Total}) at {NightSeconds}s");
            Send(open, run.Opening(Time.time));
        }

        void Send(OpenStation open, CH_PuzzleStep step, string message = "") {
            InstanceFinder.ServerManager.Broadcast(open.connection, new CH_PuzzleBroadcast {
                seed = night.Seed, stationId = open.station.Id, kind = open.station.Kind, evt = step.evt,
                progress = step.progress, total = step.total, mistakes = open.run.Mistakes, warmth = step.warmth,
                zoneCenter = step.zoneCenter, zoneWidth = step.zoneWidth, sweepSpeed = step.sweepSpeed,
                sequence = step.sequence ?? Array.Empty<int>(), message = message
            });
        }

        /// <summary>Takes <paramref name="connection"/> away from the station they work, and lets them move again.</summary>
        void CloseStation(NetworkConnection connection, CH_PuzzleEvent why, string message) {
            if (!openStations.TryGetValue(connection, out OpenStation open)) return;
            openStations.Remove(connection);
            if (PlayerData.TryGetPlayerData(connection, out var player)) ApplyMovement(player);
            if (connection.IsActive && night != null)
                InstanceFinder.ServerManager.Broadcast(connection, new CH_PuzzleBroadcast {
                    seed = night.Seed, stationId = open.station.Id, kind = open.station.Kind, evt = why,
                    progress = open.station.Progress, total = open.run.Total, mistakes = open.run.Mistakes,
                    sequence = Array.Empty<int>(), message = message
                });
        }

        void CloseAllStations(string message) {
            closing.Clear();
            closing.AddRange(openStations.Keys);
            foreach (NetworkConnection connection in closing) CloseStation(connection, CH_PuzzleEvent.Closed, message);
        }

        // ---- What a station does to the night ---------------------------------------------------------------------------------

        /// <summary>A missed strike, a wrong note, a wrong lock: he hears it if he is in earshot, and every further mistake carries further.</summary>
        bool OnPuzzleMistake(OpenStation open) {
            CH_Night.Station station = open.station;
            float baseNoise = station.Kind switch {
                CH_PuzzleKind.Engine => puzzleTuning.engineMissNoise,
                CH_PuzzleKind.Hymn => puzzleTuning.hymnWrongNoise,
                _ => puzzleTuning.lockClankNoise
            };
            float noise = baseNoise * (1f + puzzleTuning.mistakeNoiseGrowth * Mathf.Max(0, open.run.Mistakes - 1));
            Vector3 at = station.Position;
            var scare = station.Kind switch {
                CH_PuzzleKind.Engine => CH_AmbientScare.DoorSlam,
                CH_PuzzleKind.Hymn => CH_AmbientScare.DistantCry,
                _ => CH_AmbientScare.DoorSlam
            };
            InstanceFinder.ServerManager.Broadcast(new CH_AmbientScareBroadcast { seed = night.Seed, kind = scare, position = at });
            if (station.Kind == CH_PuzzleKind.Engine) QueueEar(CH_Story.LineEngineMiss, 0f);
            else if (station.Kind == CH_PuzzleKind.Hymn) QueueEar(CH_Story.LineHymnWrong, 0f);
            PlayerProgress.Modify<CH_SaveData>(open.connection, save => save.mistakesMade++);
            bool heard = monster != null && Vector3.Distance(monster.transform.position, at) <= noise;
            Debug.Log($"[Drowned Choir] {station.Kind} mistake #{open.run.Mistakes} by client {open.connection.ClientId}: noise {noise:0}m, {(heard ? "he heard it" : "out of earshot")}");
            if (!heard) return false;
            monster.Investigate(at, puzzleTuning.noiseSearchSeconds);
            if (open.run.Mistakes >= puzzleTuning.mistakesBeforeHeSpeaks) SpeakFromMonster(CH_Story.LineCraneHears);
            return true;
        }

        void OnStationSolved(OpenStation open) {
            CH_Night.Station station = open.station;
            Debug.Log($"[Drowned Choir] {station.Kind}{(station.Singer >= 0 ? " of " + CH_Story.SingerName(station.Singer) : "")} done by client {open.connection.ClientId} after {open.run.Mistakes} mistakes at {NightSeconds}s");
            Award(open.connection, deliveryXPReward, deliveryCreditReward, "finishing a minigame");
            PlayerProgress.Modify<CH_SaveData>(open.connection, save => {
                save.RecordPuzzle(station.Kind);
                if (station.Kind == CH_PuzzleKind.Hymn && station.Singer >= 0) save.singersMet |= 1 << station.Singer;
            });
            switch (station.Kind) {
                case CH_PuzzleKind.Engine:
                    QueueEar(CH_Story.LineEngineStarts, 0f);
                    break;
                case CH_PuzzleKind.Hymn:
                    // The singer speaks from their grave, then the chapel answers.
                    Broadcast(CH_Story.PlaceLine(CH_ItemKind.Hymnal, station.Singer), CH_VoiceMode.World, station.Position + Vector3.up * 1.2f);
                    if (night.SingersFreed == 1) QueueEar("mara_chapel_1", 6f);
                    if (night.SingersFreed == 2) {
                        QueueEar("mara_chapel_2", 6f);
                        if (!angryCalled) { angryCalled = true; SpeakFromMonster("crane_angry"); }
                    }
                    break;
                default:
                    QueueEar(CH_Story.LineLockOpen, 0f);
                    break;
            }
            // The investigator leaves the station on their own once the panel has shown the win.
            if (PlayerData.TryGetPlayerData(open.connection, out var player)) { openStations.Remove(open.connection); ApplyMovement(player); }
            BroadcastState();
        }

        // ---- Tick --------------------------------------------------------------------------------------------------------

        /// <summary>Ends a station for anyone it should not hold: dead, walked or pushed away, hunted, or the act has moved on.</summary>
        void UpdateStations() {
            if (openStations.Count == 0) return;
            closing.Clear();
            foreach (var pair in openStations) closing.Add(pair.Key);
            foreach (NetworkConnection connection in closing) {
                if (!openStations.TryGetValue(connection, out OpenStation open)) continue;
                LocalCharacter character = open.character;
                if (character == null || character.IsDead || !connection.IsActive) { CloseStation(connection, CH_PuzzleEvent.Closed, ""); continue; }
                if (!night.IsAvailable(open.station)) { CloseStation(connection, CH_PuzzleEvent.Closed, ""); continue; }
                if (monster != null && monster.IsChasing && monster.Quarry == character) {
                    CloseStation(connection, CH_PuzzleEvent.Interrupted, "He is coming!");
                    continue;
                }
                Vector3 reach = CH_Night.StationReachPoint(open.station.Kind, open.station.Position);
                Vector3 eye = character.CharacterCamera != null ? character.CharacterCamera.position : character.transform.position + Vector3.up * 1.6f;
                if (Vector3.Distance(eye, reach) > puzzleTuning.walkAwayDistance) CloseStation(connection, CH_PuzzleEvent.Closed, "");
            }
        }

        /// <summary>The Hush: now and then the choir stops, the lamps gutter, and for a few seconds he hears everything.</summary>
        void UpdateHush() {
            if (!ambientScares || !nightReleased || night.Phase is not (CH_Phase.Choir or CH_Phase.Sermon) || Time.time < nextHush) return;
            if (nextHush == 0f) { nextHush = Time.time + RandomRange(puzzleTuning.hushInterval) * puzzleTuning.firstHushShare; return; }
            if (monster != null && monster.IsChasing) { nextHush = Time.time + puzzleTuning.hushRetrySeconds; return; }
            hushUntil = Time.time + puzzleTuning.hushSeconds;
            nextHush = hushUntil + RandomRange(puzzleTuning.hushInterval);
            InstanceFinder.ServerManager.Broadcast(new CH_AmbientScareBroadcast {
                seed = night.Seed, kind = CH_AmbientScare.Hush, position = map.altar.position, seconds = puzzleTuning.hushSeconds
            });
            Broadcast(CH_Story.HushLines[UnityEngine.Random.Range(0, CH_Story.HushLines.Length)], CH_VoiceMode.Ear, Vector3.zero);
            Debug.Log($"[Drowned Choir] Hush for {puzzleTuning.hushSeconds:0}s at {NightSeconds}s");
        }

        // ---- Views -------------------------------------------------------------------------------------------------------

        CH_StationView[] stationViews = Array.Empty<CH_StationView>();
        string[] stationTitles = Array.Empty<string>();

        void CacheStationTitles() {
            stationTitles = new string[night.Stations.Count];
            for (int i = 0; i < stationTitles.Length; i++) stationTitles[i] = CH_Story.StationTitle(night.Stations[i].Kind, night.Stations[i].Singer);
            stationViews = new CH_StationView[night.Stations.Count];
        }

        CH_StationView[] MakeStationViews() {
            for (int i = 0; i < stationViews.Length; i++) {
                CH_Night.Station station = night.Stations[i];
                int total = station.Kind switch {
                    CH_PuzzleKind.Engine => puzzleTuning.engineStrikes,
                    CH_PuzzleKind.Hymn => puzzleTuning.hymnBaseLength + night.HymnOrder(station),
                    _ => puzzleTuning.lockRings
                };
                stationViews[i] = new CH_StationView {
                    id = station.Id, kind = station.Kind, position = station.Position, yaw = station.Yaw, singer = (sbyte)station.Singer,
                    available = night.IsAvailable(station), done = station.Done, progress = station.Progress, total = total, title = stationTitles[i]
                };
            }
            return stationViews;
        }
    }
}
