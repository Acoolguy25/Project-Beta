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
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server
{
    [Serializable]
    public enum WV_NpcType {
        Normal
    };
    [Serializable]
    public enum WV_ActiveGameState {
        Wave,
        AdvanceWave,
        FinishEnemiesOff
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
        private static readonly Vector3 SpawnCenter = new Vector3(750, 51, 750);
        private static readonly Vector3 FlagSpawnPosition = new Vector3(750f, 51.60288f, 750f);
        private const float SpawnRadius = 25f;
        [SerializeField]
        private StructureComponent[] _buildableStructures;
        [SerializeField]
        private WV_Flag _flagPrefab;
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

        private Vector3 WaveSpawnLocation;
        private WV_Flag spawnedFlag;

        public WV_ActiveGameState GameState;
        public static Action UpdateGameBarEvent;
        protected override void Awake() {
            base.Awake();
            SharedGlobalEvents.TeamEnemies = new()
            {
                [TeamColor.Red] = new() { TeamColor.Blue },
                [TeamColor.Blue] = new() { TeamColor.Red }
            };
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnLocationFunction;

            foreach (StructureComponent structure in _buildableStructures) {
                if (structure != null && structure.NetworkObject != null)
                    SharedGlobalEvents.Instance.Builds.Add(structure.NetworkObject.PrefabId);
            }
        }
        bool CanSpawnFunction(NetworkConnection conn) {
            //PlayerData.GetPlayerData(conn)
            return true;
        }
        Vector3 SpawnLocationFunction(NetworkConnection conn) {
            return ServerPathfinding.GetRandomPositionOnCircle(SpawnCenter, SpawnRadius);
        }
        bool UpdateInGameBar(int durationLeft, bool interrupted) {
            // Account for wave index being zero-based
            switch (GameState) {
                case WV_ActiveGameState.Wave:
                    SetTopMessage($"Wave {WaveNumber + 1}");
                    break;
                case WV_ActiveGameState.AdvanceWave:
                    SetTopMessage($"Wave {WaveNumber + 2} will start in {durationLeft} seconds");
                    break;
                case WV_ActiveGameState.FinishEnemiesOff:
                    int npcs = GameCharacter.TeamCount(TeamColor.Red);
                    SetTopMessage($"Finish off remaining enemies ({npcs} left)");
                    return npcs > 0;
            }
            return true;
        }
        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            playerData.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
            playerData.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
        }
        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            //character.SetScale(UnityEngine.Random.Range(1f, 3f) * 5 * Vector3.one);
        }
        protected async UniTask<bool> StartTimerCountdown(int duration, CancellationToken token) {
            return await CustomTimerCountdown(
                duration,
                UpdateInGameBar,
                interrupt => UpdateGameBarEvent += interrupt,
                interrupt => UpdateGameBarEvent -= interrupt,
                token);
        }
        protected Vector3 GetSpawnLocation() {
            Vector3 pos = NPCSpawnLocs[UnityEngine.Random.Range(0, NPCSpawnLocs.Length)];
            return pos;
        }
        protected void SpawnNpc(WV_NpcType npcType) {
            Vector3 spawnLocation = ServerPathfinding.GetRandomPositionOnCircle(WaveSpawnLocation, SpawnRadius);
            LocalNPC npc = ServerNPC.SpawnNPC(RobotNPC_Prefab[((int)npcType)], location: spawnLocation);
            GameCharacter character = npc.GetComponent<GameCharacter>();
            character.SetTeam(new TeamConfig(TeamColor.Red));
            npc.gameObject.AddComponent<WV_NPC>();
            character.OnDied += (DamageType source, IEntity sourceEntity) =>
            {
                if (GameState == WV_ActiveGameState.FinishEnemiesOff) {
                    UpdateGameBarEvent.Invoke();
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

            SpawnFlag();

            SharedGlobalEvents.Instance.CanBuild.Value = true;
            await Intermission(10, token);
            SetGlobalInvul(false);
            for (WaveNumber = 0; WaveNumber < WaveSpawnData.Length; WaveNumber++) {
                // Current Wave Logic
                WV_WaveData WaveData = WaveSpawnData[WaveNumber];
                WaveSpawnLocation = GetSpawnLocation();
                SpawnWave(WaveData.spawnData);

                // Wave Advance Logic
                GameState = WV_ActiveGameState.Wave;
                await StartTimerCountdown(10, token);
                int waveAdvanceSec = Math.Max(0, WaveData.waveIntermission - 10);
                if (waveAdvanceSec > 0) {
                    GameState = WV_ActiveGameState.AdvanceWave;
                    await StartTimerCountdown(waveAdvanceSec, token);
                }
            }
            GameState = WV_ActiveGameState.FinishEnemiesOff;
            await StartTimerCountdown(-1, token);
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
            base.Restart();
        }

        private void SpawnFlag() {
            DespawnFlag();
            if (_flagPrefab == null) {
                Debug.LogError($"{nameof(WV_ServerRunner)} is missing its flag prefab.");
                return;
            }

            GameObject clone = Instantiate(_flagPrefab.gameObject, FlagSpawnPosition, Quaternion.identity);
            Scene startScene = SceneManager.GetSceneByName("war_valley_start");
            if (startScene.IsValid() && startScene.isLoaded)
                SceneManager.MoveGameObjectToScene(clone, startScene);
            spawnedFlag = clone.GetComponent<WV_Flag>();
            InstanceFinder.ServerManager.Spawn(clone);
        }

        private void DespawnFlag() {
            if (spawnedFlag == null)
                return;

            if (InstanceFinder.IsServerStarted && spawnedFlag.IsSpawned)
                spawnedFlag.Despawn();
            else
                Destroy(spawnedFlag.gameObject);
            spawnedFlag = null;
        }

        protected override void Reset() {
            DespawnFlag();
            base.Reset();
        }
    }
}
