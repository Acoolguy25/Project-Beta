using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server
{
    [Serializable]
    public enum WV_NpcType {
        Normal,
        /// <summary>The same soldier carrying a pistol: engages from a distance and gives ground.</summary>
        Gunner
    };
    [Serializable]
    public enum WV_ActiveGameState {
        Wave,
        AdvanceWave,
        FinishEnemiesOff,
        /// <summary>The flag is down. The round is over and only the epilogue is left to play.</summary>
        GameOver
    }
    [Serializable]
    public struct WV_NpcSpawnData {
        public WV_NpcType npcType;
        public int spawnCount;
    }
    [Serializable]
    public class WV_WaveData {
        public WV_NpcSpawnData[] spawnData;
        public int waveIntermission = 40;
    }
    public class WV_ServerRunner : ServerRunner
    {
        // The half-scale terrain's central playable ground is around Y=75. Keep
        // players clear of the terrain while their client Rigidbody initializes.
        private static readonly Vector3 SpawnCenter = new Vector3(750, 75, 750);
        private static readonly Vector3 FlagSpawnPosition = new Vector3(750f, 51.60288f, 750f);
        private const float SpawnRadius = 25f;
        [SerializeField]
        private StructureComponent[] _buildableStructures;
        [SerializeField]
        private WV_Flag _flagPrefab;
        [SerializeField]
        private WV_Economy _economyPrefab;
        [SerializeField]
        private int WaveNumber = -1;
        [SerializeField]
        private static Vector3[] NPCSpawnLocs = {
            new Vector3(34.47f, 52.07f, 752.17f),
            new Vector3(754.44f, 51.62f, 1458.77f),
            new Vector3(749.95f, 51.62f, 30.75f),
            new Vector3(1470.08f, 51.62f, 737.49f),
        };

        [SerializeField]
        private GameObject[] RobotNPC_Prefab;
        [SerializeField]
        private WV_WaveData[] WaveSpawnData;

        /// <summary>
        /// Debug multiplier on how long every barracks, hangar, helipad, and airfield takes to turn
        /// out what was queued. Read once in <see cref="Awake"/>, so this runner is the single place
        /// a tester changes production pace.
        /// </summary>
        [SerializeField]
        [Tooltip("Editor only. Multiplies every production queue timer: 1 is the authored build " +
                 "time, 0.1 builds ten times faster, 3 makes a tank a real commitment.")]
        private DebugFloat DebugBuildDuration = new();

        // --- Debug shortcuts for testing a base without playing a round up to it. Each one is honoured
        // in the Editor only, so a shipped server always plays the tuned game whatever is left ticked.
        [Header("Debug (Editor only)")]
        [SerializeField]
        [Tooltip("Editor only. Every purchase - structures, units, troops, research - is free, and " +
                 "dying costs nothing.")]
        private DebugBool DebugInfiniteMoney = new(false);

        [SerializeField]
        [Tooltip("Editor only. Structures finish construction and production queues finish each " +
                 "item almost immediately. Overrides DebugBuildDuration.")]
        private DebugBool DebugInstantBuild = new(false);

        [SerializeField]
        [Tooltip("Editor only. Research completes the moment it is started.")]
        private DebugBool DebugInstantResearch = new(false);

        [SerializeField]
        [Tooltip("Editor only. Every technology starts each round already researched, so locked " +
                 "structures and units are available from the opening whistle.")]
        private DebugBool DebugAllResearched = new(false);

        private Vector3 WaveSpawnLocation;
        private WV_Flag spawnedFlag;
        private WV_Economy spawnedEconomy;
        private WV_ServerEconomy serverEconomy;
        private WV_ServerArmy serverArmy;
        private WV_ServerTroops serverTroops;
        private bool flagDown;

        /// <summary>How long the defeat message is held before the next round begins.</summary>
        private const int GameOverSeconds = 8;

        [Header("Mode")]
        [SerializeField]
        [Tooltip("The rules alliances are decided by. Survival - every player allied against the " +
                 "waves - is the only mode.")]
        private WV_GameMode GameMode = WV_GameMode.Survival;

        public WV_ActiveGameState GameState;
        protected override void Awake() {
            base.Awake();
            // Alliances come from the mode: which side commanders and waves are on, and who may hurt
            // whom. Published before anything spawns so no entity starts life on an unknown side.
            WV_Alliances.Mode = GameMode;
            SharedGlobalEvents.TeamEnemies = WV_Alliances.BuildEnemyTable();
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnLocationFunction;

            foreach (StructureComponent structure in _buildableStructures) {
                if (structure != null && structure.NetworkObject != null)
                    SharedGlobalEvents.Instance.Builds.Add(structure.NetworkObject.PrefabId);
            }

            // The build economy and the unit roster are server-only behaviours, so they are attached
            // here rather than serialized on the runner prefab, which a client build also loads.
            serverEconomy = gameObject.AddComponent<WV_ServerEconomy>();
            serverArmy = gameObject.AddComponent<WV_ServerArmy>();
            serverTroops = gameObject.AddComponent<WV_ServerTroops>();
            serverTroops.Initialize(GetNpcPrefab(WV_NpcType.Normal));

            // Applied here rather than read per timer so that a building which starts its queue
            // mid-round uses the same pace as one that started at the opening whistle. The economy
            // and research ledgers are respawned every round and pick their switches up as they open.
            ApplyDebugSettings();
            WV_ServerCommand.Register();
        }

        /// <summary>
        /// Pushes the runner's debug switches into the systems that honour them. Each system holds
        /// its switch statically, the way <see cref="WV_ProductionBuilding.BuildDurationMultiplier"/>
        /// already did, so this runner stays the single place a tester flips them.
        /// </summary>
        private void ApplyDebugSettings() {
            bool instantBuild = DebugInstantBuild.Value;
            WV_ProductionBuilding.BuildDurationMultiplier = instantBuild ? 0f : DebugBuildDuration.Value;
            WV_Constructable.ConstructionDurationMultiplier = instantBuild ? 0f : 1f;
            WV_Economy.DebugInfiniteFunds = DebugInfiniteMoney.Value;
            WV_Research.InstantResearch = DebugInstantResearch.Value;
            WV_Research.StartFullyResearched = DebugAllResearched.Value;

            if (instantBuild || DebugInfiniteMoney.Value || DebugInstantResearch.Value || DebugAllResearched.Value)
                Debug.LogWarning(
                    $"War Valley debug: infinite money={DebugInfiniteMoney.Value}, instant build={instantBuild}, " +
                    $"instant research={DebugInstantResearch.Value}, all researched={DebugAllResearched.Value}.", this);
        }
        bool CanSpawnFunction(NetworkConnection conn) {
            //PlayerData.GetPlayerData(conn)
            return true;
        }
        Vector3 SpawnLocationFunction(NetworkConnection conn) {
            return ServerPathfinding.GetRandomPositionOnCircle(SpawnCenter, SpawnRadius);
        }
        protected override bool UpdateInGameBar(int durationLeft, bool interrupted) {
            // Account for wave index being zero-based
            switch (GameState) {
                case WV_ActiveGameState.Wave:
                    SetTopMessage($"Wave {WaveNumber + 1}");
                    break;
                case WV_ActiveGameState.AdvanceWave:
                    SetTopMessage($"Wave {WaveNumber + 2} will start in {durationLeft} seconds");
                    break;
                case WV_ActiveGameState.FinishEnemiesOff:
                    int npcs = GameCharacter.TeamCount(WV_Alliances.GetWaveSide());
                    SetTopMessage($"Finish off remaining enemies ({npcs} left)");
                    return npcs > 0;
                case WV_ActiveGameState.GameOver:
                    // Returning false ends whichever countdown is running, so the round stops the
                    // moment the flag falls instead of playing out the rest of the wave timer.
                    SetTopMessage("The flag has fallen");
                    return false;
            }
            return true;
        }
        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            // The mode decides the side a commander fights for; ownership is carried by the display
            // half of the existing team setting rather than by a second parallel colour field. The
            // player list already tints names by display team, so a base and its owner's name match.
            int clientId = playerData.Owner != null && playerData.Owner.IsValid
                ? playerData.Owner.ClientId
                : WV_Owned.NoOwner;
            playerData.SetPlayerTeam(
                new TeamConfig(WV_Alliances.GetCommanderSide(clientId), WV_Rules.GetCommanderColor(clientId)));
            playerData.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
        }
        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            //character.SetScale(UnityEngine.Random.Range(1f, 3f) * 5 * Vector3.one);

            // A commander who dies pays for it out of their war chest. Each respawn is a new
            // character, so this subscription lives and dies with the one it was made for.
            int clientId = character.Owner != null && character.Owner.IsValid
                ? character.Owner.ClientId
                : WV_Owned.NoOwner;
            void HandleCommanderDied(DamageType source, IEntity attacker) {
                character.OnDied -= HandleCommanderDied;
                // Being cleared away by a round reset or a despawn is housekeeping, not a death
                // the commander could have avoided.
                if (source is DamageType.Reset or DamageType.Despawn)
                    return;
                serverEconomy.ApplyDeathPenalty(clientId);
            }
            character.OnDied += HandleCommanderDied;
        }
        protected async UniTask<bool> StartTimerCountdown(int duration, CancellationToken token) {
            return await GameTimerCountdown(duration, token);
        }
        protected Vector3 GetSpawnLocation() {
            Vector3 pos = NPCSpawnLocs[UnityEngine.Random.Range(0, NPCSpawnLocs.Length)];
            return pos;
        }
        /// <summary>
        /// The body a wave NPC is built from. A gunner is the same soldier holding a different tool,
        /// so a type with no prefab of its own falls back to the first authored one rather than
        /// requiring a parallel prefab per loadout.
        /// </summary>
        protected GameObject GetNpcPrefab(WV_NpcType npcType) {
            if (RobotNPC_Prefab == null || RobotNPC_Prefab.Length == 0) {
                Debug.LogError($"{nameof(WV_ServerRunner)} has no NPC prefabs; no wave can spawn.", this);
                return null;
            }

            int index = (int)npcType;
            GameObject prefab = index >= 0 && index < RobotNPC_Prefab.Length ? RobotNPC_Prefab[index] : null;
            return prefab != null ? prefab : RobotNPC_Prefab[0];
        }

        /// <summary>The weapon each wave type fights with.</summary>
        protected static WV_TroopKind GetNpcLoadout(WV_NpcType npcType) => npcType switch {
            WV_NpcType.Gunner => WV_TroopKind.Gunner,
            _ => WV_TroopKind.Knife
        };

        protected void SpawnNpc(WV_NpcType npcType) {
            GameObject prefab = GetNpcPrefab(npcType);
            if (prefab == null)
                return;

            Vector3 spawnLocation = ServerPathfinding.GetRandomPositionOnCircle(WaveSpawnLocation, SpawnRadius);
            LocalNPC npc = ServerNPC.SpawnNPC(prefab, location: spawnLocation);
            GameCharacter character = npc.GetComponent<GameCharacter>();
            character.SetTeam(new TeamConfig(WV_Alliances.GetWaveSide()));
            // The weapon half is attached before the brain so the loadout is settled before either
            // component's first frame.
            WV_NpcCombat.Attach(npc.gameObject, GetNpcLoadout(npcType));
            npc.gameObject.AddComponent<WV_NPC>();
            // A body that never leaves keeps counting toward the wave that is supposed to be over.
            WV_Corpses.DespawnWhenDead(this, character);
            character.OnDied += (DamageType source, IEntity sourceEntity) =>
            {
                if (GameState == WV_ActiveGameState.FinishEnemiesOff) {
                    RefreshInGameBar();
                }
            };
        }
        protected void SpawnNpcs(WV_NpcType npcType, int count) {
            for (int i = 0; i < count; i++) {
                SpawnNpc(npcType);
            }
        }
        protected void SpawnWave(WV_NpcSpawnData[] spawnData) {
            foreach (WV_NpcSpawnData npcData in spawnData) {
                SpawnNpcs(npcData.npcType, npcData.spawnCount);
            }
        }
        protected override async UniTask StartAsync(CancellationToken token) {
            await base.StartAsync(token);

            // Enabled before the economy opens accounts so the column exists by the time balances
            // are first published, and so players joining later are given the matching row.
            SetLeaderboardEnabled(WV_Rules.CoinsLeaderboard, true);

            SpawnEconomy();
            SpawnFlag();

            SharedGlobalEvents.Instance.CanBuild.Value = true;
            await Intermission(10, token);
            SetGlobalInvul(false);
            for (WaveNumber = 0; WaveNumber < WaveSpawnData.Length && !flagDown; WaveNumber++) {
                // Current Wave Logic
                WV_WaveData WaveData = WaveSpawnData[WaveNumber];
                WaveSpawnLocation = GetSpawnLocation();
                SpawnWave(WaveData.spawnData);

                // Wave Advance Logic
                GameState = WV_ActiveGameState.Wave;
                await StartTimerCountdown(10, token);
                int waveAdvanceSec = Math.Max(0, WaveData.waveIntermission - 10);
                if (waveAdvanceSec > 0 && !flagDown) {
                    GameState = WV_ActiveGameState.AdvanceWave;
                    await StartTimerCountdown(waveAdvanceSec, token);
                }
            }

            if (!flagDown) {
                GameState = WV_ActiveGameState.FinishEnemiesOff;
                await StartTimerCountdown(-1, token);
            }

            if (flagDown)
                await AnnounceFlagLost(token);
        }

        /// <summary>
        /// Plays out the end of a lost round. Returning from here lets the runner's own loop restart
        /// the match, the same way finishing the last wave does.
        /// </summary>
        private async UniTask AnnounceFlagLost(CancellationToken token) {
            GameState = WV_ActiveGameState.GameOver;
            SharedGlobalEvents.Instance.CanBuild.Value = false;
            await TimerCountdown("The flag has fallen - War Valley is lost ({0})", GameOverSeconds, token);
        }

        /// <summary>
        /// The objective is the round. Losing it ends the match immediately rather than leaving
        /// players to fight out a wave timer over a flag that is no longer there.
        /// </summary>
        private void HandleFlagDied(DamageType source, IEntity attacker) {
            if (flagDown)
                return;

            flagDown = true;
            GameState = WV_ActiveGameState.GameOver;
            // Interrupts whichever countdown is running so the round does not sit on a stale wave
            // message until the next tick.
            RefreshInGameBar();
        }
        protected override void Stop() {
            base.Stop();
            SharedGlobalEvents.Instance.CanBuild.Value = false;
        }
        protected override void Restart() {
            // Reset before base.Restart starts the next asynchronous round. Otherwise
            // its first countdown can publish with the prior loop's wave number, then
            // this assignment changes it for the following one-second update.
            WaveNumber = -1;
            flagDown = false;
            base.Restart();
        }

        private void SpawnFlag() {
            DespawnFlag();
            if (_flagPrefab == null) {
                Debug.LogError($"{nameof(WV_ServerRunner)} is missing its flag prefab.");
                return;
            }

            GameObject clone = Instantiate(_flagPrefab.gameObject, FlagSpawnPosition, Quaternion.identity);
            MoveToStartScene(clone);
            spawnedFlag = clone.GetComponent<WV_Flag>();
            InstanceFinder.ServerManager.Spawn(clone);
            spawnedFlag.OnDied += HandleFlagDied;
        }

        private void DespawnFlag() {
            if (spawnedFlag == null)
                return;

            spawnedFlag.OnDied -= HandleFlagDied;
            if (InstanceFinder.IsServerStarted && spawnedFlag.IsSpawned)
                spawnedFlag.Despawn();
            else
                Destroy(spawnedFlag.gameObject);
            spawnedFlag = null;
        }

        protected override void Reset() {
            DespawnFlag();
            DespawnEconomy();
            base.Reset();
        }

        protected override void OnDestroy() {
            WV_ServerCommand.Unregister();
            base.OnDestroy();
        }

        private void SpawnEconomy() {
            DespawnEconomy();
            if (_economyPrefab == null) {
                Debug.LogError($"{nameof(WV_ServerRunner)} is missing its economy prefab; nobody can build.");
                return;
            }

            GameObject clone = Instantiate(_economyPrefab.gameObject);
            MoveToStartScene(clone);
            spawnedEconomy = clone.GetComponent<WV_Economy>();
            InstanceFinder.ServerManager.Spawn(clone);

            // Accounts are opened only once the ledger exists, so players who joined during the
            // lobby get their starting funds instead of an empty wallet.
            serverEconomy.ResetAccounts();
        }

        private void DespawnEconomy() {
            if (spawnedEconomy == null)
                return;

            if (InstanceFinder.IsServerStarted && spawnedEconomy.IsSpawned)
                spawnedEconomy.Despawn();
            else
                Destroy(spawnedEconomy.gameObject);
            spawnedEconomy = null;
        }

        private static void MoveToStartScene(GameObject clone) {
            Scene startScene = SceneManager.GetSceneByName("war_valley_start");
            if (startScene.IsValid() && startScene.isLoaded)
                SceneManager.MoveGameObjectToScene(clone, startScene);
        }
    }
}
