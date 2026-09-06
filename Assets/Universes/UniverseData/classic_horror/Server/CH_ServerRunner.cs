using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using Channel = FishNet.Transporting.Channel;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>Owns the complete two-chapter cooperative case and all progression.</summary>
    public sealed class CH_ServerRunner : ServerRunner {
        [SerializeField, Min(60)] int investigationSeconds = 720;
        [SerializeField, Min(60)] int descentSeconds = 540;
        [SerializeField, Min(30)] int escapeSeconds = 120;
        [UnityEngine.Serialization.FormerlySerializedAs("sharedLossLimit")]
        [SerializeField, Min(0)] int revivesPerPlayer = 3;
        [SerializeField, Min(3)] int nextCaseDelay = 18;
        [SerializeField, Min(1)] float interactionReach = 3.5f;
        [Tooltip("Zero creates a fresh case every round. Nonzero reproduces a case for authoring.")]
        [SerializeField] int fixedSeed;
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Dictionary<NetworkConnection, float> snapshotTimes = new();
        readonly HashSet<NetworkConnection> eliminated = new();
        readonly HashSet<NetworkConnection> reviving = new();
        readonly Dictionary<NetworkConnection, float> nextScare = new();
        int scareSequence;
        CH_Map map;
        CH_Case current;
        CH_Monster monster;
        string dialogue = "", ending = "";
        int dialogueRevision, losses, completedCases, lastSeed, secondsLeft;
        float deadline;
        bool acceptingPlayers;
        public CH_Case CurrentCase => current;
        public CH_Map Map => map;
        public bool CaseActive => current != null && current.Phase is CH_Phase.Investigation or CH_Phase.Descent or CH_Phase.Escape;

        protected override void Awake() {
            base.Awake();
            ServerPlayerCharacter.CanSpawnFunction = CanSpawn;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            LocalCharacter.LocalCharacterDied += OnInvestigatorDied;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<CH_InteractRequest>(OnInteract);
            InstanceFinder.ServerManager.RegisterBroadcast<CH_StateRequest>(OnStateRequest);
        }

        bool CanSpawn(NetworkConnection conn) => acceptingPlayers && map != null && !eliminated.Contains(conn);
        Vector3 SpawnPosition(NetworkConnection conn) => map != null && map.arrival != null
            ? map.arrival.position + Vector3.up * 0.35f : new Vector3(500, 25, 490);

        void PreparePlayer(PlayerData player) {
            player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
            player.cameraTypes.Clear();
            player.LockCamera(GameCameraType.FirstPersonCamera);
            player.walkSpeed.Value = 4.5f;
            player.sprintSpeed.Value = 9.5f;
            player.staminaMax.Value = 100f;
            player.staminaRegen.Value = 16f;
            //player.tools.Clear();
            //player.tools.Add(ToolEnum.Flashlight);
            //ServerTool.Instance.SpawnTool(player.GetCharacter().NetworkObject, ToolEnum.Flashlight);
        }
        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            player.lives.Value = revivesPerPlayer;
            PreparePlayer(player);
        }
        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            reviving.Remove(character.Owner);
            character.SetScale(Vector3.one * 1.05f);
            character.CanSpectate.Value = true;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) PreparePlayer(player);
            // The character event may precede this runner's player event on join.
            ServerTool.Instance.SpawnTool(character.NetworkObject, ToolEnum.Flashlight);
        }
        void OnPlayerLeft(PlayerData player) { requestTimes.Remove(player.Owner); snapshotTimes.Remove(player.Owner); eliminated.Remove(player.Owner); reviving.Remove(player.Owner); nextScare.Remove(player.Owner); }

        protected override async UniTask StartAsync(CancellationToken token) {
            acceptingPlayers = false;
            await WaitForSceneAsync("classic_horror_start", token);
            map = FindAnyObjectByType<CH_Map>();
            if (map == null || !map.IsConfigured)
                throw new InvalidOperationException("Classic Horror requires its configured map and story library.");
            await WaitForPlayersAsync(1, token);
            int seed;
            do { seed = fixedSeed != 0 ? fixedSeed : Guid.NewGuid().GetHashCode(); } while (fixedSeed == 0 && seed == lastSeed);
            lastSeed = seed;
            current = map.storyLibrary.Generate(seed, map.searchLocations.Length, map.sourceLocations.Length);
            string sourceName = map.sourceLocations[current.SourceIndex].name;
            for (int i = 0; i < current.Evidence.Length; i++) current.Evidence[i] = current.Evidence[i].Replace("{source}", sourceName);
            current.ChapterTwoLine = current.ChapterTwoLine.Replace("{source}", sourceName);
            losses = 0;
            eliminated.Clear();
            reviving.Clear();
            nextScare.Clear();
            scareSequence = 0;
            ending = "";
            requestTimes.Clear();
            snapshotTimes.Clear();
            SetGlobalInvul(false);
            SetTeamKillEnabled(true);
            foreach (var player in PlayerData.Players.Values) { player.lives.Value = revivesPerPlayer; PreparePlayer(player); }
            acceptingPlayers = true;
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            var npc = ServerNPC.SpawnNPC(NPCCharacter.Monster, map.monsterSpawn.position, map.gameObject.scene);
            npc.GetComponent<GameCharacter>().SetScale(Vector3.one * 1.1f);
            npc.GetComponent<GameCharacter>().SetTeam(new TeamConfig(TeamColor.Red));
            npc.GetComponent<GameCharacter>().CanSpectate.Value = false;
            npc.GetComponent<GameCharacter>().DisplayName = "The Presence";
            monster = npc.gameObject.AddComponent<CH_Monster>();
            monster.Initialize(this, npc, current.Temperament, seed);
            deadline = Time.time + investigationSeconds;
            Speak(current.Introduction);
            while (CaseActive) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(deadline - Time.time));
                if (secondsLeft == 0 || EveryoneOut()) {
                    current.Fail();
                    ending = "STILL MISSING\nThe radio repeats your last call. The settlement keeps another name.";
                    Speak("DISPATCH / We have lost the signal. A new investigation will begin shortly.");
                }
                SetTopMessage(Objective());
                BroadcastState();
                await AwaitTime(1000, token);
            }
            acceptingPlayers = false;
            SetGlobalInvul(true);
            if (monster != null) monster.Suspend();
            if (current.Phase == CH_Phase.Complete) completedCases++;
            for (int i = nextCaseDelay; i > 0; i--) {
                secondsLeft = i;
                BroadcastState();
                SetTopMessage($"Case closed. New investigation in {i}s");
                await AwaitTime(1000, token);
            }
        }

        void OnInvestigatorDied(LocalCharacter character, DamageType damage, GameCharacter source) {
            if (!CaseActive || damage == DamageType.Despawn) return;
            if (eliminated.Contains(character.Owner) || reviving.Contains(character.Owner)) return;
            losses++;
            Scare(character, 2, true);
            character.CanSpectate.Value = false;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player) && player.lives.Value > 0) {
                player.lives.Value--;
                reviving.Add(character.Owner);
                // Keep the first-person lock through the shared respawn delay.
                // Exhausting the last revive still grants this return to play.
                Speak($"DISPATCH / Recovering an investigator. {player.lives.Value} revives remain for them. Your evidence is safe.");
            } else {
                eliminated.Add(character.Owner);
                if (player != null) player.LockCamera(GameCameraType.SpectateCamera);
                Speak("DISPATCH / Investigator lost. Survivors: finish the case. They will return with the next team.");
            }
            monster?.Repel(8f);
        }

        bool EveryoneOut() {
            if (PlayerData.Players.Count == 0) return false;
            foreach (var player in PlayerData.Players.Values)
                if (!eliminated.Contains(player.Owner)) return false;
            return true;
        }

        public void Scare(LocalCharacter character, byte kind = 0, bool fatal = false) {
            if (character == null || !character.Owner.IsAuthenticated || current == null) return;
            if (!fatal && nextScare.TryGetValue(character.Owner, out float next) && Time.time < next) return;
            nextScare[character.Owner] = Time.time + 28f;
            InstanceFinder.ServerManager.Broadcast(character.Owner, new CH_ScareBroadcast {
                seed = current.Seed, sequence = ++scareSequence, kind = kind
            });
        }

        void OnStateRequest(NetworkConnection connection, CH_StateRequest request, Channel channel) {
            if (current == null || !connection.IsAuthenticated) return;
            if (snapshotTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            snapshotTimes[connection] = Time.unscaledTime;
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        void OnInteract(NetworkConnection connection, CH_InteractRequest request, Channel channel) {
            if (!CaseActive || !connection.IsAuthenticated
                || !LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 0.25f) return;
            requestTimes[connection] = Time.unscaledTime;
            if (request.seed != current.Seed) { RejectInteraction(connection, "A new case has begun. Check your journal."); return; }
            if (!TryGetPoint(request.targetId, out Vector3 point)) { RejectInteraction(connection, "That objective has changed. Your journal has been refreshed."); return; }
            Vector3 eye = character.CharacterCamera != null ? character.CharacterCamera.position : character.transform.position;
            int mask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
            // A small authority margin accounts for the last replicated movement
            // tick; occlusion is still checked against the same physical point.
            if (Vector3.Distance(eye, point) > interactionReach + 0.35f) { RejectInteraction(connection, "Move closer to interact."); return; }
            if (!WorldInteraction.CanReach(eye, point, interactionReach + 0.35f, mask)) { RejectInteraction(connection, "Something blocks your reach. Try the other side."); return; }

            CH_Phase oldPhase = current.Phase;
            if (request.targetId < 9) {
                if (!current.Collect(request.targetId)) { RejectInteraction(connection, "This record has already been secured."); return; }
                // A seed-dependent discovery sting changes location with every case.
                if (request.targetId == (int)(unchecked((uint)current.Seed) % 4)) Scare(character, 1);
                Speak(request.targetId < 6 ? current.Evidence[request.targetId]
                    : $"Recovered the {CH_Case.Offerings[request.targetId - 6]}. {current.RelicCount}/3 offerings secured. Check the ritual order in your journal.");
            } else if (request.targetId >= 11 && request.targetId <= 13 && current.Phase == CH_Phase.Descent) {
                if (current.RelicCount < 3) { Speak("The source refuses you. Recover all three offerings first."); return; }
                int offering = request.targetId - 11;
                if (current.Offer(offering)) {
                    Speak(current.Phase == CH_Phase.Escape
                        ? "The water falls silent. RUN to the extraction radio. You have two minutes before it wakes again."
                        : $"The {CH_Case.Offerings[offering]} is accepted. {current.RitualStep}/3. Read the next step carefully.");
                } else {
                    monster?.Enrage(14f);
                    Scare(character, 1);
                    Speak("That was the wrong order. The seal has broken; begin again. Consult the keeper's instructions in your journal.");
                }
            } else if (request.targetId == 10 && current.Extract()) {
                ending = current.MemoryCount == 2
                    ? "THE WATER REMEMBERS\nYou brought the lost names home. At dawn, the settlement's windows reflect the sky again."
                    : "BORROWED SILENCE\nYou escaped and sealed the source. But the names you left behind still whisper beneath the water.";
                Speak("DISPATCH / Signal received. You are coming home. Every new case has different evidence, rules and a different ritual.");
            } else return;

            if (oldPhase != current.Phase) {
                if (current.Phase == CH_Phase.Descent) {
                    deadline = Time.time + descentSeconds;
                    Speak(current.ChapterTwoLine);
                    monster?.Enrage(6f);
                } else if (current.Phase == CH_Phase.Escape) {
                    deadline = Time.time + escapeSeconds;
                    monster?.Repel(12f);
                }
            }
            BroadcastState();
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult { seed = current.Seed, accepted = true, message = "" });
        }

        void RejectInteraction(NetworkConnection connection, string message) {
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult { seed = current.Seed, accepted = false, message = message });
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        bool TryGetPoint(int id, out Vector3 position) {
            position = default;
            if (id >= 0 && id < 9) {
                if (id >= 6 && current.Phase != CH_Phase.Descent || current.Collected(id)) return false;
                position = map.searchLocations[current.LocationIndices[id]].position + Vector3.up * 1.3f;
                return true;
            }
            if (id >= 11 && id <= 13 && current.Phase == CH_Phase.Descent && current.RelicCount == 3) position = OfferingPosition(id - 11);
            else if (id == 9 && current.Phase == CH_Phase.Descent) position = map.sourceLocations[current.SourceIndex].position + Vector3.up * 1.3f;
            else if (id == 10 && current.Phase == CH_Phase.Escape) position = map.extraction.position + Vector3.up * 1.3f;
            else return false;
            return true;
        }

        Vector3 OfferingPosition(int offering) => map.sourceLocations[current.SourceIndex].position
            + new Vector3((offering - 1) * 1.25f, 1f, -1.8f);

        string Objective() => current?.Phase switch {
            CH_Phase.Investigation => $"CHAPTER I / THE LAST CALL   |   Evidence {current.EvidenceCount}/4",
            CH_Phase.Descent when current.RelicCount < 3 => $"CHAPTER II / WHAT ANSWERED   |   Offerings {current.RelicCount}/3",
            CH_Phase.Descent => $"CHAPTER II / WHAT ANSWERED   |   Seal {map.sourceLocations[current.SourceIndex].name} ({current.RitualStep}/3)",
            CH_Phase.Escape => "CHAPTER II / WHAT ANSWERED   |   Return to the extraction radio",
            CH_Phase.Complete => "CASE CLOSED / YOU ESCAPED",
            CH_Phase.Failed => "CASE CLOSED / STILL MISSING",
            _ => "Waiting for investigators"
        };

        public void Speak(string text) { dialogue = text; dialogueRevision++; }
        CH_StateBroadcast MakeState() {
            var points = new List<CH_PointState>();
            string[] labels = { "Witness testimony", "Light observation", "Keeper's instructions", "Survey map", "Lost memory", "Lost memory", "Salt canister", "Hand bell", "Keeper's lantern" };
            for (int i = 0; i < 9; i++) {
                if (i >= 6 && current.Phase == CH_Phase.Investigation) continue;
                Transform socket = map.searchLocations[current.LocationIndices[i]];
                points.Add(new CH_PointState { id = i, position = socket.position + Vector3.up * 1.3f, title = labels[i], area = socket.name, collected = current.Collected(i) });
            }
            if (current.Phase != CH_Phase.Investigation) {
                Transform source = map.sourceLocations[current.SourceIndex];
                points.Add(new CH_PointState { id = 9, position = source.position + Vector3.up * 1.3f, title = "The source", area = source.name, collected = current.Phase != CH_Phase.Descent });
            }
            if (current.Phase == CH_Phase.Descent && current.RelicCount == 3)
                for (int i = 0; i < 3; i++) points.Add(new CH_PointState {
                    id = 11 + i, position = OfferingPosition(i), title = "Offer " + CH_Case.Offerings[i],
                    area = map.sourceLocations[current.SourceIndex].name, collected = false
                });
            points.Add(new CH_PointState { id = 10, position = map.extraction.position + Vector3.up * 1.3f, title = "Extraction radio", area = "Arrival jetty", collected = current.Phase != CH_Phase.Escape });
            var journal = new List<string> { current.Introduction };
            for (int i = 0; i < 6; i++) if (current.Collected(i)) journal.Add(current.Evidence[i]);
            return new CH_StateBroadcast {
                seed = current.Seed, phase = current.Phase, caseTitle = current.Title, objective = Objective(),
                dialogue = dialogue, dialogueRevision = dialogueRevision, journal = journal.ToArray(), points = points.ToArray(),
                evidenceCount = current.EvidenceCount, relicCount = current.RelicCount, ritualStep = current.RitualStep,
                losses = losses, lossLimit = revivesPerPlayer, secondsLeft = secondsLeft, completedCases = completedCases,
                monsterId = monster != null ? monster.GetComponent<GameCharacter>().ObjectId : -1, ending = ending
            };
        }
        void BroadcastState() { if (current != null) InstanceFinder.ServerManager.Broadcast(MakeState()); }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            acceptingPlayers = false;
            if (monster != null && monster.GetComponent<GameCharacter>().IsSpawned) InstanceFinder.ServerManager.Despawn(monster.gameObject);
            monster = null;
            current = null;
            base.Reset();
        }
        protected override void OnDestroy() {
            LocalCharacter.LocalCharacterDied -= OnInvestigatorDied;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.CanSpawnFunction == CanSpawn) ServerPlayerCharacter.CanSpawnFunction = null;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.UnregisterBroadcast<CH_InteractRequest>(OnInteract);
                InstanceFinder.ServerManager.UnregisterBroadcast<CH_StateRequest>(OnStateRequest);
            }
            base.OnDestroy();
        }
    }
}
