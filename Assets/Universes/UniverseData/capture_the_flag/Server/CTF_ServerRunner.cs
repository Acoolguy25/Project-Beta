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
    /// Mid-lane power-ups heal or speed you up. Teams, flags, captures, kills, hits and power-ups are
    /// all decided here; clients only draw the broadcasts.
    /// </summary>
    public sealed class CTF_ServerRunner : ServerRunner {
        [Header("Round")]
        [Tooltip("The match waits until this many players are in. There are no bots, so a lone player waits on " +
                 "\"Waiting for players (1/2)\" until a second one joins (set 1 by reflection for solo tests).")]
        [SerializeField, Min(1)] int minPlayers = 2;
        [SerializeField, Min(1)] int countdownSeconds = 8;
        [SerializeField, Min(30)] int matchSeconds = 480;
        [SerializeField, Min(3)] int resultsSeconds = 10;
        [SerializeField, Min(1)] int capturesToWin = CTF_Rules.CapturesToWin;
        [SerializeField, Min(0.5f)] float respawnSeconds = 4f;
        [Header("Characters")]
        [Tooltip("Health every character spawns with. Pistol and dagger hits deal 100, so 200 = two hits.")]
        [SerializeField, Min(1)] long characterHealth = 200;
        [SerializeField] float walkSpeed = 14f, sprintSpeed = 21f;
        [SerializeField, Min(0f)] float jumpHeight = 1.6f;
        [Header("Power-ups")]
        [SerializeField, Min(1f)] float powerUpRespawnSeconds = 20f;
        [SerializeField, Min(1)] long healAmount = 100;
        [SerializeField, Min(1f)] float speedBoostMultiplier = 1.35f;
        [SerializeField, Min(0.5f)] float speedBoostSeconds = 8f;
        [Header("Network")]
        [Tooltip("State broadcasts per second while playing (once a second otherwise).")]
        [SerializeField, Min(1f)] float snapshotsPerSecond = 10f;
        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong killXP = 10, captureXP = 50, captureGold = 20, returnXP = 10, winXP = 60, winGold = 25;

        const string CapturesColumn = "Flags";
        const string KillsColumn = "Kills";
        static readonly ToolEnum[] Loadout = { ToolEnum.Pistol, ToolEnum.Dagger };

        sealed class Flag {
            public CTF_Team team;
            public CTF_FlagState state;
            public Vector3 position;
            public NetworkConnection carrier;
            public float droppedAt;
        }

        /// <summary>
        /// Remembers who last damaged one character so the health drop that follows can be reported to
        /// the attacker as a confirmed hit. Lives as long as the character (subscriptions die with it).
        /// </summary>
        sealed class DamageWatch {
            readonly CTF_ServerRunner runner;
            readonly GameCharacter victim;
            IEntity attacker;

            public DamageWatch(CTF_ServerRunner runner, GameCharacter victim) {
                this.runner = runner;
                this.victim = victim;
                victim.OnDamage += OnDamage;
                victim.Health.OnChange += OnHealthChanged;
            }

            void OnDamage(DamageType type, IEntity source) => attacker = source;

            void OnHealthChanged(long previous, long next, bool asServer) {
                if (!asServer || next >= previous || attacker == null) return;
                var source = attacker;
                attacker = null;
                runner.ReportHit(source, victim, previous - next, next == 0);
            }
        }

        readonly Dictionary<NetworkConnection, CTF_Team> teams = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Dictionary<NetworkConnection, float> boostedUntil = new();
        readonly List<NetworkConnection> expiredBoosts = new();
        readonly Flag[] flags = { new() { team = CTF_Team.Red }, new() { team = CTF_Team.Blue } };
        readonly System.Random random = new();
        CTF_Map map;
        CTF_Phase phase;
        int redScore, blueScore, secondsLeft;
        string headline = "";
        float nextSnapshot, previousRespawnTime;
        uint powerUpsReady, stateSequence;
        float[] powerUpReadyAt = Array.Empty<float>();

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -30f;
            SharedGlobalEvents.TeamEnemies = new() {
                [TeamColor.Red] = new() { TeamColor.Blue },
                [TeamColor.Blue] = new() { TeamColor.Red },
            };
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.SpawnRotationFunction = SpawnRotation;
            previousRespawnTime = ServerPlayerCharacter.RespawnTime;
            ServerPlayerCharacter.RespawnTime = respawnSeconds;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<CTF_StateRequest>(OnStateRequest);
        }

        static TeamColor Color(CTF_Team team) => team == CTF_Team.Red ? TeamColor.Red : TeamColor.Blue;

        Vector3 SpawnPosition(NetworkConnection connection) {
            if (map == null) return Vector3.up * 2f;
            if (phase is CTF_Phase.Waiting || !teams.TryGetValue(connection, out var team)) return map.lobbySpawn.position + Vector3.up;
            var spawns = map.Spawns(team);
            return spawns[random.Next(spawns.Length)].position + Vector3.up;
        }

        /// <summary>Spawn facing the enemy base, so the first view is the field rather than a wall.</summary>
        Quaternion SpawnRotation(NetworkConnection connection) {
            if (map == null || !teams.TryGetValue(connection, out var team)) return Quaternion.identity;
            Vector3 toward = map.Stand(CTF_Rules.Other(team)) - map.Stand(team);
            toward.y = 0f;
            return toward.sqrMagnitude > 0f ? Quaternion.LookRotation(toward) : Quaternion.identity;
        }

        // ---------------------------------------------------------------- players
        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            player.cameraTypes.Clear();
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            player.cameraTypes.Add(GameCameraType.FirstPersonCamera);
            player.walkSpeed.Value = walkSpeed;
            player.sprintSpeed.Value = sprintSpeed;
            player.jumpHeight.Value = jumpHeight;
            player.tools.Clear();
            player.tools.AddRange(Loadout);
            AssignTeam(player.Owner);
            PlayerProgress.Load<CTF_SaveData>(player, CTF_SaveData.Universe).Forget();
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            if (!teams.TryGetValue(character.Owner, out var team)) team = AssignTeam(character.Owner);
            character.SetTeam(new TeamConfig(Color(team)));
            character.Init(characterHealth);
            // PlayerData.OnPlayerAdded and the character spawn are separate subscribers: make sure the loadout exists.
            foreach (var tool in Loadout) ServerTool.Instance.SpawnTool(character.NetworkObject, tool);
            var owner = character.Owner;
            character.OnDied += (source, killer) => OnCharacterDied(owner, character, source, killer);
            _ = new DamageWatch(this, character);
            // A fresh body keeps a running boost's speed.
            RefreshSpeed(owner);
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
            boostedUntil.Remove(player.Owner);
        }

        void OnCharacterDied(NetworkConnection owner, GameCharacter character, DamageType source, IEntity killer) {
            foreach (var flag in flags)
                if (flag.carrier == owner) Drop(flag, character != null ? character.transform.position : flag.position);
            var killerCharacter = killer as GameCharacter;
            bool killerIsPlayer = killerCharacter != null && killerCharacter.Owner != null && killerCharacter.Owner.IsValid;
            bool credited = killerIsPlayer && killerCharacter.Owner != owner;
            BroadcastKill(owner, character, credited ? killerCharacter.Owner : null, killerIsPlayer ? null : killerCharacter, source);
            if (phase != CTF_Phase.Playing) return;
            PlayerProgress.Modify<CTF_SaveData>(owner, save => save.deaths++);
            if (credited && PlayerData.TryGetPlayerData(killerCharacter.Owner, out var p)) {
                RoundLeaderboard.Add(p, KillsColumn, 1);
                Award(killerCharacter.Owner, killXP, 0);
                PlayerProgress.Modify<CTF_SaveData>(p, save => save.kills++);
            }
        }

        // ---------------------------------------------------------------- combat feedback
        void ReportHit(IEntity attacker, GameCharacter victim, long damage, bool killed) {
            if (victim == null || attacker is not GameCharacter shooter || shooter.Owner == null || !shooter.Owner.IsValid) return;
            InstanceFinder.ServerManager.Broadcast(new CTF_HitBroadcast {
                attackerId = shooter.Owner.ClientId,
                victimId = victim.OwnerId,
                position = victim.transform.position,
                damage = (int)Math.Min(damage, int.MaxValue),
                killed = killed
            });
        }

        /// <summary>
        /// Tells everyone who eliminated whom. <paramref name="killer"/> is the credited player, if any;
        /// <paramref name="otherKiller"/> a character that is not a player (a bot), named but never credited.
        /// </summary>
        void BroadcastKill(NetworkConnection victim, GameCharacter victimCharacter, NetworkConnection killer, GameCharacter otherKiller, DamageType source) {
            InstanceFinder.ServerManager.Broadcast(new CTF_KillBroadcast {
                killerId = killer != null ? killer.ClientId : -1,
                killerName = killer != null ? PlayerData.GetPlayerName(killer) : otherKiller != null ? otherKiller.DisplayName : "",
                killerTeam = TeamOf(killer, otherKiller),
                victimId = victim.ClientId,
                victimName = PlayerData.GetPlayerName(victim) ?? (victimCharacter != null ? victimCharacter.DisplayName : ""),
                victimTeam = TeamOf(victim, victimCharacter),
                cause = Cause(source),
                respawnSeconds = ServerPlayerCharacter.RespawnTime
            });
        }

        /// <summary>A player's team from the roster, otherwise the character's own team colour.</summary>
        CTF_Team TeamOf(NetworkConnection connection, GameCharacter character) {
            if (connection != null && teams.TryGetValue(connection, out var team)) return team;
            return character != null && character.GetTeam().realTeam == TeamColor.Blue ? CTF_Team.Blue : CTF_Team.Red;
        }

        static CTF_KillCause Cause(DamageType source) => source switch {
            DamageType.Gun => CTF_KillCause.Gun,
            DamageType.Melee => CTF_KillCause.Melee,
            DamageType.Fall or DamageType.Despawn => CTF_KillCause.Fall,
            _ => CTF_KillCause.Other
        };

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
            ResetPowerUps();
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
            float end = Time.time + matchSeconds;
            while (true) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(end - Time.time));
                if (secondsLeft == 0 || redScore >= capturesToWin || blueScore >= capturesToWin) break;
                headline = $"RED {redScore}  -  {blueScore} BLUE";
                // The scoreboard shows the score and clock; the shared top pill stays out of the way.
                SetTopMessage("");
                await AwaitTime(500, token);
            }

            // ---- results
            phase = CTF_Phase.Results;
            SetGlobalInvul(true);
            foreach (var flag in flags) Return(flag, false);
            ClearBoosts();
            var winner = CTF_Rules.Winner(redScore, blueScore);
            headline = winner is CTF_Team w ? $"{CTF_Rules.Name(w)} TEAM WINS {Math.Max(redScore, blueScore)}-{Math.Min(redScore, blueScore)}!" : $"DRAW {redScore}-{blueScore}";
            SetMusic(MusicSelection.VictoryMusic);
            foreach (var pair in teams.Where(t => t.Key.IsActive)) {
                bool won = winner is CTF_Team team && pair.Value == team;
                if (won) Award(pair.Key, winXP, winGold);
                PlayerProgress.Modify<CTF_SaveData>(pair.Key, save => {
                    save.matchesPlayed++;
                    if (won) save.wins++;
                });
            }
            if (winner is CTF_Team wonTeam) Event(CTF_EventKind.Won, wonTeam, null);
            else Event(CTF_EventKind.Draw, CTF_Team.Red, null);
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                // The banner names the winner and the score; the top pill only counts down.
                SetTopMessage($"Next match in {i}");
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
            ExpireBoosts();
            if (phase != CTF_Phase.Playing) {
                if (Time.time > nextSnapshot) { nextSnapshot = Time.time + 1f; BroadcastState(); }
                return;
            }
            foreach (var flag in flags) UpdateFlag(flag);
            UpdatePowerUps();
            if (Time.time > nextSnapshot) { nextSnapshot = Time.time + 1f / snapshotsPerSecond; BroadcastState(); }
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
                    RefreshSpeed(pair.Key);
                    Event(CTF_EventKind.Taken, flag.team, pair.Key);
                    return;
                }
                if (flag.state == CTF_FlagState.Dropped) {
                    Award(pair.Key, returnXP, 0);
                    PlayerProgress.Modify<CTF_SaveData>(pair.Key, save => save.returns++);
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
            Return(flag, false);
        }

        void Drop(Flag flag, Vector3 at) {
            if (flag.state != CTF_FlagState.Carried) return;
            var carrier = flag.carrier;
            flag.carrier = null;
            RefreshSpeed(carrier);
            if (map != null && at.y < map.killY + 2f) { Return(flag, true); return; }
            flag.state = CTF_FlagState.Dropped;
            flag.position = at;
            flag.droppedAt = Time.time;
            Event(CTF_EventKind.Dropped, flag.team, carrier);
        }

        void Return(Flag flag, bool announce) {
            var carrier = flag.carrier;
            flag.state = CTF_FlagState.Home;
            flag.carrier = null;
            if (carrier != null) RefreshSpeed(carrier);
            if (map != null) flag.position = map.Stand(flag.team);
            if (announce) Event(CTF_EventKind.Returned, flag.team, null);
        }

        bool IsCarrying(NetworkConnection connection) {
            foreach (var flag in flags)
                if (flag.state == CTF_FlagState.Carried && flag.carrier == connection) return true;
            return false;
        }

        /// <summary>Applies the carrier slow-down and any running speed boost to a player's walk/sprint speed.</summary>
        void RefreshSpeed(NetworkConnection connection) {
            if (connection == null || !PlayerData.TryGetPlayerData(connection, out var player)) return;
            bool boosted = boostedUntil.TryGetValue(connection, out float until) && Time.time < until;
            float m = CTF_Rules.SpeedMultiplier(IsCarrying(connection), boosted, speedBoostMultiplier);
            player.walkSpeed.Value = walkSpeed * m;
            player.sprintSpeed.Value = sprintSpeed * m;
        }

        // ---------------------------------------------------------------- power-ups
        void ResetPowerUps() {
            int count = map.powerUps.Length;
            powerUpReadyAt = new float[count];
            powerUpsReady = 0;
            for (int i = 0; i < count; i++) powerUpsReady = CTF_Rules.WithReady(powerUpsReady, i, true);
            ClearBoosts();
        }

        void UpdatePowerUps() {
            var spots = map.powerUps;
            for (int i = 0; i < spots.Length; i++) {
                if (!CTF_Rules.IsReady(powerUpsReady, i)) {
                    if (Time.time >= powerUpReadyAt[i]) powerUpsReady = CTF_Rules.WithReady(powerUpsReady, i, true);
                    continue;
                }
                foreach (var pair in LocalCharacter.Characters) {
                    var character = pair.Value;
                    if (character == null || character.IsDead || !teams.ContainsKey(pair.Key)) continue;
                    if (!CTF_Rules.InReach(character.transform.position, spots[i].Position, CTF_Rules.PowerUpRange)) continue;
                    if (!TryApply(spots[i].kind, pair.Key, character)) continue;
                    powerUpsReady = CTF_Rules.WithReady(powerUpsReady, i, false);
                    powerUpReadyAt[i] = Time.time + powerUpRespawnSeconds;
                    PlayerProgress.Modify<CTF_SaveData>(pair.Key, save => save.powerUps++);
                    InstanceFinder.ServerManager.Broadcast(new CTF_PowerUpBroadcast {
                        index = i, kind = spots[i].kind, playerId = pair.Key.ClientId,
                        seconds = spots[i].kind == CTF_PowerUpKind.Speed ? speedBoostSeconds : 0f
                    });
                    BroadcastState();
                    break;
                }
            }
        }

        bool TryApply(CTF_PowerUpKind kind, NetworkConnection connection, LocalCharacter character) {
            switch (kind) {
                case CTF_PowerUpKind.Health: {
                    long heal = CTF_Rules.HealAmount(character.Health.Value, character.MaxHealth.Value, healAmount);
                    if (heal <= 0) return false; // full health: leave it for someone who needs it
                    character.HealHealth(heal);
                    return true;
                }
                case CTF_PowerUpKind.Speed:
                    boostedUntil[connection] = Time.time + speedBoostSeconds;
                    RefreshSpeed(connection);
                    return true;
                default:
                    Debug.LogWarning($"Flag Wars has no rule for power-up kind {kind}.", this);
                    return false;
            }
        }

        void ExpireBoosts() {
            if (boostedUntil.Count == 0) return;
            expiredBoosts.Clear();
            foreach (var pair in boostedUntil)
                if (Time.time >= pair.Value) expiredBoosts.Add(pair.Key);
            foreach (var connection in expiredBoosts) {
                boostedUntil.Remove(connection);
                RefreshSpeed(connection);
            }
        }

        void ClearBoosts() {
            expiredBoosts.Clear();
            expiredBoosts.AddRange(boostedUntil.Keys);
            boostedUntil.Clear();
            foreach (var connection in expiredBoosts) RefreshSpeed(connection);
        }

        void Event(CTF_EventKind kind, CTF_Team flag, NetworkConnection player) {
            InstanceFinder.ServerManager.Broadcast(new CTF_EventBroadcast {
                kind = kind, flag = flag,
                playerName = player != null ? PlayerData.GetPlayerName(player) : "",
                playerId = player != null ? player.ClientId : -1,
                redScore = redScore, blueScore = blueScore
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
            redFlag = Snap(flags[0]), blueFlag = Snap(flags[1]), headline = headline,
            powerUpsReady = powerUpsReady,
            sequence = ++stateSequence,
            capturesToWin = capturesToWin
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
            ServerPlayerCharacter.RespawnTime = previousRespawnTime;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (ServerPlayerCharacter.SpawnRotationFunction == SpawnRotation) ServerPlayerCharacter.SpawnRotationFunction = null;
            InstanceFinder.ServerManager?.UnregisterBroadcast<CTF_StateRequest>(OnStateRequest);
            base.OnDestroy();
        }
    }
}
