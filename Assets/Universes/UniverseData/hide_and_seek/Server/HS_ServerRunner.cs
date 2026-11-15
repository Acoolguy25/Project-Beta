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
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.hide_and_seek.Server {
    /// <summary>
    /// Hide and seek: hiders scatter while seekers wait blindfolded, then seekers tag hiders (who join
    /// the seekers). Hiders win if anyone is still hidden when time runs out. Roles, tags, the
    /// blindfold freeze and late-round pings are all decided here.
    /// </summary>
    public sealed class HS_ServerRunner : ServerRunner {
        [Header("Round")]
        [SerializeField, Min(1)] int minPlayers = 2;
        [SerializeField, Min(1)] int intermissionSeconds = 10;
        [SerializeField, Min(5)] int hideSeconds = 30;
        [SerializeField, Min(20)] int seekSeconds = 150;
        [SerializeField, Min(3)] int resultsSeconds = 8;
        [Tooltip("Seconds left in the seek phase when pings start, and how often they repeat.")]
        [SerializeField, Min(0)] int pingFrom = 60;
        [SerializeField, Min(5)] int pingEvery = 15;
        [SerializeField, Min(0.1f)] float tagCooldown = 0.6f;
        [Header("Movement")]
        [SerializeField] float hiderWalk = 13f, hiderSprint = 20f, seekerWalk = 14.5f, seekerSprint = 22f;
        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong tagXP = 15, surviveXP = 40, surviveGold = 15, seekerWinXP = 20;

        const string TagsColumn = "Tags";
        const string SurvivedColumn = "Survived";

        readonly Dictionary<NetworkConnection, HS_Role> roles = new();
        readonly Dictionary<NetworkConnection, int> timesSeeker = new();
        readonly Dictionary<NetworkConnection, float> lastTag = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly System.Random random = new();
        HS_Map map;
        HS_Phase phase;
        int round, secondsLeft, phaseSeconds, hidersAtStart;
        string headline = "";
        bool stateDirty;

        public HS_Phase Phase => phase;

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -40f;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.RespawnTime = 2f;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            var server = InstanceFinder.ServerManager;
            server.RegisterBroadcast<HS_TagRequest>(OnTagRequest);
            server.RegisterBroadcast<HS_StateRequest>(OnStateRequest);
        }

        HS_Role RoleOf(NetworkConnection c) => roles.TryGetValue(c, out var r) ? r : HS_Role.None;
        static bool Seeks(HS_Role r) => r is HS_Role.Seeker or HS_Role.Caught;
        int HidersLeft => roles.Count(r => r.Value == HS_Role.Hider && r.Key.IsActive);

        Vector3 SpawnPosition(NetworkConnection connection) {
            if (map == null) return Vector3.up * 2f;
            Vector3 p = phase is HS_Phase.Hiding or HS_Phase.Seeking
                ? Seeks(RoleOf(connection)) || RoleOf(connection) == HS_Role.None ? map.seekerSpawn.position : map.hidePoints[random.Next(map.hidePoints.Length)]
                : map.lobbySpawn.position;
            return map.Scatter(p) + Vector3.up * 1.2f;
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            player.cameraTypes.Clear();
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            player.cameraTypes.Add(GameCameraType.FirstPersonCamera);
            // Joining mid-round makes you a seeker, so nobody can join just to hide.
            roles[player.Owner] = phase is HS_Phase.Hiding or HS_Phase.Seeking ? HS_Role.Seeker : HS_Role.None;
            ApplyMovement(player);
            ApplyTeam(player);
            MarkDirty();
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            character.CanSpectate.Value = false;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) { ApplyMovement(player); ApplyTeam(player); }
        }

        void OnPlayerLeft(PlayerData player) {
            roles.Remove(player.Owner);
            lastTag.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
            MarkDirty();
        }

        void ApplyMovement(PlayerData player) {
            HS_Role role = RoleOf(player.Owner);
            bool frozen = phase == HS_Phase.Hiding && Seeks(role);
            bool seeker = Seeks(role);
            player.walkSpeed.Value = frozen ? 0f : seeker ? seekerWalk : hiderWalk;
            player.sprintSpeed.Value = frozen ? 0f : seeker ? seekerSprint : hiderSprint;
            player.jumpHeight.Value = frozen ? 0f : 1.4f;
            player.staminaMax.Value = 160f;
            player.staminaRegen.Value = 25f;
        }

        void ApplyTeam(PlayerData player) {
            HS_Role role = RoleOf(player.Owner);
            TeamColor team = role == HS_Role.Hider ? TeamColor.Green : Seeks(role) ? TeamColor.Red : TeamColor.White;
            if (player.team.Value == null || player.team.Value.realTeam != team) player.SetPlayerTeam(new TeamConfig(team));
            if (player.TryGetCharacter(out var character) && character.GetTeam().realTeam != team) character.SetTeam(new TeamConfig(team));
        }

        void ApplyAll() {
            foreach (var player in PlayerData.Players.Values) { ApplyMovement(player); ApplyTeam(player); }
            MarkDirty();
        }

        // ---------------------------------------------------------------- round
        protected override async UniTask StartAsync(CancellationToken token) {
            phase = HS_Phase.Waiting;
            await WaitForSceneAsync("hide_and_seek_start", token);
            map = FindAnyObjectByType<HS_Map>();
            if (map == null || !map.IsConfigured) throw new InvalidOperationException("Hide and seek requires a configured HS_Map in hide_and_seek_start.");
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            SetLeaderboardEnabled(TagsColumn, true);
            SetLeaderboardEnabled(SurvivedColumn, true);
            SetMusic(MusicSelection.GameMusic);
            foreach (var key in roles.Keys.ToList()) roles[key] = HS_Role.None;
            ApplyAll();
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();

            while (GetActivePlayersCount() < minPlayers) {
                headline = $"Waiting for players ({GetActivePlayersCount()}/{minPlayers})";
                SetTopMessage(headline);
                BroadcastState();
                await AwaitTime(1000, token);
            }

            phase = HS_Phase.Intermission;
            phaseSeconds = intermissionSeconds;
            for (int i = intermissionSeconds; i > 0; i--) {
                secondsLeft = i;
                headline = $"Round {round + 1} starts in {i}";
                SetTopMessage(headline);
                BroadcastState();
                await AwaitTime(1000, token);
            }

            // ---- roles
            round++;
            var active = GetActivePlayers().Select(p => p.Owner).ToList();
            int seekers = TeamSplit.MinorityCount(active.Count, HS_Rules.PlayersPerSeeker);
            var picked = TeamSplit.PickRotating(active.Count, seekers, random, i => timesSeeker.TryGetValue(active[i], out int n) ? n : 0);
            roles.Clear();
            for (int i = 0; i < active.Count; i++) {
                bool seeker = picked.Contains(i);
                roles[active[i]] = seeker ? HS_Role.Seeker : HS_Role.Hider;
                if (seeker) timesSeeker[active[i]] = (timesSeeker.TryGetValue(active[i], out int n) ? n : 0) + 1;
            }
            foreach (var player in PlayerData.Players.Values) if (!roles.ContainsKey(player.Owner)) roles[player.Owner] = HS_Role.Seeker;
            hidersAtStart = HidersLeft;

            // ---- hide
            phase = HS_Phase.Hiding;
            phaseSeconds = hideSeconds;
            // The HUD's round card shows the phase and timer from here on.
            SetTopMessage("");
            ApplyAll();
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            string seekerNames = string.Join(", ", roles.Where(r => r.Value == HS_Role.Seeker).Select(r => PlayerData.GetPlayerName(r.Key)));
            ServerChat.SendSystemMessage(new($"Round {round}: {seekerNames} {(seekers == 1 ? "is" : "are")} seeking. Hide!", SystemMessageSource.CustomMessage));
            for (int i = hideSeconds; i > 0; i--) {
                secondsLeft = i;
                headline = $"HIDE! Seekers are released in {i}";
                BroadcastState();
                await AwaitTime(1000, token);
            }

            // ---- seek
            phase = HS_Phase.Seeking;
            phaseSeconds = seekSeconds;
            ApplyAll();
            SetMusic(MusicSelection.HorrorChaseMusic);
            float end = Time.time + seekSeconds;
            int nextPing = pingFrom;
            while (true) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(end - Time.time));
                if (secondsLeft == 0 || HidersLeft == 0 || !roles.Any(r => Seeks(r.Value) && r.Key.IsActive)) break;
                if (pingFrom > 0 && secondsLeft <= nextPing) { Ping(); nextPing = secondsLeft - pingEvery; }
                headline = $"SEEK! {HidersLeft}/{hidersAtStart} hiders left";
                BroadcastState();
                await AwaitTime(500, token);
            }

            // ---- results
            phase = HS_Phase.Results;
            phaseSeconds = resultsSeconds;
            int survivors = HidersLeft;
            bool hidersWin = survivors > 0;
            headline = hidersWin ? $"HIDERS WIN! {survivors} stayed hidden." : "SEEKERS WIN! Everyone was found.";
            SetMusic(MusicSelection.VictoryMusic);
            foreach (var pair in roles.Where(r => r.Key.IsActive)) {
                if (pair.Value == HS_Role.Hider) {
                    Award(pair.Key, surviveXP, surviveGold);
                    if (PlayerData.TryGetPlayerData(pair.Key, out var p)) RoundLeaderboard.Add(p, SurvivedColumn, 1);
                } else if (!hidersWin) Award(pair.Key, seekerWinXP, 0);
            }
            ServerChat.SendSystemMessage(new(headline, SystemMessageSource.CustomMessage));
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                BroadcastState();
                await AwaitTime(1000, token);
            }
            SetMusic(MusicSelection.GameMusic);
        }

        // ---------------------------------------------------------------- tagging
        void OnTagRequest(NetworkConnection connection, HS_TagRequest request, Channel channel) {
            if (!connection.IsAuthenticated || phase != HS_Phase.Seeking || !Seeks(RoleOf(connection))) return;
            if (lastTag.TryGetValue(connection, out float at) && Time.time - at < tagCooldown) return;
            lastTag[connection] = Time.time;
            if (!LocalCharacter.Characters.TryGetValue(connection, out var seeker) || seeker == null || seeker.IsDead) return;
            Vector3 eye = seeker.transform.position + Vector3.up * 1.4f;
            NetworkConnection best = null;
            float bestDistance = float.MaxValue;
            foreach (var pair in roles) {
                if (pair.Value != HS_Role.Hider || !LocalCharacter.Characters.TryGetValue(pair.Key, out var hider) || hider == null) continue;
                Vector3 target = hider.transform.position + Vector3.up * 1f;
                float d = Vector3.Distance(eye, target);
                if (d > HS_Rules.TagRange + 0.6f || d >= bestDistance) continue;
                if (Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<LocalCharacter>() == null) continue;
                best = pair.Key;
                bestDistance = d;
            }
            if (best == null) {
                InstanceFinder.ServerManager.Broadcast(connection, new HS_ResultBroadcast { accepted = false, message = "Nobody in reach." });
                return;
            }
            roles[best] = HS_Role.Caught;
            if (PlayerData.TryGetPlayerData(best, out var caught)) { ApplyMovement(caught); ApplyTeam(caught); }
            if (PlayerData.TryGetPlayerData(connection, out var tagger)) RoundLeaderboard.Add(tagger, TagsColumn, 1);
            Award(connection, tagXP, 0);
            string seekerName = PlayerData.GetPlayerName(connection), hiderName = PlayerData.GetPlayerName(best);
            InstanceFinder.ServerManager.Broadcast(new HS_TagBroadcast {
                seekerId = connection.ClientId, hiderId = best.ClientId, seekerName = seekerName, hiderName = hiderName, hidersLeft = HidersLeft
            });
            ServerChat.SendSystemMessage(new($"{seekerName} found {hiderName}! {HidersLeft} left.", SystemMessageSource.CustomMessage));
            MarkDirty();
        }

        void Ping() {
            var positions = roles.Where(r => r.Value == HS_Role.Hider && LocalCharacter.Characters.ContainsKey(r.Key))
                .Select(r => HS_Rules.Blur(LocalCharacter.Characters[r.Key].transform.position, random)).ToArray();
            if (positions.Length == 0) return;
            var seekers = roles.Where(r => Seeks(r.Value) && r.Key.IsActive).Select(r => r.Key).ToHashSet();
            InstanceFinder.ServerManager.Broadcast(seekers, new HS_PingBroadcast { positions = positions });
            foreach (var hider in roles.Where(r => r.Value == HS_Role.Hider && r.Key.IsActive))
                InstanceFinder.ServerManager.Broadcast(hider.Key, new HS_ResultBroadcast { accepted = true, message = "PING! The seekers got a rough idea where you are. Move!" });
        }

        void FixedUpdate() {
            if (map == null) return;
            // Falling out of the map puts you back where your role starts.
            foreach (var pair in LocalCharacter.Characters)
                if (pair.Value != null && !pair.Value.IsDead && pair.Value.IsSpawned && pair.Value.transform.position.y < map.killY)
                    pair.Value.TeleportTo(SpawnPosition(pair.Key));
        }

        // ---------------------------------------------------------------- messages
        void OnStateRequest(NetworkConnection connection, HS_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            requestTimes[connection] = Time.unscaledTime;
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        void MarkDirty() => stateDirty = true;

        void Update() {
            if (stateDirty && map != null) BroadcastState();
        }

        HS_StateBroadcast MakeState() => new() {
            phase = phase, round = round, secondsLeft = secondsLeft, phaseSeconds = phaseSeconds, hidersLeft = HidersLeft, hidersAtStart = hidersAtStart, headline = headline,
            roles = roles.Where(r => r.Key.IsActive).Select(r => new HS_PlayerRole { clientId = r.Key.ClientId, role = r.Value, name = PlayerData.GetPlayerName(r.Key) }).ToArray()
        };

        void BroadcastState() {
            stateDirty = false;
            InstanceFinder.ServerManager.Broadcast(MakeState());
        }

        static void Award(NetworkConnection connection, ulong xp, ulong gold) {
            if ((xp != 0 || gold != 0) && !LevelsServer.AwardPlayerXPAndGold(connection, xp, gold))
                Debug.LogWarning($"Hide and seek could not award rewards to connection {connection.ClientId}.");
        }

        static void SetMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            phase = HS_Phase.Waiting;
            base.Reset();
        }

        protected override void OnDestroy() {
            GameCharacter.FallHeightOverride = null;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            var server = InstanceFinder.ServerManager;
            if (server != null) {
                server.UnregisterBroadcast<HS_TagRequest>(OnTagRequest);
                server.UnregisterBroadcast<HS_StateRequest>(OnStateRequest);
            }
            base.OnDestroy();
        }
    }
}
