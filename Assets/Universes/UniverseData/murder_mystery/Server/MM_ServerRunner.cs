using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Items.Coin;
using RyanAssets.Levels.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Universes.UniverseData.murder_mystery;
using Universes.UniverseData.murder_mystery.Server;

namespace Universes.murder_mystery.Server {
    enum MM_Mode {
        NPCsVsPlayers,
        Classic,
        Infection,
        Unarmed, // Deprecated
    }

    /// <summary>
    /// Murder mystery's server: the mode vote, spawning players and NPCs with their roles and weapons, and the
    /// rounds. Classic (MM2 style: murderer, sheriff, innocents, coins, the dropped gun) is run by
    /// <see cref="MM_ClassicRound"/>; the older NPCs vs Players and Infection modes run here on a timer.
    /// </summary>
    public class MM_ServerRunner : ServerRunner {
        // Infection needs a broad, team-neutral search radius so infected NPCs actively
        // hunt both innocents and sheriffs without restoring the sheriff-only override.
        private const float InfectionAttackDetectionMultiplier = 6f;
        // NPC killers in NPCs vs Players, and Classic murderers once no player is left, search this much wider.
        private const float HuntAttackDetectionMultiplier = 3.5f;
        // NPC count of the older modes, before the NPCSpawnMultiplier command.
        private const int OtherModesNpcCount = 30;
        const float StateInterval = 0.5f;

        [SerializeField]
        GameObject RobotNPC_Prefab;
        [SerializeField]
        DebugBool DebugSingleNpc, DebugMotionlessNpc;
        [SerializeField]
        DebugBool DebugNoIntermission;
        [SerializeField]
        DebugBool FallTest;
        [SerializeField]
        MM_Mode mode;
        [Tooltip("Pacing and balance of Classic rounds: phase lengths, role odds, the knife, the gun, coins, NPC behaviour and rewards.")]
        [SerializeField]
        MM_ClassicTuning classicTuning = new();
        [Tooltip("Round length of the NPCs vs Players and Infection modes.")]
        [SerializeField, Min(10)]
        int otherModesRoundSeconds = 90;
        [Tooltip("Editor only: deal the first player this Classic role every round (None = the normal draw), to play-test a role on your own.")]
        [SerializeField]
        MM_Role debugFirstPlayerRole = MM_Role.None;
        [Header("Spawns")]
        [Tooltip("Players spawn within this distance of a lamp they can see whenever one of the sampled points allows it, " +
                 "so nobody starts a round in a dark corner (metres). 0 = anywhere on the NavMesh.")]
        [SerializeField, Min(0f)]
        float litSpawnDistance = 16f;
        [Tooltip("Room the orbit camera needs round a spawn point: a point counts as open when nothing is closer than " +
                 "this in any of the horizontal directions tested at Spawn Clearance Height (metres). 0 = no check.")]
        [SerializeField, Min(0f)]
        float spawnClearance = 7f;
        [Tooltip("Height above the floor of the clearance test: about where the camera sits (metres).")]
        [SerializeField, Min(0f)]
        float spawnClearanceHeight = 4f;
        [Tooltip("Random NavMesh points tried per spawn while looking for a lit, open one.")]
        [SerializeField, Min(1)]
        int spawnSamples = 24;

        public static float SpawnMultiplier;
        public static bool ForceEndGame;
        static MM_ServerRunner current;

        /// <summary>The running server's Classic tuning (null when no murder mystery server is up), for commands.</summary>
        public static MM_ClassicTuning ClassicTuning => current != null ? current.classicTuning : null;
        bool gameInProgress;
        readonly HashSet<GameCharacter> pendingInfectionRevives = new();
        string[] alienNames;
        int startNPCs, gameDurationLeft;
        // Other modes: the team each NPC / player starts on.
        List<TeamColor> npcTeams = new(), playerTeams = new();
        // Classic: the dealt roles, players first then NPCs (MM_Roles.DealClassic).
        MM_Role[] classicRoles = Array.Empty<MM_Role>();
        MM_ClassicRound classic;
        int roundNumber;
        // The map's lamps (point and spot lights), found once on the first spawn.
        List<Vector3> lampPositions;

