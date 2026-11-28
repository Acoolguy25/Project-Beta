using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
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
    /// <para>
    /// The other parts of the runner live beside this file: role abilities
    /// (<c>HS_ServerRunner.Abilities.cs</c>), round points, MVP, streaks and the retention kit
    /// (<c>HS_ServerRunner.Scoring.cs</c>), mid-round map events (<c>HS_ServerRunner.Events.cs</c>), the
    /// hiding spots' doors (<c>HS_ServerRunner.Doors.cs</c>) and the pumpkin hunt (<c>HS_ServerRunner.Pumpkins.cs</c>).
    /// </para>
    /// </summary>
    public sealed partial class HS_ServerRunner : ServerRunner {
        [Header("Round")]
        [SerializeField, Min(1)] int minPlayers = 2;
        [SerializeField, Min(1)] int intermissionSeconds = 10;
        [SerializeField, Min(5)] int hideSeconds = 30;
        [SerializeField, Min(20)] int seekSeconds = 150;
        [Tooltip("Results length: the survivors' names, the results card and the match summary stay up this long.")]
        [SerializeField, Min(3)] int resultsSeconds = 14;
        [Tooltip("Seconds left in the seek phase when pings start, and how often they repeat.")]
        [SerializeField, Min(0)] int pingFrom = 60;
        [SerializeField, Min(5)] int pingEvery = 15;
        [SerializeField, Min(0.1f)] float tagCooldown = 0.6f;
        [Tooltip("A tag reaches HS_Rules.TagRange plus this (world units) from the seeker's eyes to the hider's chest (Abilities: eye and target heights), " +
                 "so a tag the client's prompt offers at TagRange (measured at the feet) is never refused for the height difference.")]
        [SerializeField, Min(0f)] float tagReachSlack = 0.6f;
        [Tooltip("Milliseconds between two updates of the seek phase (timer, points, events).")]
        [SerializeField, Min(50)] int seekTickMs = 500;
        [Header("Movement")]
        [SerializeField] float hiderWalk = 13f, hiderSprint = 20f, seekerWalk = 14.5f, seekerSprint = 22f;
        [SerializeField, Min(0f)] float jumpHeight = 1.4f, staminaMax = 160f, staminaRegen = 25f;
        [Header("Taunts (cooldown and allowance: HS_Rules; points: Scoring)")]
        [Tooltip("World units, measured straight through walls; a seeker this close makes a taunt brave. About three robot heights, " +
                 "so a seeker on another floor or across the house does not count.")]
        [SerializeField, Min(0f)] float braveTauntRange = 20f;
        [Header("Daily twist (HS_Daily) and daily challenges")]
        [Tooltip("Days added to the clock that the daily challenges, the play streak and the featured event read. 0 in play; " +
                 "set it to preview another day (QA).")]
        [SerializeField] int previewDayOffset;

        const string PointsColumn = "Points";
        const string TagsColumn = "Tags";

        readonly Dictionary<NetworkConnection, HS_Role> roles = new();
        readonly Dictionary<NetworkConnection, int> timesSeeker = new();
        readonly Dictionary<NetworkConnection, float> lastTag = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Dictionary<NetworkConnection, float> lastTaunt = new();
        readonly Dictionary<NetworkConnection, int> tauntsThisRound = new();
        readonly System.Random random = new();
        HS_Map map;
        HS_Phase phase;
        int round, secondsLeft, phaseSeconds, hidersAtStart;
        float seekStartedAt;
        string headline = "";
        bool stateDirty;

        public HS_Phase Phase => phase;

        /// <summary>The server's clock for everything daily (the kit's challenges and streak, the featured event).</summary>
        DateTime Now => DateTime.UtcNow.AddDays(previewDayOffset);

        /// <summary>Today's featured event (<see cref="HS_Daily"/>).</summary>
        HS_RoundEvent FeaturedToday => HS_Daily.Today(Now);

        /// <summary>The share of the seek phase gone: 0 while the hiders hide, 1 at the end.</summary>
        float SeekShareGone => phase == HS_Phase.Seeking && seekSeconds > 0 ? Mathf.Clamp01((Time.time - seekStartedAt) / seekSeconds) : 0f;

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -40f;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.RespawnTime = 2f;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            abilityTuning.Validate();
            scoring.Validate();
            events.Validate();
            doorTuning.Validate();
            pumpkinTuning.Validate();
            retention = new RetentionService<HS_SaveData>(HS_Retention.Rules(retentionTuning), save => save.retention, () => Now);
            retention.Unlocked += OnUnlocked;
            retention.Ready += OnRetentionReady;
            var server = InstanceFinder.ServerManager;
            server.RegisterBroadcast<HS_TagRequest>(OnTagRequest);
            server.RegisterBroadcast<HS_StateRequest>(OnStateRequest);
            server.RegisterBroadcast<HS_TauntRequest>(OnTauntRequest);
            server.RegisterBroadcast<HS_AbilityRequest>(OnAbilityRequest);
            server.RegisterBroadcast<HS_DoorRequest>(OnDoorRequest);
            // A disconnect is reported before the player is despawned, while their document is still loaded.
            server.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        /// <summary>A player's name, or a stand-in when their data is already gone (they left this frame).</summary>
        static string NameOf(NetworkConnection c) => PlayerData.GetPlayerName(c) ?? $"Player {c.ClientId}";

        HS_Role RoleOf(NetworkConnection c) => roles.TryGetValue(c, out var r) ? r : HS_Role.None;
        static bool Seeks(HS_Role r) => r is HS_Role.Seeker or HS_Role.Caught;
        int HidersLeft => roles.Count(r => r.Value == HS_Role.Hider && r.Key.IsActive);

        /// <summary>The live character of a connection, or false when it has none (not spawned, or dead).</summary>
        static bool TryGetLiveCharacter(NetworkConnection c, out LocalCharacter character) =>
            LocalCharacter.Characters.TryGetValue(c, out character) && character != null && !character.IsDead;

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
            bool midRound = phase is HS_Phase.Hiding or HS_Phase.Seeking;
            roles[player.Owner] = midRound ? HS_Role.Seeker : HS_Role.None;
            PlayerProgress.Load<HS_SaveData>(player, HS_SaveData.Universe).Forget();
            retention.PlayerJoined(player);
            if (midRound) JoinRoundScoring(player.Owner);
            ApplyMovement(player);
            ApplyTeam(player);
            SendAbilityState(player.Owner);
            SendRunningBlends(player.Owner);
            MarkDirty();
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            character.CanSpectate.Value = false;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) { ApplyMovement(player); ApplyTeam(player); }
        }

        void OnRemoteConnectionState(NetworkConnection c, FishNet.Transporting.RemoteConnectionStateArgs args) {
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped) RecordLeaver(c);
        }

        void OnPlayerLeft(PlayerData player) {
            NetworkConnection c = player.Owner;
            // Normally recorded at the disconnect already; a player removed another way is recorded here.
            RecordLeaver(c);
            roles.Remove(c);
            lastTag.Remove(c);
            requestTimes.Remove(c);
            lastTaunt.Remove(c);
            tauntsThisRound.Remove(c);
            ForgetAbilities(c);
            ForgetScoring(c);
            ForgetDoors(c);
            ForgetPumpkins(c);
            MarkDirty();
        }

        void ApplyMovement(PlayerData player) {
            NetworkConnection c = player.Owner;
            HS_Role role = RoleOf(c);
            bool seeker = Seeks(role);
            bool rooted = (phase == HS_Phase.Hiding && seeker) || IsRooted(c);
            float scale = SpeedScaleOf(c, role);
            player.walkSpeed.Value = rooted ? 0f : (seeker ? seekerWalk : hiderWalk) * scale;
            player.sprintSpeed.Value = rooted ? 0f : (seeker ? seekerSprint : hiderSprint) * scale;
            player.jumpHeight.Value = rooted ? 0f : jumpHeight;
            player.staminaMax.Value = staminaMax;
            player.staminaRegen.Value = staminaRegen;
        }

        void ApplyMovement(NetworkConnection c) {
            if (PlayerData.TryGetPlayerData(c, out var player)) ApplyMovement(player);
        }

        void ApplyTeam(PlayerData player) {
            HS_Role role = RoleOf(player.Owner);
            TeamColor team = role == HS_Role.Hider ? TeamColor.Green : Seeks(role) ? TeamColor.Red : TeamColor.White;
            if (player.team.Value == null || player.team.Value.realTeam != team) player.SetPlayerTeam(new TeamConfig(team));
            if (player.TryGetCharacter(out var character) && character.GetTeam().realTeam != team) character.SetTeam(new TeamConfig(team));
        }

        /// <summary>Re-applies everyone's movement, team and ability bar (after a phase or role change).</summary>
        void ApplyAll() {
            foreach (var player in PlayerData.Players.Values) {
                ApplyMovement(player);
                ApplyTeam(player);
                SendAbilityState(player.Owner);
            }
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
            SetLeaderboardEnabled(PointsColumn, true);
            SetLeaderboardEnabled(TagsColumn, true);
            SetMusic(MusicSelection.GameMusic);
            ResetAbilities();
            ResetDoors();
            ResetPumpkins();
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
            seekStartedAt = float.MaxValue;
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
            lastTaunt.Clear();
            tauntsThisRound.Clear();
            ResetAbilities();
            ResetDoors();
            ResetPumpkins();
            BeginRoundScoring();

            // ---- hide
            phase = HS_Phase.Hiding;
            phaseSeconds = hideSeconds;
            // The HUD's round card shows the phase and timer from here on.
            SetTopMessage("");
            ApplyAll();
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            string seekerNames = string.Join(", ", roles.Where(r => r.Value == HS_Role.Seeker).Select(r => NameOf(r.Key)));
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
            seekStartedAt = Time.time;
            ApplyAll();
            SetMusic(MusicSelection.HorrorChaseMusic);
            float start = Time.time, end = start + seekSeconds, lastTick = start;
            PlanEvent(seekSeconds);
            StartPumpkins();
            int nextPing = pingFrom;
            while (true) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(end - Time.time));
                if (secondsLeft == 0 || HidersLeft == 0 || !roles.Any(r => Seeks(r.Value) && r.Key.IsActive)) break;
                if (pingFrom > 0 && secondsLeft <= nextPing) { Ping(); nextPing = secondsLeft - pingEvery; }
                TickScoring(Time.time - lastTick);
                TickEvent(Time.time - start);
                lastTick = Time.time;
                headline = $"SEEK! {HidersLeft}/{hidersAtStart} hiders left";
                BroadcastState();
                await AwaitTime(seekTickMs, token);
            }

            // ---- results
            StopEvent();
            ResetPumpkins();
            ResetAbilities();
            phase = HS_Phase.Results;
            phaseSeconds = resultsSeconds;
            int survivors = HidersLeft;
            bool hidersWin = survivors > 0;
            headline = hidersWin ? $"HIDERS WIN! {survivors} stayed hidden." : "SEEKERS WIN! Everyone was found.";
            ApplyAll();
            RevealHidingSpots();
            SetMusic(MusicSelection.VictoryMusic);
            FinishRound(hidersWin);
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
            if (!TryGetLiveCharacter(connection, out var seeker)) return;
            Vector3 eye = seeker.transform.position + Vector3.up * abilityEyeHeight;
            NetworkConnection best = null;
            float bestDistance = float.MaxValue;
            foreach (var pair in roles) {
                if (pair.Value != HS_Role.Hider || !LocalCharacter.Characters.TryGetValue(pair.Key, out var hider) || hider == null) continue;
                Vector3 target = hider.transform.position + Vector3.up * abilityTargetHeight;
                float d = Vector3.Distance(eye, target);
                if (d > HS_Rules.TagRange + tagReachSlack + Mathf.Abs(abilityEyeHeight - abilityTargetHeight) || d >= bestDistance) continue;
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
            // The assist is judged on the dazzle, so read it before the found hider's effects end.
            NetworkConnection assister = FlashAssister(best, connection);
            // A found hider loses whatever they had running (a blend ends where everyone can see it).
            EndEffects(best);
            if (PlayerData.TryGetPlayerData(best, out var caught)) { ApplyMovement(caught); ApplyTeam(caught); }
            SendAbilityState(best);
            if (PlayerData.TryGetPlayerData(connection, out var tagger)) RoundLeaderboard.Add(tagger, TagsColumn, 1);
            PlayerProgress.Modify<HS_SaveData>(connection, save => save.tags++);
            string seekerName = NameOf(connection), hiderName = NameOf(best);
            InstanceFinder.ServerManager.Broadcast(new HS_TagBroadcast {
                seekerId = connection.ClientId, hiderId = best.ClientId, seekerName = seekerName, hiderName = hiderName, hidersLeft = HidersLeft
            });
            ServerChat.SendSystemMessage(new($"{seekerName} found {hiderName}! {HidersLeft} left.", SystemMessageSource.CustomMessage));
            OnTagScored(connection, best, assister);
            MarkDirty();
        }

        // ---------------------------------------------------------------- taunts and reveal
        void OnTauntRequest(NetworkConnection connection, HS_TauntRequest request, Channel channel) {
            if (!connection.IsAuthenticated || request.round != round) return;
            float since = lastTaunt.TryGetValue(connection, out float at) ? Time.time - at : float.MaxValue;
            int used = tauntsThisRound.TryGetValue(connection, out int n) ? n : 0;
            int allowance = TauntAllowance(connection);
            string refusal = HS_Rules.TauntRefusal(phase, RoleOf(connection), since, used, allowance);
            if (refusal != null) {
                // Say why, and resend the count so a HUD that missed a confirmation (reconnect, late state) corrects itself.
                InstanceFinder.ServerManager.Broadcast(connection, new HS_ResultBroadcast { accepted = false, message = refusal });
                if (used > 0) InstanceFinder.ServerManager.Broadcast(connection, new HS_TauntBroadcast { clientId = connection.ClientId, used = used, correction = true });
                return;
            }
            if (!TryGetLiveCharacter(connection, out var hider)) return;
            lastTaunt[connection] = Time.time;
            tauntsThisRound[connection] = used + 1;
            Vector3 position = hider.transform.position;
            int points = HS_Rules.TauntReward(NearestSeekerDistance(position), braveTauntRange, scoring.tauntPoints, scoring.braveTauntPoints, out bool brave);
            // The taunt's own popup shows what it paid, so the points arrive without a second popup.
            AddPoints(connection, points, brave ? "Brave taunt" : "Taunt", false);
            if (brave) retention.Count(connection, HS_Retention.Stats.BraveTaunts);
            PlayerProgress.Modify<HS_SaveData>(connection, save => save.RecordTaunt(brave));
            InstanceFinder.ServerManager.Broadcast(connection, new HS_TauntBroadcast { clientId = connection.ClientId, position = position, xp = scoring.Xp(points), brave = brave, used = used + 1 });
            BroadcastTauntToOthers(connection, position);
        }

        /// <summary>
        /// What everyone but the taunter hears and sees: only who and where. A decoy sends exactly this too,
        /// so nothing in the message tells the two apart.
        /// </summary>
        void BroadcastTauntToOthers(NetworkConnection source, Vector3 position) {
            var others = new HashSet<NetworkConnection>();
            foreach (PlayerData player in PlayerData.Players.Values)
                if (player.Owner != source && player.Owner.IsActive) others.Add(player.Owner);
            if (others.Count > 0) InstanceFinder.ServerManager.Broadcast(others, new HS_TauntBroadcast { clientId = source.ClientId, position = position });
        }

        float NearestSeekerDistance(Vector3 position) {
            float best = float.MaxValue;
            foreach (var pair in roles) {
                if (!Seeks(pair.Value) || !TryGetLiveCharacter(pair.Key, out var seeker)) continue;
                best = Mathf.Min(best, Vector3.Distance(position, seeker.transform.position));
            }
            return best;
        }

        /// <summary>Everyone sees where the survivors hid once the round is over.</summary>
        void RevealHidingSpots() {
            var spots = roles.Where(r => r.Value == HS_Role.Hider && r.Key.IsActive && LocalCharacter.Characters.TryGetValue(r.Key, out var c) && c != null)
                .Select(r => new HS_HidingSpot { clientId = r.Key.ClientId, name = NameOf(r.Key), position = LocalCharacter.Characters[r.Key].transform.position })
                .ToArray();
            InstanceFinder.ServerManager.Broadcast(new HS_RevealBroadcast { round = round, spots = spots, seconds = resultsSeconds });
        }

        /// <summary>Late in a round every seeker gets a blurred fix on each hider; blended hiders are missed and live decoys show up too.</summary>
        void Ping() {
            var positions = new List<Vector3>();
            foreach (var pair in roles)
                if (pair.Value == HS_Role.Hider && !IsBlended(pair.Key) && LocalCharacter.Characters.TryGetValue(pair.Key, out var hider) && hider != null)
                    positions.Add(HS_Rules.Blur(hider.transform.position, random));
            AddDecoyPositions(positions, null, 0f, HS_Rules.PingScatter);
            SendPing(positions, "PING! The seekers know roughly where you are. Move!", pair => pair.Value == HS_Role.Hider);
        }

        /// <summary>Sends <paramref name="positions"/> to the seekers and a warning to the hiders <paramref name="warn"/> picks.</summary>
        void SendPing(List<Vector3> positions, string warning, Func<KeyValuePair<NetworkConnection, HS_Role>, bool> warn) {
            if (positions.Count == 0) return;
            var seekers = roles.Where(r => Seeks(r.Value) && r.Key.IsActive).Select(r => r.Key).ToHashSet();
            InstanceFinder.ServerManager.Broadcast(seekers, new HS_PingBroadcast { positions = positions.ToArray() });
            foreach (var hider in roles.Where(r => r.Key.IsActive && warn(r)))
                InstanceFinder.ServerManager.Broadcast(hider.Key, new HS_ResultBroadcast { accepted = true, message = warning });
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
            SendAbilityState(connection);
        }

        void MarkDirty() => stateDirty = true;

        void Update() {
            TickAbilities();
            TickPumpkins();
            if (stateDirty && map != null) BroadcastState();
        }

        HS_StateBroadcast MakeState() => new() {
            phase = phase, round = round, secondsLeft = secondsLeft, phaseSeconds = phaseSeconds, hidersLeft = HidersLeft, hidersAtStart = hidersAtStart, headline = headline,
            roles = roles.Where(r => r.Key.IsActive).Select(r => new HS_PlayerRole { clientId = r.Key.ClientId, role = r.Value, name = NameOf(r.Key) }).ToArray(),
            activeEvent = activeEvent, eventSecondsLeft = EventSecondsLeft, featuredEvent = FeaturedToday,
            closedDoors = ClosedDoors, pumpkins = LivePumpkins,
        };

        void BroadcastState() {
            stateDirty = false;
            InstanceFinder.ServerManager.Broadcast(MakeState());
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
                server.UnregisterBroadcast<HS_TauntRequest>(OnTauntRequest);
                server.UnregisterBroadcast<HS_AbilityRequest>(OnAbilityRequest);
                server.UnregisterBroadcast<HS_DoorRequest>(OnDoorRequest);
                server.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
            if (retention != null) {
                retention.Unlocked -= OnUnlocked;
                retention.Ready -= OnRetentionReady;
                retention.Dispose();
            }
            base.OnDestroy();
        }
    }
}
