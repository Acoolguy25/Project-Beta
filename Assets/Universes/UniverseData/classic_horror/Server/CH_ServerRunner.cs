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
using RyanAssets.Levels.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>Owns the complete two-chapter cooperative case and all progression.</summary>
    public sealed class CH_ServerRunner : ServerRunner {
        [SerializeField, Min(60)] int investigationSeconds = 720;
        [SerializeField, Min(60)] int descentSeconds = 540;
        [SerializeField, Min(30)] int escapeSeconds = 120;
        [Tooltip("Spawns each investigator gets per case, counting the one they start the case with. "
            + "Losing the last one eliminates them until the next case.")]
        [SerializeField, Min(1)] int livesPerPlayer = 4;
        [SerializeField, Min(3)] int nextCaseDelay = 18;
        [SerializeField, Min(1)] float interactionReach = 3.5f;
        [Header("Investigator Spawning")]
        [Tooltip("How far from the chosen authored location a spawn may be scattered.")]
        [SerializeField, Min(0)] float spawnScatterRadius = 6f;
        [Tooltip("Scattered candidates tried around each authored location before moving to the next one.")]
        [SerializeField, Min(1)] int spawnCandidateAttempts = 6;
        [Tooltip("Respawns avoid authored locations closer than this to the Presence.")]
        [SerializeField, Min(0)] float spawnMonsterSafeDistance = 25f;
        [Header("Progression Rewards")]
        [Tooltip("XP granted to the investigator who secures an evidence item, memory, or offering.")]
        [SerializeField] ulong itemXPReward = 10;
        [Tooltip("Credits (stored as gold) granted to the investigator who secures an evidence item, memory, or offering.")]
        [SerializeField] ulong itemCreditReward = 5;
        [Tooltip("XP granted to every investigator who survives a completed case.")]
        [SerializeField] ulong winXPReward = 100;
        [Tooltip("Credits (stored as gold) granted to every investigator who survives a completed case.")]
        [SerializeField] ulong winCreditReward = 50;
        [Tooltip("Zero creates a fresh case every round. Nonzero reproduces a case for authoring.")]
        [SerializeField] int fixedSeed;
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Dictionary<NetworkConnection, float> snapshotTimes = new();
        readonly HashSet<NetworkConnection> eliminated = new();
        readonly HashSet<NetworkConnection> reviving = new();
        // Lives and eliminations are also kept per account so leaving and rejoining
        // mid-case cannot refill an investigator's lives or skip spectating.
        readonly Dictionary<string, int> accountLives = new();
        readonly HashSet<string> eliminatedAccounts = new();
        readonly List<(float at, string text)> pendingSpeech = new();
        readonly List<Vector3> spawnTeammates = new();
        readonly List<(Transform anchor, float score)> spawnOrder = new();
        bool caseStartSpawn, beatEarly, beatHalfway;
        float caseStarted, lastSpeech;
        int scareSequence;
        CH_Map map;
        CH_Case current;
        CH_Monster monster;
        string dialogue = "", ending = "";
        readonly List<Transform> spawnAnchors = new();
        int dialogueRevision, livesLost, completedCases, lastSeed, secondsLeft;
        float deadline, chaseMusicUntil;
        bool acceptingPlayers;
        public CH_Case CurrentCase => current;
        public CH_Map Map => map;
        public bool CaseActive => current != null && current.Phase is CH_Phase.Investigation or CH_Phase.Descent or CH_Phase.Escape;
        const float PlayerSpawnClearance = 1.25f;
        // Standing room checked around a spawn: the investigator's capsule at scale 1.05.
        const float SpawnCapsuleRadius = 0.4f, SpawnCapsuleHeight = 2.1f;
        // Navigation projection radius. The Presence's tiny agent bakes walkable polygons on
        // crates, porch rails and under low roofs, so only accept points close to the candidate.
        const float SpawnNavSampleRadius = 2.5f;
        // Queued radio lines wait until the current one has been readable for this long.
        const float SpeechSpacing = 9f;
        // Used only when the map is missing or every authored anchor is rejected.
        static readonly Vector3 FallbackSpawn = new(500, 25, 490);
        // Keep tension continuous when the monster briefly loses sight of an investigator.
        const float ChaseMusicReleaseDelay = 4f;

        protected override void Awake() {
            base.Awake();
            ServerPlayerCharacter.CanSpawnFunction = CanSpawn;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            LocalCharacter.LocalCharacterDied += OnInvestigatorDied;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<CH_InteractRequest>(OnInteract);
            InstanceFinder.ServerManager.RegisterBroadcast<CH_StateRequest>(OnStateRequest);
        }

        bool CanSpawn(NetworkConnection conn) {
            if (!acceptingPlayers || map == null || eliminated.Contains(conn)) return false;
            string account = AccountKey(conn);
            return account == null || !eliminatedAccounts.Contains(account);
        }

        static string AccountKey(NetworkConnection conn) =>
            PlayerData.TryGetPlayerData(conn, out var player) && !string.IsNullOrEmpty(player.username.Value)
                ? player.username.Value : null;

        /// <summary>Collects the authored dry-land anchors a spawn may be scattered around.</summary>
        void RebuildSpawnAnchors() {
            spawnAnchors.Clear();
            if (map == null) return;
            // The source locations are deliberately excluded: one of them is the active
            // haunting's source and is no place to put an investigator who just arrived.
            if (map.arrival != null) spawnAnchors.Add(map.arrival);
            foreach (Transform location in map.searchLocations)
                if (location != null) spawnAnchors.Add(location);
        }

        /// <summary>
        /// The whole team arrives together on the jetty when a case starts. A recovered or
        /// late-joining investigator regroups at the authored location closest to a living
        /// teammate that is not close to the Presence, instead of a random corner of the map.
        /// </summary>
        Vector3 SpawnPosition(NetworkConnection conn) {
            if (map == null) return FallbackSpawn;
            Vector3? monsterPosition = monster != null ? monster.transform.position : null;
            bool regroup = !caseStartSpawn && CaseActive;
            OrderSpawnAnchors(conn, regroup, monsterPosition);
            // First pass keeps the Presence's distance; the second accepts any standable point.
            for (int pass = 0; pass < 2; pass++) {
                foreach (var (anchor, _) in spawnOrder) {
                    for (int attempt = 0; attempt < spawnCandidateAttempts; attempt++) {
                        // The first candidate is the authored point itself, then scattered ones.
                        // A team arriving together is spread around the jetty instead of stacked on it.
                        Vector2 scatter = attempt == 0 && regroup ? Vector2.zero : UnityEngine.Random.insideUnitCircle * spawnScatterRadius;
                        Vector3 candidate = anchor.position + new Vector3(scatter.x, 0f, scatter.y);
                        if (TryResolveSpawn(candidate, pass == 0 ? monsterPosition : null, out Vector3 resolved))
                            return resolved;
                    }
                }
            }
            Debug.LogWarning("Classic Horror rejected every spawn candidate; using the arrival jetty.");
            return map.arrival != null ? map.arrival.position + Vector3.up * PlayerSpawnClearance : FallbackSpawn;
        }

        void OrderSpawnAnchors(NetworkConnection conn, bool regroup, Vector3? monsterPosition) {
            spawnOrder.Clear();
            spawnTeammates.Clear();
            if (regroup)
                foreach (var pair in LocalCharacter.Characters)
                    if (pair.Key != conn && pair.Value != null && !pair.Value.IsDead) spawnTeammates.Add(pair.Value.transform.position);
            Vector3 home = map.arrival != null ? map.arrival.position : FallbackSpawn;
            foreach (Transform anchor in spawnAnchors) {
                float score;
                if (!regroup) score = Vector3.Distance(anchor.position, home);
                else if (spawnTeammates.Count == 0) score = anchor == map.arrival ? 0f : 50f + Vector3.Distance(anchor.position, home);
                else {
                    score = float.MaxValue;
                    foreach (Vector3 mate in spawnTeammates) score = Mathf.Min(score, Vector3.Distance(anchor.position, mate));
                }
                if (regroup && monsterPosition.HasValue && Vector3.Distance(anchor.position, monsterPosition.Value) < spawnMonsterSafeDistance)
                    score += 10000f;
                spawnOrder.Add((anchor, score));
            }
            spawnOrder.Sort((a, b) => a.score.CompareTo(b.score));
        }

        /// <summary>
        /// Accepts a spawn point only on dry, open, level ground the navigation mesh covers.
        /// The mesh is baked for the Presence's tiny agent, so it also covers crates, rails and
        /// crawl spaces under porches; a physics check makes sure a full investigator fits.
        /// </summary>
        bool TryResolveSpawn(Vector3 candidate, Vector3? avoid, out Vector3 spawnPosition) {
            spawnPosition = default;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SpawnNavSampleRadius, NavMesh.AllAreas))
                return false;
            if (hit.position.y <= map.waterLevel + 0.1f) return false;
            if (avoid.HasValue && Vector3.Distance(hit.position, avoid.Value) < spawnMonsterSafeDistance) return false;
            foreach (var other in LocalCharacter.Characters.Values)
                if (other != null && !other.IsDead && (other.transform.position - hit.position).sqrMagnitude < 1.44f) return false;
            int mask = SpawnBlockingMask();
            // Solid, roughly level ground directly under the projected point.
            if (!Physics.Raycast(hit.position + Vector3.up * 0.6f, Vector3.down, out RaycastHit ground, 1.4f, mask, QueryTriggerInteraction.Ignore))
                return false;
            if (ground.normal.y < 0.8f || ground.point.y <= map.waterLevel + 0.1f) return false;
            // A wider footprint rules out rails, crate lids and thin ledges.
            for (int i = 0; i < 4; i++) {
                Vector3 offset = Quaternion.Euler(0, i * 90f, 0) * Vector3.forward * (SpawnCapsuleRadius + 0.2f);
                if (!Physics.Raycast(ground.point + offset + Vector3.up * 0.5f, Vector3.down, out RaycastHit edge, 1.1f, mask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(edge.point.y - ground.point.y) > 0.35f)
                    return false;
            }
            // Standing room for the whole investigator, including head clearance.
            Vector3 bottom = ground.point + Vector3.up * (SpawnCapsuleRadius + 0.08f);
            Vector3 top = ground.point + Vector3.up * (SpawnCapsuleHeight - SpawnCapsuleRadius);
            if (Physics.CheckCapsule(bottom, top, SpawnCapsuleRadius, mask, QueryTriggerInteraction.Ignore)) return false;
            // Spawn slightly above the ground so physics settles the character onto it.
            spawnPosition = ground.point + Vector3.up * PlayerSpawnClearance;
            return true;
        }

        static int SpawnBlockingMask() => ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast", "Water");

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
            PreparePlayer(player);
            PlayerProgress.Load<CH_SaveData>(player, CH_SaveData.Universe).Forget();
            string account = AccountKey(player.Owner);
            player.lives.Value = account != null && CaseActive && accountLives.TryGetValue(account, out int left) ? left : livesPerPlayer;
            if (account != null && CaseActive && eliminatedAccounts.Contains(account)) {
                // Rejoining does not undo an elimination; they watch until the next case.
                eliminated.Add(player.Owner);
                player.LockCamera(GameCameraType.SpectateCamera);
            }
        }
        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            reviving.Remove(character.Owner);
            character.Init();
            character.SetEffect(CharacterEffect.Invul, 15f);
            character.SetScale(Vector3.one * 1.05f);
            character.CanSpectate.Value = true;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) PreparePlayer(player);
            // The character event may precede this runner's player event on join.
            ServerTool.Instance.SpawnTool(character.NetworkObject, ToolEnum.Flashlight);
        }
        void OnPlayerLeft(PlayerData player) { requestTimes.Remove(player.Owner); snapshotTimes.Remove(player.Owner); eliminated.Remove(player.Owner); reviving.Remove(player.Owner); }

        protected override async UniTask StartAsync(CancellationToken token) {
            acceptingPlayers = false;
            await WaitForSceneAsync("classic_horror_start", token);
            map = FindAnyObjectByType<CH_Map>();
            if (map == null || !map.IsConfigured)
                throw new InvalidOperationException("Classic Horror requires its configured map and story library.");
            RebuildSpawnAnchors();
            await WaitForPlayersAsync(1, token);
            int seed;
            do { seed = fixedSeed != 0 ? fixedSeed : Guid.NewGuid().GetHashCode(); } while (fixedSeed == 0 && seed == lastSeed);
            lastSeed = seed;
            current = map.storyLibrary.Generate(seed, map.searchLocations.Length, map.sourceLocations.Length);
            string sourceName = map.sourceLocations[current.SourceIndex].name;
            for (int i = 0; i < current.Evidence.Length; i++) current.Evidence[i] = current.Evidence[i].Replace("{source}", sourceName);
            current.ChapterTwoLine = current.ChapterTwoLine.Replace("{source}", sourceName);
            livesLost = 0;
            eliminated.Clear();
            reviving.Clear();
            accountLives.Clear();
            eliminatedAccounts.Clear();
            pendingSpeech.Clear();
            beatEarly = beatHalfway = false;
            scareSequence = 0;
            chaseMusicUntil = 0;
            ending = "";
            requestTimes.Clear();
            snapshotTimes.Clear();
            SetGlobalInvul(false);
            SetTeamKillEnabled(true);
            foreach (var player in PlayerData.Players.Values) { player.lives.Value = livesPerPlayer; PreparePlayer(player); }
            acceptingPlayers = true;
            // The team arrives together on the jetty.
            caseStartSpawn = true;
            try { ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters(); }
            finally { caseStartSpawn = false; }
            var npc = ServerNPC.SpawnNPC(NPCCharacter.Monster, map.monsterSpawn.position, map.gameObject.scene);
            npc.GetComponent<GameCharacter>().SetScale(Vector3.one * 1.1f);
            npc.GetComponent<GameCharacter>().SetTeam(new TeamConfig(TeamColor.Red));
            npc.GetComponent<GameCharacter>().CanSpectate.Value = false;
            npc.GetComponent<GameCharacter>().DisplayName = "The Cursed Priest";
            monster = npc.gameObject.AddComponent<CH_Monster>();
            monster.Initialize(this, npc, current.Temperament, seed);
            SetHorrorMusic(MusicSelection.HorrorAmbientMusic);
            deadline = Time.time + investigationSeconds;
            caseStarted = Time.time;
            Speak(current.Introduction);
            while (CaseActive) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(deadline - Time.time));
                if (secondsLeft == 0 || EveryoneOut()) {
                    current.Fail();
                    ending = $"STILL MISSING\nThe radio repeats your last call. {current.Witness} waits on the jetty until dark. The settlement keeps another name.";
                    pendingSpeech.Clear();
                    Speak("DISPATCH / We have lost the signal. A new investigation will begin shortly.");
                }
                UpdateStoryBeats();
                SetTopMessage(Objective());
                if (monster != null && monster.IsChasing)
                    chaseMusicUntil = Time.time + ChaseMusicReleaseDelay;
                SetHorrorMusic(Time.time <= chaseMusicUntil
                    ? MusicSelection.HorrorChaseMusic
                    : MusicSelection.HorrorAmbientMusic);
                BroadcastState();
                await AwaitTime(1000, token);
            }
            acceptingPlayers = false;
            SetHorrorMusic(MusicSelection.VictoryMusic);
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
            livesLost++;
            string account = AccountKey(character.Owner);
            if (monster != null && source == monster.GetComponent<GameCharacter>()) ScareCaughtByMonster(character);
            character.CanSpectate.Value = false;
            PlayerData.TryGetPlayerData(character.Owner, out var player);
            // lives counts the spawns an investigator has left including the one they just lost,
            // so the last life is spent here rather than granting one more return to play.
            int livesLeft = player != null ? Mathf.Max(0, player.lives.Value - 1) : 0;
            if (player != null) player.lives.Value = livesLeft;
            if (account != null) accountLives[account] = livesLeft;
            if (livesLeft > 0) {
                reviving.Add(character.Owner);
                // Keep the first-person lock through the shared respawn delay.
                Speak($"DISPATCH / Recovering an investigator. They will regroup with the team. {livesLeft} {(livesLeft == 1 ? "life remains" : "lives remain")} for them. Your evidence is safe.");
            } else {
                eliminated.Add(character.Owner);
                if (account != null) eliminatedAccounts.Add(account);
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

        /// <summary>Plays the scare only after the monster has killed this investigator.</summary>
        void ScareCaughtByMonster(LocalCharacter character) {
            if (character == null || !character.Owner.IsAuthenticated || !CaseActive) return;
            InstanceFinder.ServerManager.Broadcast(character.Owner, new CH_ScareBroadcast {
                seed = current.Seed, sequence = ++scareSequence, kind = 2
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
            bool itemCollected = false;
            if (request.targetId >= CH_Case.DiaryFirstId) {
                int page = request.targetId - CH_Case.DiaryFirstId;
                if (!current.CollectDiary(page)) { RejectInteraction(connection, "This page has already been read."); return; }
                itemCollected = true;
                AwardReward(connection, itemXPReward, itemCreditReward, "securing an item");
                Speak(current.Diary[page]);
                // Reading his own words aloud makes the priest falter.
                monster?.Repel(6f);
                if (current.DiaryComplete)
                    QueueSpeak($"RADIO / The knocking stops. For a moment the whole settlement holds its breath. You know his name now: {current.Priest}. Speak it at the source.", 0f);
            } else if (request.targetId < 9) {
                if (!current.Collect(request.targetId)) { RejectInteraction(connection, "This record has already been secured."); return; }
                itemCollected = true;
                AwardReward(connection, itemXPReward, itemCreditReward, "securing an item");
                Speak(request.targetId < 6 ? current.Evidence[request.targetId]
                    : $"Recovered the {CH_Case.Offerings[request.targetId - 6]}. {current.RelicCount}/3 offerings secured. Check the ritual order in your journal.");
                if (request.targetId < 4 && current.EvidenceCount < 4) QueueSpeak(current.EvidenceReplies[request.targetId], 0f);
                if (request.targetId < 4 && current.EvidenceCount == 2 && !beatHalfway) { beatHalfway = true; QueueSpeak(current.StoryBeats[1], 0f); monster?.Enrage(4f); }
            } else if (request.targetId >= 11 && request.targetId <= 13 && current.Phase == CH_Phase.Descent) {
                if (current.RelicCount < 3) { Speak("The source refuses you. Recover all three offerings first."); return; }
                int offering = request.targetId - 11;
                if (current.Offer(offering)) {
                    Speak(current.Phase == CH_Phase.Escape
                        ? "The water falls silent. RUN to the extraction radio. You have two minutes before it wakes again."
                        : $"The {CH_Case.Offerings[offering]} is accepted. {current.RitualStep}/3. Read the next step carefully.");
                } else {
                    monster?.Enrage(14f);
                    Speak("That was the wrong order. The seal has broken; begin again. Consult the keeper's instructions in your journal.");
                }
            } else if (request.targetId == 10 && current.Extract()) {
                AwardVictoryRewards();
                pendingSpeech.Clear();
                ending = Ending();
                Speak(current.MemoryCount == 2
                    ? $"{current.Witness} / You found them. You actually found them. Thank you for bringing {current.Missing} home."
                    : "DISPATCH / Signal received. You are coming home. Every new case has different evidence, rules and a different ritual.");
            } else return;

            if (itemCollected) PlayerProgress.Modify<CH_SaveData>(connection, save => save.evidenceFound++);
            if (oldPhase != current.Phase) {
                if (current.Phase == CH_Phase.Descent) {
                    // Chapter I is closed for everyone still in the case.
                    foreach (var player in PlayerData.Players.Values)
                        if (player != null && !eliminated.Contains(player.Owner)) PlayerProgress.Modify<CH_SaveData>(player, save => save.chaptersCompleted++);
                    deadline = Time.time + descentSeconds;
                    // Let the final record be read before the chapter turns.
                    pendingSpeech.Clear();
                    QueueSpeak(current.ChapterTwoLine, 0f);
                    QueueSpeak(current.StoryBeats[2], 25f);
                    monster?.Enrage(6f);
                } else if (current.Phase == CH_Phase.Escape) {
                    deadline = Time.time + escapeSeconds;
                    pendingSpeech.Clear();
                    QueueSpeak(current.StoryBeats[3], 20f);
                    monster?.Repel(12f);
                }
            }
            BroadcastState();
            // The collector receives the cue only after the server has accepted the pickup.
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult {
                seed = current.Seed, accepted = true, playObjectiveCollect = itemCollected, message = ""
            });
        }

        void AwardVictoryRewards() {
            foreach (var player in PlayerData.Players.Values) {
                if (player == null || eliminated.Contains(player.Owner)) continue;
                AwardReward(player.Owner, winXPReward, winCreditReward, "surviving the case");
                PlayerProgress.Modify<CH_SaveData>(player, save => save.RecordCaseClosed());
            }
        }

        static void AwardReward(NetworkConnection connection, ulong xp, ulong credits, string reason) {
            if (xp == 0 && credits == 0) return;
            if (!LevelsServer.AwardPlayerXPAndGold(connection, xp, credits))
                Debug.LogWarning($"Classic Horror could not award {reason} rewards to connection {connection.ClientId}.");
        }

        static void SetHorrorMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        // A short pursuit can begin and end between the case loop's one-second ticks.
        // Switch on acquisition so even that chase reaches the clients.
        public void OnMonsterChaseStarted() {
            if (!CaseActive) return;
            chaseMusicUntil = Time.time + ChaseMusicReleaseDelay;
            SetHorrorMusic(MusicSelection.HorrorChaseMusic);
        }

        void RejectInteraction(NetworkConnection connection, string message) {
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult { seed = current.Seed, accepted = false, message = message });
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        bool TryGetPoint(int id, out Vector3 position) {
            position = default;
            if (id >= CH_Case.DiaryFirstId) {
                int page = id - CH_Case.DiaryFirstId;
                if (page >= current.Diary.Length || current.DiaryCollected(page)) return false;
                position = map.searchLocations[current.LocationIndices[9 + page]].position + Vector3.up * 1.3f;
                return true;
            }
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

        public void Speak(string text) { dialogue = text; dialogueRevision++; lastSpeech = Time.time; }

        /// <summary>Plays a radio line after <paramref name="delay"/>, once the current one has been readable.</summary>
        void QueueSpeak(string text, float delay) {
            if (!string.IsNullOrEmpty(text)) pendingSpeech.Add((Time.time + delay, text));
        }

        void UpdateStoryBeats() {
            if (!CaseActive) return;
            if (!beatEarly && current.Phase == CH_Phase.Investigation && Time.time - caseStarted > 55f) {
                beatEarly = true;
                QueueSpeak(current.StoryBeats[0], 0f);
            }
            if (pendingSpeech.Count == 0 || Time.time - lastSpeech < SpeechSpacing) return;
            for (int i = 0; i < pendingSpeech.Count; i++) {
                if (pendingSpeech[i].at > Time.time) continue;
                string line = pendingSpeech[i].text;
                pendingSpeech.RemoveAt(i);
                Speak(line);
                return;
            }
        }

        string Ending() {
            string missing = Capitalize(current.Missing);
            bool memories = current.MemoryCount == 2, diary = current.DiaryComplete;
            if (memories && diary)
                return $"LAST RITES\nYou brought {current.Missing} home and spoke {current.Priest}'s name at the source. The water gives back every name it took. At dawn the chapel bell rings once, and never again.";
            if (memories)
                return $"THE WATER REMEMBERS\n{missing} is remembered. At dawn, the settlement's windows reflect the sky again. Somewhere below, {current.Priest} is still walking.";
            if (diary)
                return $"A NAME FOR A NAME\n{current.Priest} finally rests. But the memories of {current.Missing} stayed under the water, and {current.Witness} still leaves the porch light on.";
            return "BORROWED SILENCE\nYou escaped and sealed the source. But the names you left behind still whisper beneath the water, and it will be hungry again.";
        }

        static string Capitalize(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        CH_StateBroadcast MakeState() {
            var points = new List<CH_PointState>();
            string[] labels = { "Witness testimony", "Light observation", "Keeper's instructions", "Survey map", "Lost memory", "Lost memory", "Salt canister", "Hand bell", "Keeper's lantern" };
            for (int i = 0; i < 9; i++) {
                if (i >= 6 && current.Phase == CH_Phase.Investigation) continue;
                Transform socket = map.searchLocations[current.LocationIndices[i]];
                points.Add(new CH_PointState {
                    id = i, position = socket.position + Vector3.up * 1.3f, title = labels[i], area = socket.name,
                    collected = current.Collected(i), navigationTarget = i < 4 || i >= 6
                });
            }
            for (int page = 0; page < current.Diary.Length; page++) {
                Transform socket = map.searchLocations[current.LocationIndices[9 + page]];
                points.Add(new CH_PointState {
                    id = CH_Case.DiaryFirstId + page, position = socket.position + Vector3.up * 1.3f, title = "Priest's diary page",
                    area = socket.name, collected = current.DiaryCollected(page), navigationTarget = false
                });
            }
            if (current.Phase != CH_Phase.Investigation) {
                Transform source = map.sourceLocations[current.SourceIndex];
                points.Add(new CH_PointState {
                    id = 9, position = source.position + Vector3.up * 1.3f, title = "The source", area = source.name,
                    collected = current.Phase != CH_Phase.Descent, navigationTarget = false
                });
            }
            if (current.Phase == CH_Phase.Descent && current.RelicCount == 3)
                for (int i = 0; i < 3; i++) points.Add(new CH_PointState {
                    id = 11 + i, position = OfferingPosition(i), title = "Offer " + CH_Case.Offerings[i],
                    area = map.sourceLocations[current.SourceIndex].name, collected = false,
                    navigationTarget = i == current.Order[current.RitualStep]
                });
            points.Add(new CH_PointState {
                id = 10, position = map.extraction.position + Vector3.up * 1.3f, title = "Extraction radio", area = "Arrival jetty",
                collected = current.Phase != CH_Phase.Escape, navigationTarget = current.Phase == CH_Phase.Escape
            });
            var journal = new List<string> { current.Introduction };
            for (int i = 0; i < 6; i++) if (current.Collected(i)) journal.Add(current.Evidence[i]);
            for (int page = 0; page < current.Diary.Length; page++) if (current.DiaryCollected(page)) journal.Add(current.Diary[page]);
            return new CH_StateBroadcast {
                seed = current.Seed, phase = current.Phase, caseTitle = current.Title, objective = Objective(),
                dialogue = dialogue, dialogueRevision = dialogueRevision, journal = journal.ToArray(), points = points.ToArray(),
                evidenceCount = current.EvidenceCount, relicCount = current.RelicCount, ritualStep = current.RitualStep,
                livesLost = livesLost, livesPerPlayer = livesPerPlayer, secondsLeft = secondsLeft, completedCases = completedCases,
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
            chaseMusicUntil = 0;
            SetHorrorMusic(MusicSelection.GameMusic);
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