        protected override void Awake() {
            base.Awake();
            current = this;
            classic = new MM_ClassicRound(this, classicTuning);
            classic.EndgameStarted += OnClassicEndgame;
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.RegisterBroadcast<MM_ThrowKnifeRequest>(classic.OnThrowRequest, true);
                InstanceFinder.ServerManager.RegisterBroadcast<MM_SmokeRequest>(classic.OnSmokeRequest, true);
                InstanceFinder.ServerManager.RegisterBroadcast<MM_PerkRequest>(MM_PerkPicker.OnPerkRequest, true);
            } else {
                Debug.LogError("[MM] No ServerManager when the runner woke: knife throws and smoke bombs from players will be ignored.", this);
            }
            CoinShared.CanCollect = classic.CanCollectCoin;
            CoinShared.Collected += classic.OnCoinCollected;
            BroadcastStateLoop(destroyCancellationToken).Forget(exception => Debug.LogException(exception, this));
            SharedGlobalEvents.TeamEnemies = new() {
                [TeamColor.Red] = new() { TeamColor.Blue, TeamColor.Green },
                [TeamColor.Blue] = new() { TeamColor.Red },
                [TeamColor.Green] = new() // can't kill anyone!
            };
            ForceEndGame = false;
            SpawnMultiplier = 1f;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnLocationFunction;

            base.SetTeamKillEnabled(true);
            float npcSpeed = DebugMotionlessNpc.Value ? 0f : 1f;
            LocalNPC.FleeSpeedMultiplier = npcSpeed;
            LocalNPC.WalkSpeedMultiplier = npcSpeed;
            LocalNPC.AttackSpeedMultiplier = npcSpeed;
        }

        void Update() => classic.Tick(Time.deltaTime);

        bool CanSpawnFunction(NetworkConnection player) {
            if (!gameInProgress)
                return true;
            // Classic and Infection deal everyone in at the start; late joiners watch until the next round.
            return mode is not (MM_Mode.Classic or MM_Mode.Infection);
        }

        Vector3 SpawnLocationFunction(NetworkConnection player) {
            return DebugSingleNpc.Value ? new Vector3(1120.56995f, 12.12100029f, 1008.34003f) : LitSpawnPoint();
        }

        /// <summary>A random NavMesh point, preferring one near a lamp: the first frame of a round should never be black.</summary>
        // Horizontal directions tested for camera room round a spawn point.
        const int ClearanceRays = 8;
        // A lamp is seen from a point this high above the floor (chest height on the 5.6 m robots).
        const float LampSightHeight = 3f;
        readonly RaycastHit[] spawnHits = new RaycastHit[16];

