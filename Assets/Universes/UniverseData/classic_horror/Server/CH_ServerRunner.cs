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

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>
    /// Runs one night of The Drowned Choir: the prologue, three acts and the departure, with the items, the hiding places,
    /// the voice lines, the scares and Father Crane. Everything that decides the night happens here; clients present it.
    /// </summary>
    public sealed class CH_ServerRunner : ServerRunner {
        [Header("Night")]
        [Tooltip("Spawns each investigator gets per night, counting the first. Losing the last one leaves them watching.")]
        [SerializeField, Min(1)] int livesPerPlayer = 3;
        [Tooltip("Seconds everyone still ashore has to reach the boat once the bell is aboard.")]
        [SerializeField, Min(5)] int boardingSeconds = 25;
        [SerializeField, Min(3)] int nextNightDelay = 20;
        [Tooltip("A night that has run this long ends in failure, so a stuck server always moves on.")]
        [SerializeField, Min(300)] int maximumNightSeconds = 2400;
        [SerializeField, Min(1)] float interactionReach = 3f;
        [Tooltip("Investigators walk and sprint at these speeds; carrying the fuel can takes the sprint away.")]
        [SerializeField] float walkSpeed = CH_MonsterTuning.InvestigatorWalkSpeed;
        [SerializeField] float sprintSpeed = CH_MonsterTuning.InvestigatorSprintSpeed;
        [SerializeField] float heavyWalkSpeed = 3.9f;
        [Tooltip("Seconds of grace at the start of Act I before the priest starts hunting.")]
        [SerializeField, Min(0)] float openingGraceSeconds = 20f;
        [Tooltip("Deep water pulls an investigator out without spending a life.")]
        [SerializeField] bool drowningCostsLife = false;

        [Header("Spawning")]
        [SerializeField, Min(0)] float spawnScatterRadius = 5f;
        [SerializeField, Min(1)] int spawnCandidateAttempts = 6;
        [Tooltip("Respawns avoid places closer than this to the priest.")]
        [SerializeField, Min(0)] float spawnMonsterSafeDistance = 30f;
        [Tooltip("Items are never laid closer than this to the jetty, so every errand crosses the village.")]
        [SerializeField, Min(0)] float itemMinimumDistanceFromJetty = 30f;

        [Header("Rewards")]
        [SerializeField] ulong deliveryXPReward = 15;
        [SerializeField] ulong deliveryCreditReward = 5;
        [SerializeField] ulong escapeXPReward = 120;
        [SerializeField] ulong escapeCreditReward = 60;
        [SerializeField] ulong trueEndingBonusXP = 80;

        [Header("Father Crane")]
        [SerializeField] CH_MonsterTuning monsterTuning = new();

        [Header("Scares")]
        [Tooltip("Face-to-face warnings an investigator can get per night; the catch itself is never limited.")]
        [SerializeField, Min(0)] int warningsPerInvestigator = 3;
        [SerializeField, Min(0)] float warningCooldown = 60f;
        [Tooltip("He shows his face when he is chasing someone he can see this close (metres)...")]
        [SerializeField, Min(1)] float warningDistance = 7f;
        [Tooltip("...and holds still this long while it is on their screen.")]
        [SerializeField, Min(0)] float warningHoldSeconds = 0.9f;
        [Tooltip("Seconds between an investigator's whispered apparitions (behind you, a watcher in the dark), never in a chase.")]
        [SerializeField] Vector2 apparitionInterval = new(70f, 140f);
        [SerializeField] bool ambientScares = true;
        [SerializeField] Vector2 ambientScareInterval = new(55f, 110f);
        [SerializeField, Min(0.5f)] float lightsOutSeconds = 3.5f;
        [Tooltip("A door slams at a search location this far from the investigator it is meant for.")]
        [SerializeField] Vector2 doorSlamDistance = new(10f, 26f);
        [Tooltip("Zero creates a fresh night every round. Nonzero reproduces one for authoring.")]
        [SerializeField] int fixedSeed;

        // --- runtime ----------------------------------------------------------------------------------------------------
        CH_Map map;
        CH_Night night;
        CH_Monster monster;
        float phaseStarted, nightStarted, nightEnded, boardingEnds;
        int lastSeed, livesLost, escapes, scareSequence;
        bool acceptingPlayers, caseStartSpawn, nightReleased;
        // The running night's token: scene tasks started from Tick die with the night, not only with the runner.
        CancellationToken nightToken;
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Dictionary<NetworkConnection, float> snapshotTimes = new();
        readonly HashSet<NetworkConnection> eliminated = new();
        readonly HashSet<string> eliminatedAccounts = new();
        readonly Dictionary<string, int> accountLives = new();
        readonly HashSet<NetworkConnection> reviving = new();
        readonly Dictionary<NetworkConnection, CH_ScareRation> warnings = new();
        readonly Dictionary<NetworkConnection, float> nextApparition = new();
        readonly Dictionary<LocalCharacter, (CH_HidingSpot spot, float since)> hidden = new();
        readonly List<(float at, string line)> earQueue = new();
        readonly List<Transform> spawnAnchors = new();
        readonly List<(Transform anchor, float score)> spawnOrder = new();
        readonly List<Vector3> teammates = new();
        float earFreeAt, monsterVoiceFreeAt, nextAmbientScare, chaseMusicUntil, nextBroadcast;
        bool firstChaseCalled, chaseTipCalled, hideTipCalled, angryCalled;
        /// <summary>A scene or act change is under way; the story does not advance twice.</summary>
        bool pendingScene;
        /// <summary>When the priest is let go at the start of an act.</summary>
        float releaseAt;
        int nextNightCountdown;

        const float PlayerSpawnClearance = 1.25f;
        const float SpawnCapsuleRadius = 0.4f, SpawnCapsuleHeight = 2.1f, SpawnNavSampleRadius = 2.5f;
        static readonly Vector3 FallbackSpawn = new(500, 25, 490);
        const float ChaseMusicReleaseDelay = 5f;
        /// <summary>Items and slots are interacted with at about chest height above their anchor.</summary>
        static readonly Vector3 InteractLift = Vector3.up * 0.9f;
        const float VoiceGap = 0.35f;
        const float StateInterval = 0.5f;

        public CH_Map Map => map;
        public CH_Phase Phase => night?.Phase ?? CH_Phase.Waiting;
        /// <summary>The priest may move: an act is under way and the opening grace is over.</summary>
        public bool Hunting => night != null && CH_Story.IsPlayable(night.Phase) && nightReleased;
        public int Act => Phase switch { CH_Phase.Choir or CH_Phase.ChapelOpens => 2, CH_Phase.Sermon or CH_Phase.Boarding => 3, _ => 1 };
        public int LivingInvestigators { get; private set; } = 1;
        int NightSeconds => night == null ? 0 : Mathf.Max(0, Mathf.RoundToInt((night.IsOver ? nightEnded : Time.time) - nightStarted));

        protected override void Awake() {
            base.Awake();
            ServerPlayerCharacter.CanSpawnFunction = CanSpawn;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            LocalCharacter.LocalCharacterDied += OnInvestigatorDied;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<CH_InteractRequest>(OnInteract);
            InstanceFinder.ServerManager.RegisterBroadcast<CH_StateRequest>(OnStateRequest);
        }

        // ---- Players ---------------------------------------------------------------------------------------------------

        bool CanSpawn(NetworkConnection conn) {
            if (!acceptingPlayers || map == null || eliminated.Contains(conn)) return false;
            string account = AccountKey(conn);
            return account == null || !eliminatedAccounts.Contains(account);
        }

        static string AccountKey(NetworkConnection conn) =>
            PlayerData.TryGetPlayerData(conn, out var player) && !string.IsNullOrEmpty(player.username.Value) ? player.username.Value : null;

        static int CarrierKey(NetworkConnection conn) => conn.ClientId;

        void PreparePlayer(PlayerData player) {
            player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
            player.cameraTypes.Clear();
            player.LockCamera(GameCameraType.FirstPersonCamera);
            player.staminaMax.Value = 100f;
            player.staminaRegen.Value = 16f;
            ApplyMovement(player);
        }

        /// <summary>Walk and sprint for what the investigator is doing: hiding stands still, the fuel can is heavy.</summary>
        void ApplyMovement(PlayerData player) {
            if (player == null) return;
            LocalCharacter character = player.GetCharacter();
            bool hiding = character != null && hidden.ContainsKey(character);
            bool heavy = night != null && night.CarriedBy(CarrierKey(player.Owner))?.Kind == CH_ItemKind.Fuel;
            player.walkSpeed.Value = hiding ? 0f : heavy ? heavyWalkSpeed : walkSpeed;
            player.sprintSpeed.Value = hiding ? 0f : heavy ? heavyWalkSpeed : sprintSpeed;
            player.jumpHeight.Value = hiding ? 0f : 1.4f;
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            PreparePlayer(player);
            PlayerProgress.Load<CH_SaveData>(player, CH_SaveData.Universe).Forget();
            string account = AccountKey(player.Owner);
            bool active = night != null && !night.IsOver;
            player.lives.Value = account != null && active && accountLives.TryGetValue(account, out int left) ? left : livesPerPlayer;
            if (account != null && active && eliminatedAccounts.Contains(account)) {
                eliminated.Add(player.Owner);
                player.LockCamera(GameCameraType.SpectateCamera);
            }
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            reviving.Remove(character.Owner);
            character.Init();
            character.SetEffect(CharacterEffect.Invul, 8f);
            character.SetScale(Vector3.one * 1.05f);
            character.CanSpectate.Value = true;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) PreparePlayer(player);
            ServerTool.Instance.SpawnTool(character.NetworkObject, ToolEnum.Flashlight, PrepareFlashlight);
            if (night != null) nextApparition[character.Owner] = Time.time + RandomRange(apparitionInterval);
        }

        static void PrepareFlashlight(ToolBaseShared tool) {
            if (tool is ToolFlashlightShared flashlight) flashlight.LightOn.Value = true;
        }

        void OnPlayerLeft(PlayerData player) {
            requestTimes.Remove(player.Owner);
            snapshotTimes.Remove(player.Owner);
            eliminated.Remove(player.Owner);
            reviving.Remove(player.Owner);
            warnings.Remove(player.Owner);
            nextApparition.Remove(player.Owner);
            if (night == null) return;
            // Whatever they carried stays where they stood.
            LocalCharacter character = player.GetCharacter();
            Vector3 at = character != null ? character.transform.position : map.arrival.position;
            DropCarried(player.Owner, at, character != null ? character.transform.eulerAngles.y : 0f);
            if (character != null) hidden.Remove(character);
        }

        // ---- The night ---------------------------------------------------------------------------------------------------

        protected override async UniTask StartAsync(CancellationToken token) {
            acceptingPlayers = false;
            nightToken = token;
            await WaitForSceneAsync("classic_horror_start", token);
            map = FindAnyObjectByType<CH_Map>();
            if (map == null || !map.IsConfigured)
                throw new InvalidOperationException("The Drowned Choir needs its story set on the map: run Ryan > Classic Horror > Build Story Set.");
            RebuildSpawnAnchors();
            await WaitForPlayersAsync(1, token);

            int seed;
            do { seed = fixedSeed != 0 ? fixedSeed : Guid.NewGuid().GetHashCode(); } while (fixedSeed == 0 && seed == lastSeed);
            lastSeed = seed;
            night = new CH_Night(seed, BuildLayout(seed));
            ResetNightState();
            foreach (var player in PlayerData.Players.Values) { player.lives.Value = livesPerPlayer; PreparePlayer(player); }
            acceptingPlayers = true;
            caseStartSpawn = true;
            try { ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters(); }
            finally { caseStartSpawn = false; }
            SpawnMonster(seed);
            nightStarted = Time.time;
            Debug.Log($"[Drowned Choir] night {seed} begins: singers {string.Join(", ", Array.ConvertAll(night.Singers, CH_Story.SingerName))}");

            await PlayCutscene(CH_Phase.Prologue, token);
            if (!night.IsOver) EnterAct(CH_Phase.Stranded);
            while (!night.IsOver) {
                token.ThrowIfCancellationRequested();
                Tick();
                await AwaitTime(100, token);
            }

            nightEnded = Time.time;
            acceptingPlayers = false;
            ReleaseEveryoneFromHiding();
            monster?.Suspend();
            SetGlobalInvul(true);
            SetMusic(night.Phase == CH_Phase.Complete ? MusicSelection.VictoryMusic : MusicSelection.HorrorAmbientMusic);
            RecordEnding();
            for (int i = nextNightDelay; i > 0; i--) {
                nextNightCountdown = i;
                BroadcastState();
                await AwaitTime(1000, token);
            }
        }

        void ResetNightState() {
            livesLost = 0;
            eliminated.Clear();
            eliminatedAccounts.Clear();
            accountLives.Clear();
            reviving.Clear();
            warnings.Clear();
            nextApparition.Clear();
            hidden.Clear();
            earQueue.Clear();
            requestTimes.Clear();
            snapshotTimes.Clear();
            earFreeAt = monsterVoiceFreeAt = 0f;
            chaseMusicUntil = 0f;
            firstChaseCalled = chaseTipCalled = hideTipCalled = angryCalled = false;
            nightReleased = false;
            nextNightCountdown = 0;
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            SetTopMessage(string.Empty);
        }

        void SpawnMonster(int seed) {
            var npc = ServerNPC.SpawnNPC(NPCCharacter.Monster, map.monsterSpawn.position, map.gameObject.scene);
            var character = npc.GetComponent<GameCharacter>();
            character.SetScale(Vector3.one * 1.1f);
            character.SetTeam(new TeamConfig(TeamColor.Red));
            character.CanSpectate.Value = false;
            character.DisplayName = "Father Crane";
            monster = npc.gameObject.AddComponent<CH_Monster>();
            monster.Initialize(this, npc, seed, monsterTuning);
            monster.Suspend();
        }

        /// <summary>Holds the night for a cutscene: everyone is safe, the priest is still, every client plays the scene.</summary>
        async UniTask PlayCutscene(CH_Phase scene, CancellationToken token) {
            ReleaseEveryoneFromHiding();
            night.Enter(scene);
            phaseStarted = Time.time;
            SetGlobalInvul(true);
            // No hunt, scares or apparitions while the scene plays; the next act releases him again after its grace.
            nightReleased = false;
            monster?.Suspend();
            SetMusic(MusicSelection.HorrorAmbientMusic);
            earQueue.Clear();
            foreach (var (at, line) in CH_Story.CutsceneLines(scene)) Broadcast(line, CH_VoiceMode.Ear, Vector3.zero, at);
            BroadcastState();
            float ends = Time.time + CH_Story.CutsceneSeconds(scene);
            while (Time.time < ends && !night.IsOver) {
                token.ThrowIfCancellationRequested();
                FlushVoiceQueue();
                if (EveryoneOut()) { FailNight(); return; }
                if (Time.time >= nextBroadcast) BroadcastState();
                await AwaitTime(100, token);
            }
        }

        void EnterAct(CH_Phase act) {
            night.Enter(act);
            phaseStarted = Time.time;
            SetGlobalInvul(false);
            // The priest stays where the scene left him and starts again after the act's grace.
            nightReleased = false;
            float grace = act == CH_Phase.Stranded ? openingGraceSeconds : act == CH_Phase.Choir ? 10f : 3f;
            releaseAt = Time.time + grace;
            nextAmbientScare = Time.time + Mathf.Max(grace, RandomRange(ambientScareInterval) * 0.6f);
            switch (act) {
                case CH_Phase.Stranded:
                    QueueEar("stranded_hint", 22f);
                    monster?.Warp(map.monsterSpawn.position);
                    break;
                case CH_Phase.Choir:
                    // He dragged Mara into the chapel: he starts the act there.
                    monster?.Warp(map.altar.position + map.altar.forward * 6f);
                    break;
                case CH_Phase.Sermon:
                    monster?.Warp(map.altar.position + map.altar.forward * 10f);
                    break;
            }
            BroadcastState();
        }

        void Tick() {
            LivingInvestigators = CountLiving();
            // A scene's own loop keeps the night going (voices, state, everyone-out) while it plays.
            if (CH_Story.IsCutscene(night.Phase)) return;
            if (!nightReleased && Time.time >= releaseAt && CH_Story.IsPlayable(night.Phase)) {
                nightReleased = true;
                monster?.Resume();
            }
            if (EveryoneOut()) { FailNight(); return; }
            if (NightSeconds > maximumNightSeconds) {
                Debug.Log($"[Drowned Choir] night {night.Seed} ran out the clock at {NightSeconds}s");
                FailNight();
                return;
            }
            KeepHiddenInPlace();
            UpdateBoarding();
            UpdateMusic();
            UpdateApparitions();
            UpdateAmbientScares();
            FlushVoiceQueue();
            AdvanceStory();
            if (Time.time >= nextBroadcast) BroadcastState();
        }

        /// <summary>Starts the next scene or act when the current one is complete.</summary>
        void AdvanceStory() {
            CH_Phase earned = night.EarnedPhase();
            if (earned == night.Phase || pendingScene) return;
            pendingScene = true;
            RunScene(earned).Forget();
        }

        async UniTaskVoid RunScene(CH_Phase earned) {
            CH_Night current = night;
            CancellationToken token = nightToken;
            bool Stale() => night != current || current.IsOver;
            try {
                switch (earned) {
                    case CH_Phase.Taking:
                        await UniTask.Delay(TimeSpan.FromSeconds(2.5f), cancellationToken: token); // let "Lamp's lit" land
                        if (Stale()) return;
                        await PlayCutscene(CH_Phase.Taking, token);
                        if (!Stale()) EnterAct(CH_Phase.Choir);
                        break;
                    case CH_Phase.ChapelOpens:
                        await UniTask.Delay(TimeSpan.FromSeconds(3.5f), cancellationToken: token); // the last singer's thanks
                        if (Stale()) return;
                        await PlayCutscene(CH_Phase.ChapelOpens, token);
                        if (!Stale()) EnterAct(CH_Phase.Sermon);
                        break;
                    case CH_Phase.Boarding:
                        night.Enter(CH_Phase.Boarding);
                        phaseStarted = Time.time;
                        boardingEnds = Time.time + boardingSeconds;
                        BroadcastState();
                        break;
                }
            } catch (OperationCanceledException) {
            } finally {
                pendingScene = false;
            }
        }

        void UpdateBoarding() {
            if (night.Phase != CH_Phase.Boarding || pendingScene) return;
            bool everyoneAboard = true;
            int aboard = 0;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (character == null || character.IsDead || eliminated.Contains(character.Owner)) continue;
                if (IsAboard(character)) aboard++;
                else everyoneAboard = false;
            }
            if (aboard == 0 && Time.time < boardingEnds) return;
            if (!everyoneAboard && Time.time < boardingEnds) return;
            pendingScene = true;
            Depart().Forget();
        }

        bool IsAboard(LocalCharacter character) =>
            Vector3.Distance(Flat(character.transform.position), Flat(map.boardingPoint.position)) <= map.boardingRadius;

        async UniTaskVoid Depart() {
            CH_Night current = night;
            try {
                // Anyone not aboard when the boat leaves is left to the choir.
                foreach (var character in LocalCharacter.Characters.Values) {
                    if (character == null || eliminated.Contains(character.Owner)) continue;
                    if (character.IsDead || !IsAboard(character)) LeaveBehind(character.Owner);
                }
                if (EveryoneOut()) { FailNight(); return; }
                await PlayCutscene(CH_Phase.Departure, nightToken);
                if (night != current || current.IsOver) return;
                escapes++;
                night.Complete();
                Debug.Log($"[Drowned Choir] night {night.Seed} escaped in {NightSeconds}s (music box {(night.MusicBoxReturned ? "returned" : "not returned")}, lives lost {livesLost})");
                Broadcast(night.MusicBoxReturned ? "escape_true" : "escape_plain", CH_VoiceMode.Ear, Vector3.zero);
                BroadcastState();
            } catch (OperationCanceledException) {
            } finally {
                pendingScene = false;
            }
        }

        void LeaveBehind(NetworkConnection connection) {
            eliminated.Add(connection);
            string account = AccountKey(connection);
            if (account != null) { eliminatedAccounts.Add(account); accountLives[account] = 0; }
            if (PlayerData.TryGetPlayerData(connection, out var player)) {
                player.lives.Value = 0;
                player.LockCamera(GameCameraType.SpectateCamera);
            }
        }

        void FailNight() {
            if (night.IsOver) return;
            night.Fail();
            earQueue.Clear();
            Broadcast("fail_1", CH_VoiceMode.Ear, Vector3.zero);
            Debug.Log($"[Drowned Choir] night {night.Seed} lost at {NightSeconds}s in {Phase} ({night.PartsFixed} parts, {night.SingersFreed} singers)");
            BroadcastState();
        }

        void RecordEnding() {
            byte ending = night.Ending;
            int seconds = NightSeconds;
            foreach (var player in PlayerData.Players.Values) {
                if (player == null) continue;
                bool escaped = night.Phase == CH_Phase.Complete && !eliminated.Contains(player.Owner);
                byte theirs = escaped ? ending : CH_Story.EndingDrowned;
                PlayerProgress.Modify<CH_SaveData>(player, save => save.RecordEnding(theirs, seconds));
                if (!escaped) continue;
                ulong xp = escapeXPReward + (ending == CH_Story.EndingTrue ? trueEndingBonusXP : 0UL);
                Award(player.Owner, xp, escapeCreditReward, "escaping Hollowmere");
            }
        }

        // ---- Interaction -------------------------------------------------------------------------------------------------

        void OnInteract(NetworkConnection connection, CH_InteractRequest request, Channel channel) {
            if (night == null || !CH_Story.IsPlayable(night.Phase) || !connection.IsAuthenticated
                || !LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 0.25f) return;
            requestTimes[connection] = Time.unscaledTime;
            if (request.seed != night.Seed) { Reject(connection, request.target, "A new night has begun."); return; }

            if (request.target == CH_Target.Hide) { ToggleHiding(connection, character, request.id); return; }
            if (hidden.ContainsKey(character)) { Reject(connection, request.target, "Leave your hiding place first."); return; }
            if (!TryGetInteractPoint(request.target, request.id, out Vector3 point)) { Reject(connection, request.target, "That's gone."); return; }
            if (!CanReach(character, point)) { Reject(connection, request.target, "Move closer."); return; }

            int key = CarrierKey(connection);
            if (request.target == CH_Target.Item) {
                if (!night.TryPickUp(key, request.id, out CH_Night.Item swapped, out bool firstTouch)) { Reject(connection, request.target, "You can't take that now."); return; }
                OnPickedUp(connection, character, night.GetItem(request.id), firstTouch);
                if (swapped != null) Debug.Log($"[Drowned Choir] client {connection.ClientId} swapped {swapped.Kind} for {night.GetItem(request.id).Kind}");
            } else {
                CH_Night.Slot slot = night.GetSlot(request.id);
                CH_Night.Item held = night.CarriedBy(key);
                if (held == null) { Reject(connection, request.target, Hint(slot)); return; }
                if (!night.TryPlace(key, request.id, out CH_Night.Item placed)) {
                    Reject(connection, request.target, slot.Kind == CH_SlotKind.Grave && held.Singer >= 0
                        ? $"This isn't {CH_Story.SingerName(held.Singer)}'s grave." : $"The {CH_Story.ItemName(held.Kind).ToLowerInvariant()} doesn't go here.");
                    return;
                }
                OnPlaced(connection, placed, slot);
            }
            if (PlayerData.TryGetPlayerData(connection, out var player)) ApplyMovement(player);
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult { seed = night.Seed, accepted = true, target = request.target, message = "" });
            BroadcastState();
        }

        string Hint(CH_Night.Slot slot) => slot.Kind switch {
            CH_SlotKind.Grave => $"Here lies {CH_Story.SingerName(slot.Singer)}.",
            CH_SlotKind.LilyMarker => "Lily Crane, 1954 - 1962.",
            _ => "Bring the missing part here."
        };

        bool CanReach(LocalCharacter character, Vector3 point) {
            Vector3 eye = character.CharacterCamera != null ? character.CharacterCamera.position : character.transform.position + Vector3.up * 1.6f;
            int mask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
            return Vector3.Distance(eye, point) <= interactionReach + 0.5f && WorldInteraction.CanReach(eye, point, interactionReach + 0.5f, mask);
        }

        bool TryGetInteractPoint(CH_Target target, int id, out Vector3 point) {
            point = default;
            if (target == CH_Target.Item) {
                CH_Night.Item item = night.GetItem(id);
                if (item == null || item.State != CH_ItemState.Resting) return false;
                point = item.Position + Vector3.up * 0.3f;
                return true;
            }
            CH_Night.Slot slot = night.GetSlot(id);
            if (slot == null || slot.Filled) return false;
            point = slot.Position + InteractLift;
            return true;
        }

        void OnPickedUp(NetworkConnection connection, LocalCharacter character, CH_Night.Item item, bool firstTouch) {
            Debug.Log($"[Drowned Choir] client {connection.ClientId} picked up {item.Kind} at {NightSeconds}s");
            if (item.Kind == CH_ItemKind.Bell) {
                if (firstTouch) TakeTheBell(connection, character);
                return;
            }
            if (!firstTouch) return;
            string line = CH_Story.PickUpLine(item.Kind, item.Singer);
            if (line != null) QueueEar(line, 0.3f);
            if (item.Kind == CH_ItemKind.Hymnal && !hideTipCalled) { hideTipCalled = true; QueueEar("hide_tip", 0f); }
        }

        /// <summary>The bell leaves the altar: the lights die, he is in the thief's face, Mara is free, and he hunts the bell.</summary>
        void TakeTheBell(NetworkConnection connection, LocalCharacter character) {
            InstanceFinder.ServerManager.Broadcast(new CH_AmbientScareBroadcast {
                seed = night.Seed, kind = CH_AmbientScare.LightsOut, position = map.altar.position, seconds = 2.5f
            });
            foreach (var other in LocalCharacter.Characters.Values)
                if (other != null && !other.IsDead && Vector3.Distance(other.transform.position, map.altar.position) < 30f)
                    SendScare(other.Owner, CH_ScareKind.BellGrab);
            Broadcast("bell_take", CH_VoiceMode.Monster, Vector3.zero);
            QueueEar("mara_free", 5.5f);
            QueueEar("bell_tip", 0f);
            if (monster != null) {
                // He was behind the altar all along.
                Vector3 behind = map.altar.position - map.altar.forward * 1.2f;
                monster.Warp(behind);
                monster.Hold(2.4f);
                monster.Hunt(character);
            }
        }

        void OnPlaced(NetworkConnection connection, CH_Night.Item item, CH_Night.Slot slot) {
            Debug.Log($"[Drowned Choir] client {connection.ClientId} placed {item.Kind} on {slot.Kind} at {NightSeconds}s");
            Award(connection, deliveryXPReward, deliveryCreditReward, "delivering an item");
            int singer = item.Singer;
            PlayerProgress.Modify<CH_SaveData>(connection, save => {
                save.itemsDelivered++;
                if (slot.Kind == CH_SlotKind.Grave && singer >= 0) save.singersMet |= 1 << singer;
            });
            string line = CH_Story.PlaceLine(item.Kind, singer);
            switch (slot.Kind) {
                case CH_SlotKind.BoatFuel or CH_SlotKind.BoatToolbox or CH_SlotKind.BoatLantern:
                    QueueEar(line, 0f);
                    if (night.PartsFixed == 1) QueueEar("stranded_two_left", 0f);
                    else if (night.PartsFixed == 2) QueueEar("stranded_one_left", 0f);
                    break;
                case CH_SlotKind.Grave:
                    // The singer speaks from their grave, then the chapel answers.
                    Broadcast(line, CH_VoiceMode.World, slot.Position + Vector3.up * 1.2f);
                    if (night.SingersFreed == 1) QueueEar("mara_chapel_1", 6f);
                    if (night.SingersFreed == 2) {
                        QueueEar("mara_chapel_2", 6f);
                        if (!angryCalled) { angryCalled = true; SpeakFromMonster("crane_angry"); }
                    }
                    break;
                case CH_SlotKind.LilyMarker:
                    QueueEar(line, 0f);
                    break;
                case CH_SlotKind.BoatBell:
                    QueueEar(line, 0f);
                    break;
            }
        }

        void DropCarried(NetworkConnection connection, Vector3 position, float yaw) {
            if (night == null) return;
            // Set it down on the ground under them, above the waterline where possible.
            Vector3 ground = position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas) && hit.position.y > map.waterLevel) ground = hit.position;
            if (ground.y <= map.waterLevel) ground = map.arrival.position;
            CH_Night.Item dropped = night.Drop(CarrierKey(connection), ground, yaw);
            if (dropped != null) Debug.Log($"[Drowned Choir] client {connection.ClientId} dropped {dropped.Kind}");
        }

        public bool IsCarryingBell(LocalCharacter character) =>
            night != null && character != null && night.CarriedBy(CarrierKey(character.Owner))?.Kind == CH_ItemKind.Bell;

        public bool IsCarryingItem(LocalCharacter character) =>
            night != null && character != null && night.CarriedBy(CarrierKey(character.Owner)) != null;

        // ---- Hiding ------------------------------------------------------------------------------------------------------

        void ToggleHiding(NetworkConnection connection, LocalCharacter character, int spotId) {
            if (hidden.TryGetValue(character, out var current)) {
                LeaveHiding(character, current.spot);
                Accept(connection, CH_Target.Hide);
                return;
            }
            if (spotId < 0 || spotId >= map.hidingSpots.Length || map.hidingSpots[spotId] == null) { Reject(connection, CH_Target.Hide, "There's nowhere to hide here."); return; }
            CH_HidingSpot spot = map.hidingSpots[spotId];
            if (OccupantOf(spot) != null) { Reject(connection, CH_Target.Hide, "Someone is already hiding in there."); return; }
            if (!CanReach(character, spot.Handle)) { Reject(connection, CH_Target.Hide, "Move closer."); return; }
            if (IsCarryingBell(character)) { Reject(connection, CH_Target.Hide, "The bell won't stop ringing. He would hear it."); return; }
            hidden[character] = (spot, Time.time);
            character.TeleportTo(spot.Inside);
            if (PlayerData.TryGetPlayerData(connection, out var player)) ApplyMovement(player);
            Accept(connection, CH_Target.Hide);
            BroadcastState();
        }

        void LeaveHiding(LocalCharacter character, CH_HidingSpot spot) {
            hidden.Remove(character);
            if (!character.IsDead) character.TeleportTo(spot.Exit);
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) ApplyMovement(player);
            BroadcastState();
        }

        void ReleaseEveryoneFromHiding() {
            var inside = new List<(LocalCharacter, CH_HidingSpot)>();
            foreach (var pair in hidden) inside.Add((pair.Key, pair.Value.spot));
            foreach (var (character, spot) in inside) if (character != null) LeaveHiding(character, spot);
            hidden.Clear();
        }

        const float HideSettleSeconds = 1f, HideDriftDistance = 1.5f;

        /// <summary>Client movement is authoritative, so a hidden investigator who drifts out is taken out of hiding.</summary>
        void KeepHiddenInPlace() {
            if (hidden.Count == 0) return;
            List<LocalCharacter> drifted = null;
            foreach (var pair in hidden) {
                bool gone = pair.Key == null || pair.Key.IsDead;
                // Positions the owner sent before the teleport can still arrive just after it: give it a moment to land.
                bool settled = Time.time - pair.Value.since > HideSettleSeconds;
                if (gone || (settled && Vector3.Distance(pair.Key.transform.position, pair.Value.spot.Inside) > HideDriftDistance))
                    (drifted ??= new List<LocalCharacter>()).Add(pair.Key);
            }
            if (drifted == null) return;
            foreach (var character in drifted) {
                hidden.Remove(character);
                if (character != null && PlayerData.TryGetPlayerData(character.Owner, out var player)) ApplyMovement(player);
            }
            BroadcastState();
        }

        public bool IsHidden(LocalCharacter character) => character != null && hidden.ContainsKey(character);
        public CH_HidingSpot HidingPlaceOf(LocalCharacter character) => character != null && hidden.TryGetValue(character, out var entry) ? entry.spot : null;
        public float HiddenSince(LocalCharacter character) => character != null && hidden.TryGetValue(character, out var entry) ? entry.since : float.MaxValue;

        public LocalCharacter OccupantOf(CH_HidingSpot spot) {
            foreach (var pair in hidden) if (pair.Value.spot == spot) return pair.Key;
            return null;
        }

        public CH_HidingSpot NearestHidingPlace(Vector3 position, float within) {
            CH_HidingSpot best = null;
            float bestDistance = within;
            foreach (CH_HidingSpot spot in map.hidingSpots) {
                if (spot == null) continue;
                float distance = Vector3.Distance(position, spot.Exit);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = spot;
            }
            return best;
        }

        /// <summary>He pulled a hiding place open. If someone was in it, they are about to be caught.</summary>
        public void OnHidingPlaceOpened(CH_HidingSpot spot, LocalCharacter inside) {
            if (inside == null) {
                InstanceFinder.ServerManager.Broadcast(new CH_AmbientScareBroadcast { seed = night.Seed, kind = CH_AmbientScare.DoorSlam, position = spot.Exit });
                return;
            }
            Debug.Log($"[Drowned Choir] the priest found client {inside.Owner.ClientId} hiding in the {spot.label}");
            SpeakFromMonster("wardrobe_found", force: true);
            // Pulled out in front of him, able to move again, whether or not his blow lands (spawn protection).
            LeaveHiding(inside, spot);
        }

        // ---- Deaths and spawning ------------------------------------------------------------------------------------------

        void OnInvestigatorDied(LocalCharacter character, DamageType damage, GameCharacter source) {
            if (night == null || night.IsOver || damage == DamageType.Despawn) return;
            if (eliminated.Contains(character.Owner) || reviving.Contains(character.Owner)) return;
            hidden.Remove(character);
            DropCarried(character.Owner, character.transform.position, character.transform.eulerAngles.y);
            if (damage == DamageType.Water && !drowningCostsLife) {
                character.CanSpectate.Value = false;
                reviving.Add(character.Owner);
                return;
            }
            bool byPriest = monster != null && source == monster.GetComponent<GameCharacter>();
            if (byPriest) {
                SendScare(character.Owner, CH_ScareKind.Fatal);
                SpeakFromMonster(CH_Story.CatchLines[livesLost % CH_Story.CatchLines.Length], force: true);
                PlayerProgress.Modify<CH_SaveData>(character.Owner, save => save.timesCaught++);
            }
            livesLost++;
            character.CanSpectate.Value = false;
            PlayerData.TryGetPlayerData(character.Owner, out var player);
            int livesLeft = player != null ? Mathf.Max(0, player.lives.Value - 1) : 0;
            if (player != null) player.lives.Value = livesLeft;
            string account = AccountKey(character.Owner);
            if (account != null) accountLives[account] = livesLeft;
            Debug.Log($"[Drowned Choir] client {character.Owner.ClientId} lost a life ({damage}) at {NightSeconds}s; {livesLeft} left");
            if (livesLeft > 0) {
                reviving.Add(character.Owner);
                QueueEar("player_down", 4.5f);
            } else {
                LeaveBehind(character.Owner);
                QueueEar("player_out", 4.5f);
            }
            BroadcastState();
        }

        bool EveryoneOut() {
            if (PlayerData.Players.Count == 0) return false;
            foreach (var player in PlayerData.Players.Values)
                if (!eliminated.Contains(player.Owner)) return false;
            return true;
        }

        int CountLiving() {
            int living = 0;
            foreach (var character in LocalCharacter.Characters.Values) if (character != null && !character.IsDead) living++;
            return Mathf.Max(1, living);
        }

        void RebuildSpawnAnchors() {
            spawnAnchors.Clear();
            spawnAnchors.Add(map.arrival);
            foreach (Transform location in map.searchLocations) if (location != null) spawnAnchors.Add(location);
        }

        /// <summary>The team arrives together on the jetty; a recovered investigator wakes on the shore near a teammate, away from him.</summary>
        Vector3 SpawnPosition(NetworkConnection conn) {
            if (map == null) return FallbackSpawn;
            Vector3? monsterPosition = monster != null ? monster.transform.position : null;
            bool regroup = !caseStartSpawn && night != null && !night.IsOver;
            OrderSpawnAnchors(conn, regroup, monsterPosition);
            for (int pass = 0; pass < 2; pass++) {
                foreach (var (anchor, _) in spawnOrder) {
                    for (int attempt = 0; attempt < spawnCandidateAttempts; attempt++) {
                        Vector2 scatter = attempt == 0 && regroup ? Vector2.zero : UnityEngine.Random.insideUnitCircle * spawnScatterRadius;
                        Vector3 candidate = anchor.position + new Vector3(scatter.x, 0f, scatter.y);
                        if (TryResolveSpawn(candidate, pass == 0 ? monsterPosition : null, out Vector3 resolved)) return resolved;
                    }
                }
            }
            Debug.LogWarning("[Drowned Choir] every spawn candidate was rejected; using the jetty.");
            return map.arrival.position + Vector3.up * PlayerSpawnClearance;
        }

        void OrderSpawnAnchors(NetworkConnection conn, bool regroup, Vector3? monsterPosition) {
            spawnOrder.Clear();
            teammates.Clear();
            if (regroup)
                foreach (var pair in LocalCharacter.Characters)
                    if (pair.Key != conn && pair.Value != null && !pair.Value.IsDead) teammates.Add(pair.Value.transform.position);
            Vector3 home = map.arrival.position;
            foreach (Transform anchor in spawnAnchors) {
                float score;
                if (!regroup || teammates.Count == 0) score = anchor == map.arrival ? 0f : 50f + Vector3.Distance(anchor.position, home);
                else {
                    score = float.MaxValue;
                    foreach (Vector3 mate in teammates) score = Mathf.Min(score, Vector3.Distance(anchor.position, mate));
                }
                if (regroup && monsterPosition.HasValue && Vector3.Distance(anchor.position, monsterPosition.Value) < spawnMonsterSafeDistance) score += 10000f;
                spawnOrder.Add((anchor, score));
            }
            spawnOrder.Sort((a, b) => a.score.CompareTo(b.score));
        }

        bool TryResolveSpawn(Vector3 candidate, Vector3? avoid, out Vector3 spawnPosition) {
            spawnPosition = default;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SpawnNavSampleRadius, NavMesh.AllAreas)) return false;
            if (hit.position.y <= map.waterLevel + 0.1f) return false;
            if (avoid.HasValue && Vector3.Distance(hit.position, avoid.Value) < spawnMonsterSafeDistance) return false;
            foreach (var other in LocalCharacter.Characters.Values)
                if (other != null && !other.IsDead && (other.transform.position - hit.position).sqrMagnitude < 1.44f) return false;
            int mask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast", "Water");
            if (!Physics.Raycast(hit.position + Vector3.up * 0.6f, Vector3.down, out RaycastHit ground, 1.4f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (ground.normal.y < 0.8f || ground.point.y <= map.waterLevel + 0.1f) return false;
            for (int i = 0; i < 4; i++) {
                Vector3 offset = Quaternion.Euler(0, i * 90f, 0) * Vector3.forward * (SpawnCapsuleRadius + 0.2f);
                if (!Physics.Raycast(ground.point + offset + Vector3.up * 0.5f, Vector3.down, out RaycastHit edge, 1.1f, mask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(edge.point.y - ground.point.y) > 0.35f) return false;
            }
            Vector3 bottom = ground.point + Vector3.up * (SpawnCapsuleRadius + 0.08f);
            Vector3 top = ground.point + Vector3.up * (SpawnCapsuleHeight - SpawnCapsuleRadius);
            if (Physics.CheckCapsule(bottom, top, SpawnCapsuleRadius, mask, QueryTriggerInteraction.Ignore)) return false;
            spawnPosition = ground.point + Vector3.up * PlayerSpawnClearance;
            return true;
        }

        // ---- Layout -------------------------------------------------------------------------------------------------------

        /// <summary>Chooses where tonight's items lie: spread across the village and away from the jetty.</summary>
        CH_Night.Layout BuildLayout(int seed) {
            var random = new System.Random(seed);
            var pool = new List<Transform>();
            foreach (Transform location in map.searchLocations)
                if (location != null && Vector3.Distance(location.position, map.arrival.position) >= itemMinimumDistanceFromJetty) pool.Add(location);
            if (pool.Count < 6) pool = new List<Transform>(Array.FindAll(map.searchLocations, location => location != null));
            Shuffle(pool, random);
            var chosen = new List<Transform>();
            // Greedy spread: each next spot is the candidate furthest from those already chosen.
            chosen.Add(pool[0]);
            while (chosen.Count < 6 && chosen.Count < pool.Count) {
                Transform best = null;
                float bestScore = -1f;
                foreach (Transform candidate in pool) {
                    if (chosen.Contains(candidate)) continue;
                    float nearest = float.MaxValue;
                    foreach (Transform taken in chosen) nearest = Mathf.Min(nearest, Vector3.Distance(candidate.position, taken.position));
                    float score = nearest + (float)random.NextDouble() * 6f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                }
                chosen.Add(best);
            }
            Pose PoseOf(Transform t) => new(t.position, t.rotation);
            Transform musicBox = map.musicBoxSpots != null && map.musicBoxSpots.Length > 0
                ? map.musicBoxSpots[random.Next(map.musicBoxSpots.Length)] : pool[pool.Count - 1];
            return new CH_Night.Layout {
                boatPartSpots = new[] { PoseOf(chosen[0]), PoseOf(chosen[2]), PoseOf(chosen[4]) },
                relicSpots = new[] { PoseOf(chosen[1]), PoseOf(chosen[3]), PoseOf(chosen[5 % chosen.Count]) },
                musicBoxSpot = PoseOf(musicBox),
                bellSpot = PoseOf(map.altar),
                boatSlots = Array.ConvertAll(map.boatSlots, PoseOf),
                graves = Array.ConvertAll(map.graves, PoseOf),
                lilyMarker = PoseOf(map.lilyMarker)
            };
        }

        static void Shuffle<T>(IList<T> list, System.Random random) {
            for (int i = list.Count - 1; i > 0; i--) {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---- The priest's callbacks -----------------------------------------------------------------------------------------

        public void OnChaseStarted(LocalCharacter target) {
            if (night == null || !CH_Story.IsPlayable(night.Phase)) return;
            chaseMusicUntil = Time.time + ChaseMusicReleaseDelay;
            SetMusic(MusicSelection.HorrorChaseMusic);
            if (!firstChaseCalled) { firstChaseCalled = true; QueueEar("first_chase", 0f); }
            else if (!chaseTipCalled && monster.ChasesStarted >= 3) { chaseTipCalled = true; QueueEar("chase_tip", 0f); }
        }

        public void OnChaseLost() {
            if (monster != null && monster.ChasesEscaped % 2 == 1) SpeakFromMonster(CH_Story.LostLines[(monster.ChasesEscaped / 2) % CH_Story.LostLines.Length]);
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) PlayerProgress.Modify<CH_SaveData>(character.Owner, save => save.chasesEscaped++);
        }

        /// <summary>He is chasing someone he can see up close: now and then his face fills their screen and he falters.</summary>
        public void OnCloseEncounter(LocalCharacter target, float distance) {
            if (distance > warningDistance || distance < monsterTuning.strikeDistance + 0.5f || target == null) return;
            warnings.TryGetValue(target.Owner, out CH_ScareRation ration);
            if (!ration.TryTake(Time.time, warningsPerInvestigator, warningCooldown)) return;
            warnings[target.Owner] = ration;
            SendScare(target.Owner, CH_ScareKind.Warning);
            monster.Hold(warningHoldSeconds);
        }

        public void OnRelocated(Vector3 position) {
            Debug.Log($"[Drowned Choir] the priest rose from the water at {position}");
            if (UnityEngine.Random.value < 0.5f) Broadcast("rise_1", CH_VoiceMode.World, position + Vector3.up * 1.5f);
        }

        public void SpeakFromMonster(string line) => SpeakFromMonster(line, false);

        void SpeakFromMonster(string line, bool force) {
            if (map.voices == null || (!force && Time.time < monsterVoiceFreeAt)) return;
            monsterVoiceFreeAt = Time.time + map.voices.Seconds(line) + 1.5f;
            Broadcast(line, CH_VoiceMode.Monster, Vector3.zero);
        }

        // ---- Voice -------------------------------------------------------------------------------------------------------

        /// <summary>Queues a line in everyone's ear; it plays once the previous ear line has finished.</summary>
        void QueueEar(string line, float delay) => earQueue.Add((Time.time + delay, line));

        void FlushVoiceQueue() {
            if (earQueue.Count == 0 || Time.time < earFreeAt) return;
            for (int i = 0; i < earQueue.Count; i++) {
                if (earQueue[i].at > Time.time) continue;
                string line = earQueue[i].line;
                earQueue.RemoveAt(i);
                Broadcast(line, CH_VoiceMode.Ear, Vector3.zero);
                return;
            }
        }

        void Broadcast(string line, CH_VoiceMode mode, Vector3 position, float delay = 0f) {
            if (string.IsNullOrEmpty(line) || night == null) return;
            if (delay > 0f) { DelayedBroadcast(line, mode, position, delay, night.Seed).Forget(); return; }
            if (mode == CH_VoiceMode.Ear) earFreeAt = Time.time + (map.voices != null ? map.voices.Seconds(line) : 3f) + VoiceGap;
            InstanceFinder.ServerManager.Broadcast(new CH_VoiceBroadcast { seed = night.Seed, line = line, mode = mode, position = position });
        }

        async UniTaskVoid DelayedBroadcast(string line, CH_VoiceMode mode, Vector3 position, float delay, int seed) {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            if (night != null && night.Seed == seed) Broadcast(line, mode, position);
        }

        // ---- Scares and atmosphere -----------------------------------------------------------------------------------------

        void SendScare(NetworkConnection connection, CH_ScareKind kind) {
            if (connection == null || !connection.IsAuthenticated) return;
            InstanceFinder.ServerManager.Broadcast(connection, new CH_ScareBroadcast { seed = night.Seed, sequence = ++scareSequence, kind = kind });
            Debug.Log($"[Drowned Choir] {kind} scare for client {connection.ClientId} at {NightSeconds}s");
        }

        /// <summary>Now and then, away from any chase, someone hears a whisper and sees him where he cannot be.</summary>
        void UpdateApparitions() {
            if (!nightReleased || monster == null || monster.IsHunting) return;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (character == null || character.IsDead || IsHidden(character)) continue;
                if (!nextApparition.TryGetValue(character.Owner, out float due)) { nextApparition[character.Owner] = Time.time + RandomRange(apparitionInterval); continue; }
                if (Time.time < due) continue;
                nextApparition[character.Owner] = Time.time + RandomRange(apparitionInterval);
                // Not with him actually close: the real thing is scarier.
                if (Vector3.Distance(character.transform.position, monster.transform.position) < 25f) continue;
                SendScare(character.Owner, UnityEngine.Random.value < 0.55f ? CH_ScareKind.Apparition : CH_ScareKind.Watcher);
                return; // one per tick
            }
        }

        void UpdateAmbientScares() {
            if (!ambientScares || !nightReleased || Time.time < nextAmbientScare) return;
            if (monster != null && monster.IsChasing) { nextAmbientScare = Time.time + 10f; return; }
            nextAmbientScare = Time.time + RandomRange(ambientScareInterval);
            var kind = (CH_AmbientScare)UnityEngine.Random.Range(0, 4);
            if (!TryAmbientScarePosition(kind, out Vector3 position)) return;
            InstanceFinder.ServerManager.Broadcast(new CH_AmbientScareBroadcast {
                seed = night.Seed, kind = kind, position = position, seconds = kind == CH_AmbientScare.LightsOut ? lightsOutSeconds : 0f
            });
        }

        bool TryAmbientScarePosition(CH_AmbientScare kind, out Vector3 position) {
            position = map.altar.position;
            if (kind is CH_AmbientScare.ChapelBell or CH_AmbientScare.LightsOut) return true;
            teammates.Clear();
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) teammates.Add(character.transform.position);
            if (teammates.Count == 0) return false;
            Vector3 listener = teammates[UnityEngine.Random.Range(0, teammates.Count)];
            float bestScore = float.MinValue;
            foreach (Transform location in map.searchLocations) {
                if (location == null) continue;
                float nearest = float.MaxValue;
                foreach (Vector3 mate in teammates) nearest = Mathf.Min(nearest, Vector3.Distance(mate, location.position));
                float score = kind == CH_AmbientScare.DistantCry ? nearest
                    : -Mathf.Abs(Vector3.Distance(listener, location.position) - Mathf.Lerp(doorSlamDistance.x, doorSlamDistance.y, 0.5f))
                      - (nearest < doorSlamDistance.x ? 1000f : 0f);
                if (score <= bestScore) continue;
                bestScore = score;
                position = location.position;
            }
            return true;
        }

        void UpdateMusic() {
            if (monster != null && monster.IsHunting) chaseMusicUntil = Time.time + ChaseMusicReleaseDelay;
            bool urgent = night.Phase == CH_Phase.Boarding;
            SetMusic(urgent || Time.time <= chaseMusicUntil ? MusicSelection.HorrorChaseMusic : MusicSelection.HorrorAmbientMusic);
        }

        static void SetMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        static void Award(NetworkConnection connection, ulong xp, ulong credits, string reason) {
            if (xp == 0 && credits == 0) return;
            if (!LevelsServer.AwardPlayerXPAndGold(connection, xp, credits))
                Debug.LogWarning($"[Drowned Choir] could not award {reason} rewards to connection {connection.ClientId}.");
        }

        static float RandomRange(Vector2 range) => UnityEngine.Random.Range(range.x, Mathf.Max(range.x, range.y));
        static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);

        // ---- State -------------------------------------------------------------------------------------------------------

        void Accept(NetworkConnection connection, CH_Target target) =>
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult { seed = night.Seed, accepted = true, target = target, message = "" });

        void Reject(NetworkConnection connection, CH_Target target, string message) {
            InstanceFinder.ServerManager.Broadcast(connection, new CH_InteractionResult { seed = night.Seed, accepted = false, target = target, message = message });
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        void OnStateRequest(NetworkConnection connection, CH_StateRequest request, Channel channel) {
            if (night == null || !connection.IsAuthenticated) return;
            if (snapshotTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            snapshotTimes[connection] = Time.unscaledTime;
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        void BroadcastState() {
            if (night == null) return;
            nextBroadcast = Time.time + StateInterval;
            InstanceFinder.ServerManager.Broadcast(MakeState());
        }

        CH_StateBroadcast MakeState() {
            var items = new CH_ItemView[night.Items.Count];
            for (int i = 0; i < items.Length; i++) {
                CH_Night.Item item = night.Items[i];
                items[i] = new CH_ItemView {
                    id = item.Id, kind = item.Kind, state = item.State, position = item.Position, yaw = item.Yaw,
                    carrierId = item.State == CH_ItemState.Carried ? CarrierObjectId(item.Carrier) : -1,
                    objective = night.IsObjective(item),
                    singer = (sbyte)item.Singer,
                    title = item.Singer >= 0 ? $"{CH_Story.SingerName(item.Singer)}'s {CH_Story.ItemName(item.Kind).ToLowerInvariant()}" : CH_Story.ItemName(item.Kind)
                };
            }
            var slots = new CH_SlotView[night.Slots.Count];
            for (int i = 0; i < slots.Length; i++) {
                CH_Night.Slot slot = night.Slots[i];
                slots[i] = new CH_SlotView {
                    id = slot.Id, kind = slot.Kind, accepts = slot.Accepts, position = slot.Position, yaw = slot.Yaw,
                    filled = slot.Filled, open = night.IsOpen(slot), singer = (sbyte)slot.Singer, title = SlotTitle(slot)
                };
            }
            var hides = new CH_HideView[map.hidingSpots.Length];
            for (int i = 0; i < hides.Length; i++) {
                LocalCharacter occupant = map.hidingSpots[i] != null ? OccupantOf(map.hidingSpots[i]) : null;
                hides[i] = new CH_HideView { id = i, occupantId = occupant != null ? occupant.ObjectId : -1 };
            }
            Objective(out string objective, out string detail, out int progress, out int total);
            return new CH_StateBroadcast {
                seed = night.Seed, phase = night.Phase, phaseSeconds = Time.time - phaseStarted,
                objective = objective, detail = detail, items = items, slots = slots, hides = hides,
                progress = progress, progressTotal = total,
                countdown = night.Phase == CH_Phase.Boarding ? Mathf.Max(0, Mathf.CeilToInt(boardingEnds - Time.time)) : 0,
                monsterId = monster != null ? monster.GetComponent<GameCharacter>().ObjectId : -1,
                livesPerPlayer = livesPerPlayer, livesLost = livesLost, nightSeconds = NightSeconds,
                ending = night.Ending, escapes = escapes, nextNightSeconds = nextNightCountdown
            };
        }

        static string SlotTitle(CH_Night.Slot slot) => slot.Kind switch {
            CH_SlotKind.Grave => $"{CH_Story.SingerName(slot.Singer)}'s grave",
            CH_SlotKind.LilyMarker => "Lily's marker",
            CH_SlotKind.BoatBell => "The boat",
            _ => "The boat"
        };

        int CarrierObjectId(int carrierKey) {
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && character.Owner.ClientId == carrierKey) return character.ObjectId;
            return -1;
        }

        void Objective(out string objective, out string detail, out int progress, out int total) {
            progress = total = 0;
            detail = "";
            switch (night.Phase) {
                case CH_Phase.Stranded:
                    objective = "Repair the boat";
                    detail = "Find fuel, a toolbox and a lantern. Bring them to the boat.";
                    progress = night.PartsFixed; total = CH_Story.BoatParts.Length;
                    break;
                case CH_Phase.Choir:
                    objective = "Free the drowned choir";
                    detail = "Lay each singer's belonging on their grave by the chapel.";
                    progress = night.SingersFreed; total = CH_Story.SingersPerNight;
                    break;
                case CH_Phase.Sermon:
                    bool carried = night.Items.Exists(item => item.Kind == CH_ItemKind.Bell && item.State != CH_ItemState.Hidden && item.Touched);
                    objective = carried ? "Carry the bell to the boat" : "Take Crane's bell from the chapel altar";
                    detail = carried ? "He hears the bell wherever it goes. Pass it on if he gets close." : "Mara is in the chapel. Take his bell and he will let her go.";
                    break;
                case CH_Phase.Boarding:
                    objective = "GET TO THE BOAT";
                    detail = "Anyone left ashore stays with the choir.";
                    break;
                case CH_Phase.Complete:
                    objective = "You escaped Hollowmere";
                    break;
                case CH_Phase.Failed:
                    objective = "The choir sings on";
                    break;
                default:
                    objective = CH_Story.Title;
                    break;
            }
        }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            acceptingPlayers = false;
            if (monster != null && monster.GetComponent<GameCharacter>().IsSpawned) InstanceFinder.ServerManager.Despawn(monster.gameObject);
            monster = null;
            night = null;
            pendingScene = false;
            SetMusic(MusicSelection.GameMusic);
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
