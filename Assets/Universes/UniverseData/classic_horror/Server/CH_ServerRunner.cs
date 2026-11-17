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
        [Tooltip("Seconds into Chapter I before the first strange voice on the radio.")]
        [SerializeField, Min(5)] float earlyStoryBeatSeconds = 55f;
        [Header("The Presence")]
        [Tooltip("Speeds, strike and pursuit numbers for the monster.")]
        [SerializeField] CH_MonsterTuning monsterTuning = new();
        [Tooltip("Seconds it keeps its distance at the start of a case, so the team can leave the jetty before the hunt begins.")]
        [SerializeField, Min(0)] float openingGraceSeconds = 45f;
        [Tooltip("Seconds it hunts harder after the second required record is secured. Kept short: it punishes progress.")]
        [SerializeField, Min(0)] float halfwayEnrageSeconds = 2f;
        [Tooltip("Seconds it hunts harder when Chapter II begins.")]
        [SerializeField, Min(0)] float chapterTwoEnrageSeconds = 4f;
        [Tooltip("Seconds it hunts harder after an offering is placed in the wrong order.")]
        [SerializeField, Min(0)] float wrongOfferingEnrageSeconds = 8f;
        [Tooltip("Seconds it keeps its distance after it catches someone, so the team can regroup and a recovered "
            + "investigator is not hunted the moment they are back on their feet.")]
        [SerializeField, Min(0)] float caughtRepelSeconds = 20f;
        [Tooltip("Seconds it falters after a diary page is read aloud.")]
        [SerializeField, Min(0)] float diaryRepelSeconds = 6f;
        [Tooltip("Seconds it holds back when the escape begins.")]
        [SerializeField, Min(0)] float escapeRepelSeconds = 12f;
        [Header("Encounter Scares")]
        [Tooltip("Warning and discovery scares an investigator can see per case. Fatal scares are not limited.")]
        [SerializeField, Min(0)] int nonFatalScaresPerInvestigator = 2;
        [Tooltip("Seconds between two non-fatal scares for the same investigator.")]
        [SerializeField, Min(0)] float nonFatalScareCooldown = 90f;
        [Tooltip("The Presence shows itself when it is hunting an investigator it can see within this distance.")]
        [SerializeField, Min(1)] float proximityScareDistance = 6f;
        [Tooltip("Seconds the Presence stands still after a warning scare: the face is on screen for about 2.25 s, "
            + "and the rest is the investigator's head start once it fades.")]
        [SerializeField, Min(0)] float warningStaggerSeconds = 4.5f;
        [Tooltip("Seconds the Presence then withdraws after a warning, as it does after a catch: the warning is a real "
            + "chance to get away, not a countdown to the strike.")]
        [SerializeField, Min(0)] float warningRetreatSeconds = 8f;
        [Tooltip("Seconds the Presence stands still after a discovery scare.")]
        [SerializeField, Min(0)] float discoveryStaggerSeconds = 2f;
        [Header("Story Timing")]
        [Tooltip("Seconds into Chapter II before the priest's invitation plays.")]
        [SerializeField, Min(0)] float priestInvitationDelay = 25f;
        [Tooltip("Seconds into the escape before the missing person begs for help.")]
        [SerializeField, Min(0)] float escapeBeatDelay = 20f;
        [Header("Ambient Scares")]
        [Tooltip("Door slams, distant cries, failing lights and the chapel bell on a timer.")]
        [SerializeField] bool ambientScares = true;
        [Tooltip("Quiet time after a case starts before the first ambient scare.")]
        [SerializeField, Min(0)] float ambientScareGrace = 60f;
        [SerializeField, Min(5)] float ambientScareMinInterval = 70f;
        [SerializeField, Min(5)] float ambientScareMaxInterval = 140f;
        [Tooltip("An ambient scare due during a chase waits this long and tries again; the chase is scary enough.")]
        [SerializeField, Min(1)] float ambientScareChaseRetry = 12f;
        [SerializeField, Min(0.5f)] float lightsOutSeconds = 3f;
        [Tooltip("A door slams at a search location this far from the investigator it is meant for.")]
        [SerializeField] Vector2 doorSlamDistance = new(10f, 28f);
        [Tooltip("Chance that the radio comments on an ambient scare.")]
        [SerializeField, Range(0, 1)] float ambientRadioChance = 0.5f;
        [Header("Equipment")]
        [Tooltip("Investigators spawn with the flashlight switched on, so nobody starts the case in total darkness.")]
        [SerializeField] bool flashlightStartsOn = true;
        [Tooltip("Zero creates a fresh case every round. Nonzero reproduces a case for authoring.")]
        [SerializeField] int fixedSeed;
        readonly Dictionary<NetworkConnection, CH_ScareRation> encounterScares = new();
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
        readonly List<Vector3> livingInvestigators = new();
        readonly List<(Transform anchor, float score)> spawnOrder = new();
        bool caseStartSpawn, beatEarly, beatHalfway;
        float caseStarted, caseEnded, lastSpeech, nextAmbientScare;
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
        public float ProximityScareDistance => proximityScareDistance;
        // The clock stops when the case is closed, so the ending card shows how long it took.
        int CaseSeconds => current == null ? 0 : Mathf.Max(0, Mathf.RoundToInt((CaseActive ? Time.time : caseEnded) - caseStarted));
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
        // Case points (records, the source, the radio) sit this far above their socket: about chest
        // height, so focus and the compass aim at the prop rather than the ground under it.
        static readonly Vector3 PointLift = Vector3.up * 1.3f;
        // The three offering spots at the source: a row this far apart, this high, this far in front.
        const float OfferingSpacing = 1.25f, OfferingHeight = 1f, OfferingSetback = -1.8f;
        // Point titles for ids 0..8 (records, memories, then the Chapter II offerings to recover).
        static readonly string[] PointTitles = { "Witness testimony", "Light observation", "Keeper's instructions", "Survey map", "Lost memory", "Lost memory", "Salt canister", "Hand bell", "Keeper's lantern" };
        const int RelicFirstId = CH_Case.EvidenceRecordCount;

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
            ServerTool.Instance.SpawnTool(character.NetworkObject, ToolEnum.Flashlight, PrepareFlashlight);
        }

        /// <summary>Runs before the tool is spawned, so clients receive it already lit.</summary>
        void PrepareFlashlight(ToolBaseShared tool) {
            if (flashlightStartsOn && tool is ToolFlashlightShared flashlight) flashlight.LightOn.Value = true;
        }

        void OnPlayerLeft(PlayerData player) {
            requestTimes.Remove(player.Owner);
            snapshotTimes.Remove(player.Owner);
            eliminated.Remove(player.Owner);
            reviving.Remove(player.Owner);
            encounterScares.Remove(player.Owner);
        }

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
            current.BindSource(map.sourceLocations[current.SourceIndex].name);
            livesLost = 0;
            eliminated.Clear();
            reviving.Clear();
            accountLives.Clear();
            eliminatedAccounts.Clear();
            encounterScares.Clear();
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
            monster.Initialize(this, npc, current.Temperament, seed, monsterTuning);
            monster.Repel(openingGraceSeconds);
            SetHorrorMusic(MusicSelection.HorrorAmbientMusic);
            deadline = Time.time + investigationSeconds;
            caseStarted = Time.time;
            nextAmbientScare = caseStarted + ambientScareGrace;
            Speak(current.Introduction);
            while (CaseActive) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(deadline - Time.time));
                if (secondsLeft == 0 || EveryoneOut()) {
                    current.Fail();
                    caseEnded = Time.time;
                    Debug.Log($"[Classic Horror] case failed ({(secondsLeft == 0 ? "out of time" : "everyone caught")}) at {CaseSeconds}s, evidence {current.EvidenceCount}, lives lost {livesLost}");
                    ending = current.Ending();
                    pendingSpeech.Clear();
                    Speak("DISPATCH / We have lost the signal. A new investigation will begin shortly.");
                }
                UpdateStoryBeats();
                UpdateAmbientScares();
                // The investigation card shows the chapter and the instruction; the shared top bar keeps the game's name.
                SetTopMessage(string.Empty);
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
            for (int i = nextCaseDelay; i > 0; i--) {
                secondsLeft = i;
                BroadcastState();
                await AwaitTime(1000, token);
            }
        }

        void OnInvestigatorDied(LocalCharacter character, DamageType damage, GameCharacter source) {
            if (!CaseActive || damage == DamageType.Despawn) return;
            if (eliminated.Contains(character.Owner) || reviving.Contains(character.Owner)) return;
            livesLost++;
            Debug.Log($"[Classic Horror] investigator {character.Owner.ClientId} lost a life ({damage}) at case {CaseSeconds}s; lives lost {livesLost}");
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
            monster?.Repel(caughtRepelSeconds);
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
            SendScare(character.Owner, CH_ScareKind.Fatal);
        }

        /// <summary>
        /// Shows the Presence to one investigator for a warning or discovery encounter, at most
        /// <see cref="nonFatalScaresPerInvestigator"/> times per case and never twice within
        /// <see cref="nonFatalScareCooldown"/> seconds, so scares stay rare enough to land.
        /// </summary>
        public bool TryEncounterScare(NetworkConnection connection, CH_ScareKind kind) {
            if (kind == CH_ScareKind.Fatal || connection == null || !connection.IsAuthenticated || !CaseActive) return false;
            encounterScares.TryGetValue(connection, out CH_ScareRation ration);
            if (!ration.TryTake(Time.time, nonFatalScaresPerInvestigator, nonFatalScareCooldown)) return false;
            encounterScares[connection] = ration;
            SendScare(connection, kind);
            // The face fills their screen: hold the Presence until it fades.
            if (monster != null) {
                bool warning = kind == CH_ScareKind.Warning;
                float hold = warning ? warningStaggerSeconds : discoveryStaggerSeconds;
                monster.Stagger(hold, warning ? warningRetreatSeconds : 0f);
                Debug.Log(warning
                    ? $"[Classic Horror] the Presence is held for {hold:0.0}s while the {kind} face plays, then withdraws for {warningRetreatSeconds:0}s"
                    : $"[Classic Horror] the Presence is held for {hold:0.0}s while the {kind} face plays");
            }
            return true;
        }

        void SendScare(NetworkConnection connection, CH_ScareKind kind) {
            InstanceFinder.ServerManager.Broadcast(connection, new CH_ScareBroadcast {
                seed = current.Seed, sequence = ++scareSequence, kind = (byte)kind
            });
            Debug.Log($"[Classic Horror] {kind} encounter for client {connection.ClientId} at case {CaseSeconds}s");
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
            bool itemCollected;
            if (request.targetId >= CH_Case.DiaryFirstId) itemCollected = ReadDiaryPage(connection, request.targetId - CH_Case.DiaryFirstId);
            else if (request.targetId < CH_Case.RequiredLocations) itemCollected = CollectItem(connection, request.targetId);
            else if (CH_Case.IsOfferingId(request.targetId) && current.Phase == CH_Phase.Descent) {
                OfferAtSource(connection, request.targetId - CH_Case.OfferingFirstId);
                itemCollected = false;
            } else if (request.targetId == CH_Case.ExtractionId && current.Extract()) {
                Escape();
                itemCollected = false;
            } else return;
            if (itemCollected) PlayerProgress.Modify<CH_SaveData>(connection, save => save.evidenceFound++);
            if (oldPhase != current.Phase) OnPhaseChanged();
            BroadcastState();
            // The collector receives the cue only after the server has accepted the pickup.
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult {
                seed = current.Seed, accepted = true, playObjectiveCollect = itemCollected, message = ""
            });
        }

        bool ReadDiaryPage(NetworkConnection connection, int page) {
            if (!current.CollectDiary(page)) { RejectInteraction(connection, "This page has already been read."); return false; }
            AwardReward(connection, itemXPReward, itemCreditReward, "securing an item");
            PlayerProgress.Modify<CH_SaveData>(connection, save => save.diaryPagesRead++);
            Speak(current.Diary[page]);
            // Reading his own words aloud makes the priest falter.
            monster?.Repel(diaryRepelSeconds);
            if (current.DiaryComplete) {
                QueueSpeak($"RADIO / The knocking stops. For a moment the whole settlement holds its breath. You know his name now: {current.Priest}. Speak it at the source.", 0f);
                // He answers to his name: the reader glimpses him once.
                TryEncounterScare(connection, CH_ScareKind.Discovery);
            }
            return true;
        }

        bool CollectItem(NetworkConnection connection, int id) {
            if (!current.Collect(id)) { RejectInteraction(connection, "This record has already been secured."); return false; }
            AwardReward(connection, itemXPReward, itemCreditReward, "securing an item");
            bool memory = id >= CH_Case.RequiredEvidence && id < CH_Case.EvidenceRecordCount;
            if (memory) PlayerProgress.Modify<CH_SaveData>(connection, save => save.memoriesRecovered++);
            Speak(id < CH_Case.EvidenceRecordCount ? current.Evidence[id]
                : $"Recovered the {CH_Case.Offerings[id - RelicFirstId]}. {current.RelicCount}/{CH_Case.OfferingCount} offerings secured. Check the ritual order in your journal.");
            Debug.Log($"[Classic Horror] investigator {connection.ClientId} secured item {id} at case {CaseSeconds}s; evidence {current.EvidenceCount}, offerings {current.RelicCount}");
            if (id < CH_Case.RequiredEvidence && current.EvidenceCount < CH_Case.RequiredEvidence) QueueSpeak(current.EvidenceReplies[id], 0f);
            if (id < CH_Case.RequiredEvidence && current.EvidenceCount == 2 && !beatHalfway) {
                beatHalfway = true;
                QueueSpeak(current.StoryBeats[1], 0f);
                monster?.Enrage(halfwayEnrageSeconds);
            }
            return true;
        }

        void OfferAtSource(NetworkConnection connection, int offering) {
            if (current.RelicCount < CH_Case.OfferingCount) { Speak("The source refuses you. Recover all three offerings first."); return; }
            if (current.Offer(offering)) {
                Speak(current.Phase == CH_Phase.Escape
                    ? $"The water falls silent. RUN to the extraction radio. You have {FormatDuration(escapeSeconds)} before it wakes again."
                    : $"{current.OfferingLines[offering]} The {CH_Case.Offerings[offering]} is accepted. {current.RitualStep}/3. Read the next step carefully.");
                return;
            }
            monster?.Enrage(wrongOfferingEnrageSeconds);
            Speak("That was the wrong order. The seal has broken; begin again. Consult the keeper's instructions in your journal.");
            if (current.MistakeLines.Length > 0) QueueSpeak(current.MistakeLines[(current.Mistakes - 1) % current.MistakeLines.Length], 0f);
            TryEncounterScare(connection, CH_ScareKind.Discovery);
        }

        void Escape() {
            caseEnded = Time.time;
            // Counted before the Complete state goes out, so the ending card shows this case.
            completedCases++;
            ending = current.Ending();
            AwardVictoryRewards();
            pendingSpeech.Clear();
            Speak(current.AllMemories
                ? $"{current.Witness} / You found them. You actually found them. Thank you for bringing {current.Missing} home."
                : "DISPATCH / Signal received. You are coming home. Every new case has different evidence, rules and a different ritual.");
        }

        void OnPhaseChanged() {
            if (current.Phase == CH_Phase.Descent) {
                // Chapter I is closed for everyone still in the case.
                foreach (var player in PlayerData.Players.Values)
                    if (player != null && !eliminated.Contains(player.Owner)) PlayerProgress.Modify<CH_SaveData>(player, save => save.chaptersCompleted++);
                deadline = Time.time + descentSeconds;
                // Let the final record be read before the chapter turns.
                pendingSpeech.Clear();
                QueueSpeak(current.ChapterTwoLine, 0f);
                QueueSpeak(current.StoryBeats[2], priestInvitationDelay);
                monster?.Enrage(chapterTwoEnrageSeconds);
            } else if (current.Phase == CH_Phase.Escape) {
                deadline = Time.time + escapeSeconds;
                pendingSpeech.Clear();
                QueueSpeak(current.StoryBeats[3], escapeBeatDelay);
                monster?.Repel(escapeRepelSeconds);
            }
        }

        static string FormatDuration(int seconds) =>
            seconds % 60 == 0 ? $"{seconds / 60} minute{(seconds == 60 ? "" : "s")}" : $"{seconds} seconds";

        void AwardVictoryRewards() {
            int endingIndex = current.EndingIndex(), caseSeconds = CaseSeconds;
            Debug.Log($"[Classic Horror] case closed: ending {endingIndex} in {caseSeconds}s, evidence {current.EvidenceCount}, memories {current.MemoryCount}, lives lost {livesLost}");
            foreach (var player in PlayerData.Players.Values) {
                if (player == null || eliminated.Contains(player.Owner)) continue;
                AwardReward(player.Owner, winXPReward, winCreditReward, "surviving the case");
                PlayerProgress.Modify<CH_SaveData>(player, save => save.RecordCaseClosed(endingIndex, caseSeconds));
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
                position = map.searchLocations[current.LocationIndices[CH_Case.RequiredLocations + page]].position + PointLift;
                return true;
            }
            if (id >= 0 && id < CH_Case.RequiredLocations) {
                if (id >= CH_Case.EvidenceRecordCount && current.Phase != CH_Phase.Descent || current.Collected(id)) return false;
                position = map.searchLocations[current.LocationIndices[id]].position + PointLift;
                return true;
            }
            if (CH_Case.IsOfferingId(id) && current.Phase == CH_Phase.Descent && current.RelicCount == CH_Case.OfferingCount) position = OfferingPosition(id - CH_Case.OfferingFirstId);
            else if (id == CH_Case.SourceId && current.Phase == CH_Phase.Descent) position = map.sourceLocations[current.SourceIndex].position + PointLift;
            else if (id == CH_Case.ExtractionId && current.Phase == CH_Phase.Escape) position = map.extraction.position + PointLift;
            else return false;
            return true;
        }

        Vector3 OfferingPosition(int offering) => map.sourceLocations[current.SourceIndex].position
            + new Vector3((offering - 1) * OfferingSpacing, OfferingHeight, OfferingSetback);

        string Objective() => current?.Phase switch {
            CH_Phase.Investigation => $"CHAPTER I / THE LAST CALL   |   Find the {CH_Case.RequiredEvidence} records",
            CH_Phase.Descent when current.RelicCount < CH_Case.OfferingCount => $"CHAPTER II / WHAT ANSWERED   |   Recover the {CH_Case.OfferingCount} offerings",
            CH_Phase.Descent => $"CHAPTER II / WHAT ANSWERED   |   Seal {map.sourceLocations[current.SourceIndex].name} ({current.RitualStep}/{CH_Case.OfferingCount})",
            CH_Phase.Escape => "CHAPTER II / WHAT ANSWERED   |   Return to the extraction radio",
            CH_Phase.Complete => "CASE CLOSED / YOU ESCAPED",
            CH_Phase.Failed => "CASE CLOSED / STILL MISSING",
            _ => "Waiting for investigators"
        };

        public void Speak(string text) { dialogue = text; dialogueRevision++; lastSpeech = Time.time; }

        /// <summary>
        /// A flavour line from the Presence. It never cuts off a story line that is still being
        /// read; if the radio is busy the warning is simply dropped. Returns whether it played.
        /// </summary>
        public bool TrySpeakWarning(string text) {
            if (Time.time - lastSpeech < SpeechSpacing || pendingSpeech.Count > 0) return false;
            Speak(text);
            return true;
        }

        /// <summary>Plays a radio line after <paramref name="delay"/>, once the current one has been readable.</summary>
        void QueueSpeak(string text, float delay) {
            if (!string.IsNullOrEmpty(text)) pendingSpeech.Add((Time.time + delay, text));
        }

        void UpdateStoryBeats() {
            if (!CaseActive) return;
            if (!beatEarly && current.Phase == CH_Phase.Investigation && Time.time - caseStarted > earlyStoryBeatSeconds) {
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

        /// <summary>
        /// Schedules door slams, distant cries, failing lights and the chapel bell between
        /// <see cref="ambientScareMinInterval"/> and <see cref="ambientScareMaxInterval"/> seconds.
        /// Everyone hears the same event at the same place; clients only present it.
        /// </summary>
        void UpdateAmbientScares() {
            if (!ambientScares || !CaseActive || Time.time < nextAmbientScare) return;
            if (monster != null && monster.IsChasing) { nextAmbientScare = Time.time + ambientScareChaseRetry; return; }
            nextAmbientScare = Time.time + UnityEngine.Random.Range(ambientScareMinInterval, Mathf.Max(ambientScareMinInterval, ambientScareMaxInterval));
            var kind = (CH_AmbientScare)UnityEngine.Random.Range(0, CH_Case.AmbientScareCount);
            if (!TryAmbientScarePosition(kind, out Vector3 position)) return;
            InstanceFinder.ServerManager.Broadcast(new CH_AmbientScareBroadcast {
                seed = current.Seed, kind = kind, position = position,
                seconds = kind == CH_AmbientScare.LightsOut ? lightsOutSeconds : 0f
            });
            if (UnityEngine.Random.value < ambientRadioChance) QueueSpeak(current.AmbientLines[(int)kind], 0f);
            Debug.Log($"[Classic Horror] ambient scare {kind} at case {CaseSeconds}s; next in {nextAmbientScare - Time.time:0}s");
        }

        /// <summary>
        /// The bell rings at the source's chapel; a cry comes from the search location furthest from
        /// the team; a door slams at a location within <see cref="doorSlamDistance"/> of one investigator.
        /// </summary>
        bool TryAmbientScarePosition(CH_AmbientScare kind, out Vector3 position) {
            position = map.sourceLocations[current.SourceIndex].position;
            if (kind is CH_AmbientScare.ChapelBell or CH_AmbientScare.LightsOut) return true;
            livingInvestigators.Clear();
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) livingInvestigators.Add(character.transform.position);
            if (livingInvestigators.Count == 0) return false;
            Vector3 listener = livingInvestigators[UnityEngine.Random.Range(0, livingInvestigators.Count)];
            float bestScore = float.MinValue;
            foreach (Transform location in map.searchLocations) {
                if (location == null) continue;
                float nearestTeammate = float.MaxValue;
                foreach (Vector3 mate in livingInvestigators) nearestTeammate = Mathf.Min(nearestTeammate, Vector3.Distance(mate, location.position));
                float score = kind == CH_AmbientScare.DistantCry
                    ? nearestTeammate
                    : -Mathf.Abs(Vector3.Distance(listener, location.position) - Mathf.Lerp(doorSlamDistance.x, doorSlamDistance.y, 0.5f))
                        - (nearestTeammate < doorSlamDistance.x ? 1000f : 0f);
                if (score <= bestScore) continue;
                bestScore = score;
                position = location.position;
            }
            return true;
        }

        CH_StateBroadcast MakeState() {
            var points = new List<CH_PointState>();
            for (int i = 0; i < CH_Case.RequiredLocations; i++) {
                if (i >= RelicFirstId && current.Phase == CH_Phase.Investigation) continue;
                Transform socket = map.searchLocations[current.LocationIndices[i]];
                points.Add(new CH_PointState {
                    id = i, position = socket.position + PointLift, title = PointTitles[i], area = socket.name,
                    collected = current.Collected(i), navigationTarget = i < CH_Case.RequiredEvidence || i >= RelicFirstId
                });
            }
            for (int page = 0; page < current.Diary.Length; page++) {
                Transform socket = map.searchLocations[current.LocationIndices[CH_Case.RequiredLocations + page]];
                points.Add(new CH_PointState {
                    id = CH_Case.DiaryFirstId + page, position = socket.position + PointLift, title = "Priest's diary page",
                    area = socket.name, collected = current.DiaryCollected(page), navigationTarget = false
                });
            }
            if (current.Phase != CH_Phase.Investigation) {
                Transform source = map.sourceLocations[current.SourceIndex];
                points.Add(new CH_PointState {
                    id = CH_Case.SourceId, position = source.position + PointLift, title = "The source", area = source.name,
                    collected = current.Phase != CH_Phase.Descent, navigationTarget = false
                });
            }
            if (current.Phase == CH_Phase.Descent && current.RelicCount == CH_Case.OfferingCount)
                for (int i = 0; i < CH_Case.OfferingCount; i++) points.Add(new CH_PointState {
                    id = CH_Case.OfferingFirstId + i, position = OfferingPosition(i), title = "Offer " + CH_Case.Offerings[i],
                    area = map.sourceLocations[current.SourceIndex].name, collected = false,
                    navigationTarget = i == current.Order[current.RitualStep]
                });
            points.Add(new CH_PointState {
                id = CH_Case.ExtractionId, position = map.extraction.position + PointLift, title = "Extraction radio", area = "Arrival jetty",
                collected = current.Phase != CH_Phase.Escape, navigationTarget = current.Phase == CH_Phase.Escape
            });
            var journal = new List<string> { current.Introduction };
            for (int i = 0; i < CH_Case.EvidenceRecordCount; i++) if (current.Collected(i)) journal.Add(current.Evidence[i]);
            for (int page = 0; page < current.Diary.Length; page++) if (current.DiaryCollected(page)) journal.Add(current.Diary[page]);
            return new CH_StateBroadcast {
                seed = current.Seed, phase = current.Phase, caseTitle = current.Title, objective = Objective(),
                dialogue = dialogue, dialogueRevision = dialogueRevision, journal = journal.ToArray(), points = points.ToArray(),
                evidenceCount = current.EvidenceCount, relicCount = current.RelicCount, ritualStep = current.RitualStep,
                livesLost = livesLost, livesPerPlayer = livesPerPlayer, secondsLeft = secondsLeft, completedCases = completedCases,
                monsterId = monster != null ? monster.GetComponent<GameCharacter>().ObjectId : -1, ending = ending,
                memoryCount = current.MemoryCount, diaryCount = current.DiaryCount, diaryTotal = current.Diary.Length,
                ritualMistakes = current.Mistakes, caseSeconds = CaseSeconds, temperament = current.Temperament
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