        /// <summary>
        /// A NavMesh point for a player's spawn: near a lamp that lights it (nothing in between) and with room
        /// round it for the orbit camera, so the role card never shows over a dark corner or the inside of a
        /// wall. Tries <see cref="spawnSamples"/> points and keeps the first that passes both tests, else the
        /// one that came closest (open beats lit, since a camera inside a wall shows nothing at all).
        /// </summary>
        Vector3 LitSpawnPoint() {
            lampPositions ??= FindLamps();
            Vector3 best = Vector3.zero;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < spawnSamples; i++) {
                Vector3 candidate = ServerPathfinding.GetRandomPosition();
                float room = spawnClearance > 0f ? Mathf.Min(Clearance(candidate), spawnClearance) / spawnClearance : 1f;
                float light = litSpawnDistance > 0f && lampPositions.Count > 0 ? (LitByLamp(candidate) ? 1f : 0f) : 1f;
                float score = room * 2f + light;
                if (score > bestScore) {
                    best = candidate;
                    bestScore = score;
                }
                if (score >= 3f)
                    break;
            }
            return best;
        }

        /// <summary>True when a lamp within <see cref="litSpawnDistance"/> has a clear line to the point.</summary>
        bool LitByLamp(Vector3 point) {
            Vector3 eye = point + Vector3.up * LampSightHeight;
            float range = litSpawnDistance * litSpawnDistance;
            foreach (Vector3 lamp in lampPositions)
                if ((lamp - eye).sqrMagnitude <= range && !Physics.Linecast(lamp, eye, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return true;
            return false;
        }

        /// <summary>The shortest distance to a wall round the point at camera height; characters do not count.</summary>
        float Clearance(Vector3 point) {
            Vector3 origin = point + Vector3.up * spawnClearanceHeight;
            float shortest = spawnClearance;
            for (int i = 0; i < ClearanceRays; i++) {
                Vector3 direction = Quaternion.Euler(0f, i * 360f / ClearanceRays, 0f) * Vector3.forward;
                int count = Physics.RaycastNonAlloc(origin, direction, spawnHits, spawnClearance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                    if (spawnHits[h].distance < shortest && spawnHits[h].collider.GetComponentInParent<GameCharacter>() == null)
                        shortest = spawnHits[h].distance;
            }
            return shortest;
        }

        static List<Vector3> FindLamps() {
            List<Vector3> lamps = new();
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type is LightType.Point or LightType.Spot)
                    lamps.Add(light.transform.position);
            if (lamps.Count == 0)
                Debug.LogWarning("[MM] No lamps found in the map; players spawn anywhere on the NavMesh.");
            return lamps;
        }

        protected void OnSetPlayerTeam(PlayerData player, TeamConfig teamConfig) {
            switch (teamConfig.realTeam) {
                case TeamColor.Red: // Murderer
                    player.staminaMax.Value = 165f;
                    player.staminaRegen.Value = 25f;
                    player.staminaCooldown.Value = 0.6f;
                    break;
                default:
                    player.staminaMax.Value = 100f;
                    player.staminaRegen.Value = 15f;
                    player.staminaCooldown.Value = 0.8f;
                    break;
            }
            ApplyPlayerSpeed(player, 1f);
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            LoadProgress(player).Forget(exception => Debug.LogException(exception, this));
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                case MM_Mode.Unarmed:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                    break;
                case MM_Mode.Classic:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.White, TeamColor.White));
                    break;
                case MM_Mode.Infection:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
                    break;
            }
            player.team.OnChange += (oldTeam, newTeam, _) => OnSetPlayerTeam(player, newTeam);
            OnSetPlayerTeam(player, player.team.Value);
        }

        /// <summary>Loads the player's saves; someone joining during the reveal then gets this round's results card.</summary>
        async UniTask LoadProgress(PlayerData player) {
            await PlayerProgress.Load<MM_SaveData>(player, MM_SaveData.Universe);
            MM_PerkPicker.Send(player);
            if (player != null && mode == MM_Mode.Classic)
                classic.SendResultsToLateJoiner(player);
        }

        void PreparePlayerForActiveRound(PlayerData player) {
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                case MM_Mode.Unarmed:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                    break;
                case MM_Mode.Infection:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
                    break;
                // Classic roles are dealt once in SpawnPlayers; late joins cannot spawn mid-round.
            }
            ApplyPlayerSpeed(player, 1f);
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            character.Init(100);
            character.OnDied += (source, killer) => SharedOnDied(character, source, killer);
            if (!gameInProgress)
                return;

            // PlayerData.OnPlayerAdded and ServerPlayerCharacter.PlayerAdded are
            // independent subscribers, so their invocation order is not safe to
            // rely on. Reapply the active-round configuration after the character
            // exists and immediately before assigning its tools.
            PlayerData player = PlayerData.GetPlayerData(character.Owner);
            PreparePlayerForActiveRound(player);
            RandomizeCharacterName(character);
            SpawnOtherModeTools(player, character.NetworkObject);
        }

        void SharedOnDied(GameCharacter character, DamageType source, IEntity killer) {
            GameCharacter killerCharacter = killer as GameCharacter;
            if (gameInProgress && killerCharacter != null && source != DamageType.Command)
                BroadcastKill(character, source, killerCharacter);
            switch (mode) {
                case MM_Mode.Classic:
                    if (gameInProgress)
                        classic.OnCharacterDied(character, source, killerCharacter);
                    character.SetRealColor(TeamColor.White);
                    break;
                case MM_Mode.Infection:
                    if (gameInProgress && serverRunning && killerCharacter != null
                        && killerCharacter.GetTeam().realTeam == TeamColor.Red
                        && character.GetTeam().realTeam != TeamColor.Red) {
                        // Capture this before the delayed revive. The owner-authoritative
                        // root can continue changing while the character is ragdolled.
                        pendingInfectionRevives.Add(character);
                        character.SetTeam(new TeamConfig(TeamColor.Red));
                        ReviveAsInfected(character, character.transform.position, serverRunnerCTS.Token)
                            .Forget(exception => Debug.LogException(exception, this));
                    } else {
                        character.SetRealColor(TeamColor.White);
                    }
                    if (killerCharacter != null && killerCharacter.Owner.IsValid)
                        LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 1, 1);
                    break;
                default:
                    if (killerCharacter != null && killerCharacter.Owner.IsValid)
                        LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 1, 1);
                    break;
            }
            RefreshInGameBar();
        }

        async UniTask ReviveAsInfected(GameCharacter character, Vector3 deathPosition, CancellationToken token) {
            bool revived = false;
            try {
                // HealthComponent sends the death RPC after its server-side OnDied event.
                // Wait one frame so observers process death before the revive RPC, rather
                // than receiving the two lifecycle notifications in reverse order.
                if (await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow())
                    return;
                if (await UniTask.WaitForSeconds(3f, cancellationToken: token).SuppressCancellationThrow())
                    return;
                if (character == null || !character.IsDead || mode != MM_Mode.Infection)
                    return;

                if (character.Owner.IsValid)
                    ServerTool.Instance.ClearTools(character);
                else
                    ServerTool.Instance.DespawnTools(character.NetworkObject);

                if (character is LocalCharacter localCharacter) {
                    if (!localCharacter.ReviveAtPosition(75, deathPosition))
                        return;
                } else {
                    character.Revive(75);
                }
                revived = true;
                character.GetComponent<RobotColor>().ApplyColor(TeamColor.Green);

                if (character.TryGetComponent(out MM_LocalNPC mmLocalNPC)) {
                    LocalNPC npc = character.GetComponent<LocalNPC>();
                    npc.AllowAttackTargetOverrides = false;
                    npc.AttackDetectionRadius *= InfectionAttackDetectionMultiplier;
                    mmLocalNPC.InitializeAttackState();
                } else
                    ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
            } finally {
                pendingInfectionRevives.Remove(character);
                if (revived)
                    RefreshInGameBar();
            }
        }

        /// <summary>
        /// Tells the players within earshot of a kill (plus the victim and the killer) where it happened, so
        /// their clients can play the kill sting. Nobody else learns of it.
        /// </summary>
        void BroadcastKill(GameCharacter victim, DamageType source, GameCharacter killer) {
            if (InstanceFinder.ServerManager == null)
                return;
            Vector3 position = victim.transform.position;
            float audibleSqr = classicTuning.killAudibleDistance * classicTuning.killAudibleDistance;
            foreach (PlayerData player in PlayerData.Players.Values) {
                if (player == null || !player.Owner.IsActive)
                    continue;
                bool youDied = victim.Owner == player.Owner;
                bool youKilled = killer.Owner == player.Owner;
                bool inEarshot = LocalCharacter.Characters.TryGetValue(player.Owner, out LocalCharacter listener)
                    && listener != null
                    && (listener.transform.position - position).sqrMagnitude <= audibleSqr;
                if (!youDied && !youKilled && !inEarshot)
                    continue;
                InstanceFinder.ServerManager.Broadcast(player.Owner, new MM_KillBroadcast {
                    position = position,
                    byGun = source == DamageType.Gun,
                    youDied = youDied,
                    youKilled = youKilled,
                });
            }
        }

        // ---------- NPCs ----------

        void SpawnNpcs(int firstNpcRole) {
            for (int i = 0; i < startNPCs; i++) {
                LocalNPC npc = ServerNPC.SpawnNPC(RobotNPC_Prefab, location: DebugSingleNpc.Value ? new Vector3(1117.56995f, 12.12100029f, 1008.34003f) : null);
                GameCharacter gameCharacter = npc.GetComponent<GameCharacter>();
                gameCharacter.Init(100);
                // Classic protects everyone during the role reveal anyway; its spawn shield would only glow.
                if (mode != MM_Mode.Classic)
                    gameCharacter.InitDefaultEffects();
                RandomizeCharacterName(gameCharacter);
                MM_LocalNPC body = npc.gameObject.AddComponent<MM_LocalNPC>();
                switch (mode) {
                    case MM_Mode.NPCsVsPlayers:
                        gameCharacter.SetTeam(new TeamConfig(TeamColor.Red, TeamColor.Red));
                        npc.AttackDetectionRadius *= HuntAttackDetectionMultiplier;
                        break;
                    case MM_Mode.Classic:
                        SetUpClassicNpc(npc, gameCharacter, body, classicRoles[firstNpcRole + i]);
                        break;
                    case MM_Mode.Infection:
                        gameCharacter.SetTeam(new TeamConfig(npcTeams[i]));
                        npc.AllowAttackTargetOverrides = false;
                        if (npcTeams[i] == TeamColor.Red) {
                            gameCharacter.GetComponent<RobotColor>().ApplyColor(TeamColor.Green);
                            npc.AttackDetectionRadius *= InfectionAttackDetectionMultiplier;
                        }
                        break;
                    case MM_Mode.Unarmed:
                        gameCharacter.SetTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
                        break;
                }
                gameCharacter.OnDied += (source, killer) => {
                    SharedOnDied(gameCharacter, source, killer);
                    if (source == DamageType.Fall) {
                        if (FallTest.Value)
                            Time.timeScale = 0f;
                        Debug.LogError($"[MM] NPC {gameCharacter.name} fell out of the map.", gameCharacter);
                    }
                    GameCharacter killerCharacter = killer as GameCharacter;
                    if (mode != MM_Mode.Classic && killerCharacter != null && killerCharacter.Owner.IsValid) {
                        int kills = SharedGlobalEvents.GetLeaderboardIndex("Kills");
                        if (kills != -1) // if it exists
                            PlayerData.GetPlayerData(killerCharacter.Owner).leaderboard[kills]++;
                    }
                };
            }
        }

        /// <summary>A Classic NPC: a townsperson who runs from a murderer giving themselves away, the murderer, or the sheriff.</summary>
        void SetUpClassicNpc(LocalNPC npc, GameCharacter character, MM_LocalNPC body, MM_Role role) {
            character.SetTeam(new TeamConfig(MM_ClassicRound.TeamOf(role), TeamColor.None));
            classic.AddParticipant(character, null, role);
            // Townsfolk only run from a murderer with the knife out, or one they just saw kill.
            // (A murderer with the Ninja perk only gives the knife away up close.)
            npc.FleeThreatFilter = murderer => classic.IsVisibleThreatFrom(murderer, npc.transform.position);
            npc.ConfigureFleeRadii(classicTuning.npcPanicDistance, classicTuning.npcPanicDistance * classicTuning.npcPanicClearMultiplier);
            switch (role) {
                case MM_Role.Murderer:
                    body.KillAllowed = () => classic.CanKill(character);
                    body.IssueKnife = murderer => classic.ArmMurderer(murderer, null);
                    body.ThrowKnife = classic.TryThrow;
                    body.ThrowChancePerSecond = classicTuning.npcThrowChancePerSecond;
                    body.ThrowDistance = classicTuning.npcThrowDistance;
                    body.ThrowWindupSeconds = classicTuning.npcThrowWindupSeconds;
                    body.TargetHidden = classic.HiddenBySmoke;
                    MM_NPC hunter = npc.gameObject.AddComponent<MM_NPC>();
                    hunter.ConfigureSheriffHunting(_ => classic.SheriffNoticeDistance(character),
                        classicTuning.sheriffDetectionMultiplier, classicTuning.sheriffTargetPriority,
                        classicTuning.sheriffCreepDistance, classicTuning.sheriffCreepSpeed);
                    hunter.TargetHidden = classic.HiddenBySmoke;
                    break;
                case MM_Role.Sheriff:
                    ToolBaseShared gun = classic.ArmSheriff(character);
                    npc.gameObject.AddComponent<MM_NpcSheriff>().Configure(classic, classicTuning, gun);
                    break;
            }
        }

        /// <summary>No player is left alive in a Classic round: the NPC murderers stop blending in and search the house.</summary>
        void OnClassicEndgame() {
            foreach (GameCharacter murderer in classic.LivingWithRole(MM_Role.Murderer))
                if (murderer.TryGetComponent(out LocalNPC npc))
                    npc.AttackDetectionRadius *= HuntAttackDetectionMultiplier;
        }

        // ---------- players ----------

        void SpawnOtherModeTools(PlayerData player, NetworkObject character) {
            if (mode == MM_Mode.Unarmed)
                ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
            switch (player.team.Value.realTeam) {
                case TeamColor.Red:
                    ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
                    break;
                case TeamColor.Blue:
                    ServerTool.Instance.SpawnTool(character, ToolEnum.Pistol, tool => {
                        if (mode == MM_Mode.Infection)
                            tool.maxClipAmmo = 6;
                    });
                    break;
            }
        }

        void SpawnPlayers(List<PlayerData> players) {
            for (int i = 0; i < players.Count; i++) {
                PlayerData player = players[i];
                // Establish authoritative round state before replacing the lobby
                // character. This keeps every CharacterAdded observer from seeing
                // a stale Ghost/lobby team during the spawn callback.
                MM_Role classicRole = mode == MM_Mode.Classic ? classicRoles[i] : MM_Role.None;
                switch (mode) {
                    case MM_Mode.NPCsVsPlayers:
                    case MM_Mode.Unarmed:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                        break;
                    case MM_Mode.Classic:
                        player.SetPlayerTeam(new TeamConfig(MM_ClassicRound.TeamOf(classicRole), TeamColor.None));
                        break;
                    case MM_Mode.Infection:
                        player.SetPlayerTeam(new TeamConfig(i < playerTeams.Count ? playerTeams[i] : TeamColor.Blue));
                        break;
                }

                LocalCharacter character = ServerPlayerCharacter.Instance.SpawnPlayerCharacter(player.Owner);
                if (character == null)
                    continue;
                RandomizeCharacterName(character);
                if (mode == MM_Mode.Classic) {
                    // The role reveal is covered by the global invulnerability; the spawn shield's glow would
                    // only mark players out from the NPCs.
                    character.EffectsComponent.RemoveEffect(CharacterEffect.Invul);
                    classic.AddParticipant(character, player, classicRole);
                    if (classicRole == MM_Role.Murderer)
                        classic.ArmMurderer(character, player);
                    else if (classicRole == MM_Role.Sheriff)
                        classic.ArmSheriff(character, player);
                } else {
                    SpawnOtherModeTools(player, character.NetworkObject);
                    ServerChat.SendSystemMessage(player.Owner, new($"Welcome to the {GetTeamName(player.team.Value.realTeam)} team!", SystemMessageSource.CustomMessage));
                }
            }
        }

        void RandomizeCharacterName(GameCharacter gameCharacter) {
            alienNames ??= ServerBootStrap.universeCfg.LoadText("alien_names").Replace("\r", "").Split("\n");
            gameCharacter.DisplayName = alienNames[UnityEngine.Random.Range(0, alienNames.Length)];
        }

        string GetTeamName(TeamColor team) {
            return team switch {
                TeamColor.Red => "Murderer",
                TeamColor.Green => "Innocent",
                TeamColor.Blue => "Sheriff",
                _ => "Unknown"
            };
        }

        string GetWinnerName(TeamColor team) {
            return team switch {
                TeamColor.Red => mode == MM_Mode.Infection ? "Infected" : "Murderers",
                TeamColor.Green => "Innocents",
                TeamColor.Blue => "Sheriffs",
                _ => "Nobody"
            };
        }

        int GetNPCCount() => GameObject.FindGameObjectsWithTag("NPC").Count();
        int GetTeamCount(TeamColor team) => GameCharacter.TeamToCharacter.TryGetValue(team, out var characters) ? characters.Count : 0;

        void AssignInfectionTeams(int players) {
            npcTeams = Enumerable.Repeat(TeamColor.Green, startNPCs).ToList();
            playerTeams = Enumerable.Repeat(TeamColor.Blue, players).ToList();
            int infected = GetComponent<MM_Roles>().InfectedCount(startNPCs);
            foreach (int index in Enumerable.Range(0, startNPCs).OrderBy(_ => UnityEngine.Random.value).Take(infected))
                npcTeams[index] = TeamColor.Red;
        }

        TeamColor GetInfectionWinnerTeam() {
            if (GetTeamCount(TeamColor.Red) == 0)
                return TeamColor.Blue;
            if (GetTeamCount(TeamColor.Blue) == 0)
                return TeamColor.Red;
            return TeamColor.None;
        }

        protected override bool UpdateInGameBar(int durationLeft, bool interrupted) {
            gameDurationLeft = durationLeft;
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                case MM_Mode.Unarmed:
                    int npcsLeft = GetNPCCount();
                    SetTopMessage(string.Format(mode == MM_Mode.Unarmed ? "Civilians Left: {0} ({1})" : "NPC Killers Left: {0} ({1})", npcsLeft, durationLeft));
                    return npcsLeft > 0;
                case MM_Mode.Infection:
                    SetTopMessage($"Infection In Progress ({durationLeft})");
                    return GetInfectionWinnerTeam() == TeamColor.None;
                default:
                    // Classic runs its own clock (MM_ClassicRound.RunHunt).
                    return true;
            }
        }

        async UniTask SpawnCoinLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                ServerCoin.SpawnCoin(null);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(MathHelper.Range(5f, 15f)),
                    cancellationToken: token
                ).SuppressCancellationThrow();
            }
        }

        // ---------- rounds ----------

        protected override async UniTask StartAsync(CancellationToken token) {
            await base.StartAsync(token);

            if (!DebugNoIntermission.Value)
                mode = (MM_Mode)await ServerVote.StartVote(VoteEnum.MM_VoteMode, 15f, token);
            SetLeaderboardEnabled("Kills", mode != MM_Mode.Classic);
            List<PlayerData> players = PlayerData.Players.Values
                .Where(player => player.Owner.IsValid && player.Owner.IsAuthenticated)
                .ToList();
            int baseNpcs = mode == MM_Mode.Classic ? classicTuning.NpcsFor(players.Count) : OtherModesNpcCount;
            startNPCs = FallTest.Value ? 700 : Mathf.RoundToInt((DebugSingleNpc.Value ? 1 : baseNpcs) * SpawnMultiplier);
            if (mode == MM_Mode.Classic)
                classicRoles = MM_Roles.DealClassic(players, startNPCs, classicTuning, DebugFirstPlayerRole());
            else if (mode == MM_Mode.Infection)
                AssignInfectionTeams(players.Count);

            roundNumber++;
            PlayerProgress.ModifyAll<MM_SaveData>(save => save.roundsPlayed++);
            // Rounds unlock perks: everyone's picker learns about a new one as the round starts.
            MM_PerkPicker.SendAll();
            if (mode == MM_Mode.Classic) {
                classic.Begin(roundNumber);
                SpawnNpcs(players.Count);
                SpawnPlayers(players);
                gameInProgress = true;
                await classic.RunRoleReveal(token);
                await RunClassicRound(token);
                return;
            }

            SpawnNpcs(0);
            SpawnPlayers(players);
            base.SetGlobalInvul(false);
            int gameTime = DebugTimerSpeedUp.Value ? 10 : otherModesRoundSeconds;
            gameDurationLeft = gameTime;
            gameInProgress = true;
            using CancellationTokenSource coinCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            SpawnCoinLoop(coinCts.Token).Forget(exception => Debug.LogException(exception, this));
            try {
                await GameTimerCountdown(gameTime, token);
            } finally {
                coinCts.Cancel();
            }
            ServerCoin.ClearAllCoins();
            base.SetGlobalInvul(true);
            switch (mode) {
                case MM_Mode.Unarmed:
                case MM_Mode.NPCsVsPlayers:
                    List<PlayerData> scores = base.GetLeaderboardWinner("Kills");
                    if (scores.Count > 0) {
                        ServerChat.SendSystemMessage(new($"{scores[0].username.Value} wins with {scores[0].leaderboard[SharedGlobalEvents.GetLeaderboardIndex("Kills")]} kills!", SystemMessageSource.CustomMessage));
                    }
                    await TimerCountdown($"Game over: {GetNPCCount()}/{startNPCs} NPCs survived! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 5, token);
                    break;
                case MM_Mode.Infection:
                    TeamColor infectionWinner = GetInfectionWinnerTeam();
                    if (infectionWinner == TeamColor.None)
                        infectionWinner = TeamColor.Blue;
                    await TimerCountdown($"{GetWinnerName(infectionWinner)} Win! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 10, token);
                    break;
            }
        }

        MM_Role DebugFirstPlayerRole() {
#if UNITY_EDITOR
            return debugFirstPlayerRole;
#else
            return MM_Role.None;
#endif
        }

        /// <summary>Classic: the timed hunt, then the reveal.</summary>
        async UniTask RunClassicRound(CancellationToken token) {
            base.SetGlobalInvul(false);
            await classic.RunHunt(DebugTimerSpeedUp.Value ? 30 : classicTuning.huntSeconds, token);
            base.SetGlobalInvul(true);
            await classic.RunResults(DebugTimerSpeedUp.Value ? 4 : classicTuning.resultsSeconds, token);
        }

        /// <summary>Players' walk and sprint speeds from the tuning, times <paramref name="multiplier"/> (a smoke bomb's burst).</summary>
        public void ApplyPlayerSpeed(PlayerData player, float multiplier) {
            if (player == null)
                return;
            player.walkSpeed.Value = classicTuning.walkSpeed * multiplier;
            player.sprintSpeed.Value = classicTuning.sprintSpeed * multiplier;
        }

        /// <summary>Sends one player their state now instead of at the next tick (after something they did changed it).</summary>
        public void SendClassicState(PlayerData player) {
            if (player == null || !player.Owner.IsActive || InstanceFinder.ServerManager == null || mode != MM_Mode.Classic || !gameInProgress)
                return;
            InstanceFinder.ServerManager.Broadcast(player.Owner, classic.BuildState(player, true));
        }

        /// <summary>Tells each player the round phase, their own role and their part in it.</summary>
        async UniTask BroadcastStateLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                if (await UniTask.Delay(TimeSpan.FromSeconds(StateInterval), cancellationToken: token).SuppressCancellationThrow())
                    return;
                if (InstanceFinder.ServerManager == null || !InstanceFinder.ServerManager.Started || PlayerData.Players == null)
                    continue;
                bool classicRound = mode == MM_Mode.Classic && gameInProgress;
                classic.RefreshOdds();
                foreach (PlayerData player in PlayerData.Players.Values.ToList()) {
                    if (player == null || !player.Owner.IsActive)
                        continue;
                    MM_StateBroadcast state = classic.BuildState(player, classicRound);
                    if (!classicRound) {
                        state.phase = gameInProgress ? MM_Phase.Hunt : MM_Phase.Lobby;
                        state.secondsLeft = gameDurationLeft;
                        state.classic = mode == MM_Mode.Classic;
                    }
                    InstanceFinder.ServerManager.Broadcast(player.Owner, state);
                }
            }
        }

        protected override void Stop() {
            base.Stop();
            classic?.End();
            gameInProgress = false;
            gameDurationLeft = 0;
            pendingInfectionRevives.Clear();
        }

        protected override void Reset() {
            base.Reset();
            ServerNPC.ClearAllNPC();
            ServerCoin.ClearAllCoins();

            // Tools are stored in PlayerData and are copied into every newly spawned
            // character. Clear the round inventory before respawning lobby characters,
            // otherwise an infection dagger survives into the next round.
            foreach (PlayerData player in PlayerData.Players.Values)
                ServerTool.Instance.ClearTools(player.Owner);

            SetPlayerTeams(new TeamConfig(TeamColor.White));
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            SetLeaderboardEnabled("Kills", false);
        }

        /// <summary>Changes the Classic tuning at runtime (server commands) and refreshes everyone's role odds.</summary>
        public static void TuneClassic(Action<MM_ClassicTuning> change) {
            if (current == null || change == null)
                return;
            change(current.classicTuning);
            current.classic.RefreshOdds(force: true);
        }

        protected override void OnDestroy() {
            if (current == this)
                current = null;
            if (InstanceFinder.ServerManager != null && classic != null) {
                InstanceFinder.ServerManager.UnregisterBroadcast<MM_ThrowKnifeRequest>(classic.OnThrowRequest);
                InstanceFinder.ServerManager.UnregisterBroadcast<MM_SmokeRequest>(classic.OnSmokeRequest);
                InstanceFinder.ServerManager.UnregisterBroadcast<MM_PerkRequest>(MM_PerkPicker.OnPerkRequest);
            }
            if (classic != null) {
                classic.EndgameStarted -= OnClassicEndgame;
                if (CoinShared.CanCollect == classic.CanCollectCoin)
                    CoinShared.CanCollect = null;
                CoinShared.Collected -= classic.OnCoinCollected;
            }
            if (ServerPlayerCharacter.CanSpawnFunction == CanSpawnFunction)
                ServerPlayerCharacter.CanSpawnFunction = null;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnLocationFunction)
                ServerPlayerCharacter.SpawnLocationFunction = null;
            base.OnDestroy();
        }

        public static void RefreshNPCSpeeds() {
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")) {
                if (obj.TryGetComponent(out LocalNPC npc)) {
                    npc.UpdateSpeed();
                }
            }
        }
    }
}
