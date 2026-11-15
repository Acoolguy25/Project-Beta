using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Rounds;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// Flag Wars: red vs blue with pistol and dagger. Run over the enemy flag to take it, bring it to
    /// your own stand while your flag is home to score. Touch your dropped flag to send it home.
    /// Teams, flags, captures and kills are all decided here.
    /// </summary>
    public sealed class CTF_ServerRunner : ServerRunner {
        [Header("Round")]
        [SerializeField, Min(1)] int minPlayers = 2;
        [SerializeField, Min(1)] int countdownSeconds = 8;
        [SerializeField, Min(30)] int matchSeconds = 480;
        [SerializeField, Min(3)] int resultsSeconds = 10;
        [SerializeField, Min(1)] int capturesToWin = CTF_Rules.CapturesToWin;
        [SerializeField, Min(0.5f)] float respawnSeconds = 4f;
        [Header("Movement")]
        [SerializeField] float walkSpeed = 14f, sprintSpeed = 21f;
        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong killXP = 10, captureXP = 50, captureGold = 20, returnXP = 10, winXP = 60, winGold = 25;

        const string CapturesColumn = "Captures";
        const string KillsColumn = "Kills";
        static readonly ToolEnum[] Loadout = { ToolEnum.Pistol, ToolEnum.Dagger };

        sealed class Flag {
            public CTF_Team team;
            public CTF_FlagState state;
            public Vector3 position;
            public NetworkConnection carrier;
            public float droppedAt;
        }

        readonly Dictionary<NetworkConnection, CTF_Team> teams = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Flag[] flags = { new() { team = CTF_Team.Red }, new() { team = CTF_Team.Blue } };
        readonly System.Random random = new();
        CTF_Map map;
        CTF_Phase phase;
        int redScore, blueScore, secondsLeft;
        string headline = "";
        float nextSnapshot;

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -30f;
            SharedGlobalEvents.TeamEnemies = new() {
                [TeamColor.Red] = new() { TeamColor.Blue },
                [TeamColor.Blue] = new() { TeamColor.Red },
            };
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.RespawnTime = respawnSeconds;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<CTF_StateRequest>(OnStateRequest);
        }

        static TeamColor Color(CTF_Team team) => team == CTF_Team.Red ? TeamColor.Red : TeamColor.Blue;
        int Score(CTF_Team team) => team == CTF_Team.Red ? redScore : blueScore;

        Vector3 SpawnPosition(NetworkConnection connection) {
            if (map == null) return Vector3.up * 2f;
            if (phase is CTF_Phase.Waiting || !teams.TryGetValue(connection, out var team)) return map.lobbySpawn.position + Vector3.up;
            var spawns = map.Spawns(team);
            return spawns[random.Next(spawns.Length)].position + Vector3.up;
        }

        // ---------------------------------------------------------------- players
        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            player.cameraTypes.Clear();
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            player.cameraTypes.Add(GameCameraType.FirstPersonCamera);
            player.walkSpeed.Value = walkSpeed;
            player.sprintSpeed.Value = sprintSpeed;
            player.jumpHeight.Value = 1.6f;
            player.tools.Clear();
            player.tools.AddRange(Loadout);
            AssignTeam(player.Owner);
            PlayerProgress.Load<CTF_SaveData>(player, CTF_SaveData.Universe).Forget();
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            if (!teams.TryGetValue(character.Owner, out var team)) team = AssignTeam(character.Owner);
            character.SetTeam(new TeamConfig(Color(team)));
            // PlayerData.OnPlayerAdded and the character spawn are separate subscribers: make sure the loadout exists.
            foreach (var tool in Loadout) ServerTool.Instance.SpawnTool(character.NetworkObject, tool);
            var owner = character.Owner;
            character.OnDied += (source, killer) => OnCharacterDied(owner, character, killer);
        }

        CTF_Team AssignTeam(NetworkConnection connection) {
            if (!teams.TryGetValue(connection, out var team)) {
                int red = teams.Count(t => t.Value == CTF_Team.Red && t.Key.IsActive), blue = teams.Count(t => t.Value == CTF_Team.Blue && t.Key.IsActive);
                team = CTF_Rules.PickTeam(red, blue, redScore, blueScore);
                teams[connection] = team;
            }
            if (PlayerData.TryGetPlayerData(connection, out var player)) player.SetPlayerTeam(new TeamConfig(Color(team)));
            InstanceFinder.ServerManager.Broadcast(connection, new CTF_TeamBroadcast { team = team });
            return team;
        }

        void OnPlayerLeft(PlayerData player) {
            foreach (var flag in flags) if (flag.carrier == player.Owner) Drop(flag, flag.position);
            teams.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
        }

        void OnCharacterDied(NetworkConnection owner, LocalCharacter character, IEntity killer) {
            foreach (var flag in flags)
                if (flag.carrier == owner) Drop(flag, character != null ? character.transform.position : flag.position);
            if (phase != CTF_Phase.Playing) return;
            if (killer is GameCharacter k && k.Owner != null && k.Owner.IsValid && k.Owner != owner && PlayerData.TryGetPlayerData(k.Owner, out var p)) {
                RoundLeaderboard.Add(p, KillsColumn, 1);
                Award(k.Owner, killXP, 0);
                PlayerProgress.Modify<CTF_SaveData>(p, save => save.kills++);
            }
        }

        // ---------------------------------------------------------------- round
        protected override async UniTask StartAsync(CancellationToken token) {
            phase = CTF_Phase.Waiting;
            await WaitForSceneAsync("capture_the_flag_start", token);
            map = FindAnyObjectByType<CTF_Map>();
            if (map == null || !map.IsConfigured) throw new InvalidOperationException("Capture the flag requires a configured CTF_Map in capture_the_flag_start.");
            SetGlobalInvul(true);
            SetTeamKillEnabled(true); // teammates can't hurt each other
            SetLeaderboardEnabled(CapturesColumn, true);
            SetLeaderboardEnabled(KillsColumn, true);
            SetMusic(MusicSelection.GameMusic);
            redScore = blueScore = 0;
            foreach (var flag in flags) Return(flag, false);
            // Rebalance for the new match.
            var active = GetActivePlayers().Select(p => p.Owner).ToList();
            TeamSplit.Shuffle(active, random);
            teams.Clear();
            for (int i = 0; i < active.Count; i++) teams[active[i]] = i % 2 == 0 ? CTF_Team.Red : CTF_Team.Blue;
            foreach (var c in active) AssignTeam(c);
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();

            while (GetActivePlayersCount() < minPlayers) {
                headline = $"Waiting for players ({GetActivePlayersCount()}/{minPlayers})";
                SetTopMessage(headline);
                BroadcastState();
                await AwaitTime(1000, token);
            }

            phase = CTF_Phase.Countdown;
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            for (int i = countdownSeconds; i > 0; i--) {
                secondsLeft = i;
                headline = $"Capture {capturesToWin} flags to win. Starting in {i}";
                SetTopMessage(headline);
                BroadcastState();
                await AwaitTime(1000, token);
            }

            phase = CTF_Phase.Playing;
            SetGlobalInvul(false);
            ServerChat.SendSystemMessage(new("FIGHT! Take the enemy flag back to your base.", SystemMessageSource.CustomMessage));
            float end = Time.time + matchSeconds;
            while (true) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(end - Time.time));
                if (secondsLeft == 0 || redScore >= capturesToWin || blueScore >= capturesToWin) break;
                headline = $"RED {redScore}  -  {blueScore} BLUE";
                SetTopMessage($"{headline}   ({RoundFormat.Clock(secondsLeft)})");
                await AwaitTime(500, token);
            }

            // ---- results
            phase = CTF_Phase.Results;
            SetGlobalInvul(true);
            foreach (var flag in flags) Return(flag, false);
            var winner = CTF_Rules.Winner(redScore, blueScore);
            headline = winner is CTF_Team w ? $"{CTF_Rules.Name(w)} TEAM WINS {Math.Max(redScore, blueScore)}-{Math.Min(redScore, blueScore)}!" : $"DRAW {redScore}-{blueScore}";
            SetMusic(MusicSelection.VictoryMusic);
            if (winner is CTF_Team won) {
                foreach (var pair in teams.Where(t => t.Value == won && t.Key.IsActive)) {
                    Award(pair.Key, winXP, winGold);
                    PlayerProgress.Modify<CTF_SaveData>(pair.Key, save => save.wins++);
                }
                InstanceFinder.ServerManager.Broadcast(new CTF_EventBroadcast { kind = CTF_EventKind.Won, flag = won });
            }
            ServerChat.SendSystemMessage(new(headline, SystemMessageSource.CustomMessage));
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                SetTopMessage($"{headline}  Next match in {i}");
                BroadcastState();
                await AwaitTime(1000, token);
            }
        }

        // ---------------------------------------------------------------- flags
        void FixedUpdate() {
            if (map == null) return;
            foreach (var pair in LocalCharacter.Characters)
                if (pair.Value != null && !pair.Value.IsDead && pair.Value.IsSpawned && pair.Value.transform.position.y < map.killY)
                    pair.Value.Kill(DamageType.Despawn);
            if (phase != CTF_Phase.Playing) {
                if (Time.time > nextSnapshot) { nextSnapshot = Time.time + 1f; BroadcastState(); }
                return;
            }
            foreach (var flag in flags) UpdateFlag(flag);
            if (Time.time > nextSnapshot) { nextSnapshot = Time.time + 0.1f; BroadcastState(); }
        }

        void UpdateFlag(Flag flag) {
            if (flag.state == CTF_FlagState.Carried) {
                if (!LocalCharacter.Characters.TryGetValue(flag.carrier, out var carrier) || carrier == null || carrier.IsDead) {
                    Drop(flag, flag.position);
                    return;
                }
                flag.position = carrier.transform.position;
                // Score: carrier reaches their own stand while their own flag is home.
                var carrierTeam = CTF_Rules.Other(flag.team);
                if (flags[(int)carrierTeam].state == CTF_FlagState.Home && CTF_Rules.InReach(flag.position, map.Stand(carrierTeam), CTF_Rules.CaptureRange))
                    Capture(flag, carrierTeam);
                return;
            }
            if (flag.state == CTF_FlagState.Dropped && Time.time - flag.droppedAt > CTF_Rules.DropReturnSeconds) {
                Return(flag, true);
                return;
            }
            foreach (var pair in LocalCharacter.Characters) {
                var character = pair.Value;
                if (character == null || character.IsDead || !teams.TryGetValue(pair.Key, out var team)) continue;
                if (!CTF_Rules.InReach(character.transform.position, flag.position, CTF_Rules.PickupRange)) continue;
                if (team != flag.team) {
                    flag.state = CTF_FlagState.Carried;
                    flag.carrier = pair.Key;
                    Event(CTF_EventKind.Taken, flag.team, pair.Key);
                    SetCarrierSpeed(pair.Key, true);
                    return;
                }
                if (flag.state == CTF_FlagState.Dropped) {
                    Award(pair.Key, returnXP, 0);
                    Event(CTF_EventKind.Returned, flag.team, pair.Key);
                    Return(flag, false);
                    return;
                }
            }
        }

        void Capture(Flag flag, CTF_Team scorer) {
            var carrier = flag.carrier;
            if (scorer == CTF_Team.Red) redScore++; else blueScore++;
            if (PlayerData.TryGetPlayerData(carrier, out var p)) RoundLeaderboard.Add(p, CapturesColumn, 1);
            Award(carrier, captureXP, captureGold);
            PlayerProgress.Modify<CTF_SaveData>(carrier, save => save.captures++);
            Event(CTF_EventKind.Captured, flag.team, carrier);
            ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(carrier)} captured the {CTF_Rules.Name(flag.team)} flag! RED {redScore} - {blueScore} BLUE", SystemMessageSource.CustomMessage));
            Return(flag, false);
        }

        void Drop(Flag flag, Vector3 at) {
            if (flag.state != CTF_FlagState.Carried) return;
            var carrier = flag.carrier;
            SetCarrierSpeed(carrier, false);
            flag.carrier = null;
            if (map != null && at.y < map.killY + 2f) { Return(flag, true); return; }
            flag.state = CTF_FlagState.Dropped;
            flag.position = at;
            flag.droppedAt = Time.time;
            Event(CTF_EventKind.Dropped, flag.team, carrier);
        }

        void Return(Flag flag, bool announce) {
            if (flag.carrier != null) SetCarrierSpeed(flag.carrier, false);
            flag.state = CTF_FlagState.Home;
            flag.carrier = null;
            if (map != null) flag.position = map.Stand(flag.team);
            if (announce) Event(CTF_EventKind.Returned, flag.team, null);
        }

        void SetCarrierSpeed(NetworkConnection connection, bool carrying) {
            if (connection == null || !PlayerData.TryGetPlayerData(connection, out var player)) return;
            float m = carrying ? CTF_Rules.CarrierSpeedMultiplier : 1f;
            player.walkSpeed.Value = walkSpeed * m;
            player.sprintSpeed.Value = sprintSpeed * m;
        }

        void Event(CTF_EventKind kind, CTF_Team flag, NetworkConnection player) {
            InstanceFinder.ServerManager.Broadcast(new CTF_EventBroadcast {
                kind = kind, flag = flag,
                playerName = player != null ? PlayerData.GetPlayerName(player) : "",
                playerId = player != null ? player.ClientId : -1
            });
            BroadcastState();
        }

        // ---------------------------------------------------------------- messages
        void OnStateRequest(NetworkConnection connection, CTF_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            requestTimes[connection] = Time.unscaledTime;
            if (teams.TryGetValue(connection, out var team)) InstanceFinder.ServerManager.Broadcast(connection, new CTF_TeamBroadcast { team = team });
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        static CTF_FlagSnap Snap(Flag flag) => new() {
            state = flag.state, position = flag.position, carrierId = flag.carrier != null ? flag.carrier.ClientId : -1
        };

        CTF_StateBroadcast MakeState() => new() {
            phase = phase, secondsLeft = secondsLeft, redScore = redScore, blueScore = blueScore,
            redFlag = Snap(flags[0]), blueFlag = Snap(flags[1]), headline = headline
        };

        void BroadcastState() => InstanceFinder.ServerManager.Broadcast(MakeState(), true, Channel.Unreliable);

        static void Award(NetworkConnection connection, ulong xp, ulong gold) {
            if ((xp != 0 || gold != 0) && !LevelsServer.AwardPlayerXPAndGold(connection, xp, gold))
                Debug.LogWarning($"Capture the flag could not award rewards to connection {connection.ClientId}.");
        }

        static void SetMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            phase = CTF_Phase.Waiting;
            base.Reset();
        }

        protected override void OnDestroy() {
            GameCharacter.FallHeightOverride = null;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            InstanceFinder.ServerManager?.UnregisterBroadcast<CTF_StateRequest>(OnStateRequest);
            base.OnDestroy();
        }
    }
}
