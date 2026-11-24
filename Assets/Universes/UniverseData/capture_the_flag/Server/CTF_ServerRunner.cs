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

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// Flag Wars: red vs blue. Run over the enemy flag to take it, bring it to your own stand while your
    /// flag is home to score. Touch your dropped flag to send it home. Mid-lane power-ups heal or speed
    /// you up. Empty slots are filled with server-driven bots (<see cref="CTF_BotSquad"/>), so one player
    /// gets a full match. Levels unlock guns and perks; kills earn medals; the match ends on the shared
    /// retention summary. Everything is decided here; clients only draw the broadcasts.
    /// <para>This file: setup, players, the match loop and the messages. Flags and power-ups are in
    /// <c>CTF_ServerRunner.Flags.cs</c>, kills and medals in <c>CTF_ServerRunner.Combat.cs</c>, XP and the
    /// match-end summary in <c>CTF_ServerRunner.Rewards.cs</c>.</para>
    /// </summary>
    public sealed partial class CTF_ServerRunner : ServerRunner, CTF_IBotWorld, CTF_BotSquad.IHost {
        [Header("Round")]
        [Tooltip("Players needed before a match starts. With bots on, 1 is enough: bots take the empty slots.")]
        [SerializeField, Min(1)] int minHumans = 1;
        [Tooltip("Seconds the first match waits for friends once enough players are in (skipped when the teams are already full).")]
        [SerializeField, Min(0)] int lobbySeconds = 12;
        [SerializeField, Min(1)] int countdownSeconds = 8;
        [Tooltip("Top bar text during play. The score and goal are on the scoreboard, so this is just the game's name.")]
        [SerializeField] string playingTopMessage = "Flag Wars";
        [SerializeField, Min(30)] int matchSeconds = 480;
        [SerializeField, Min(3)] int resultsSeconds = 10;
        [SerializeField, Min(1)] int capturesToWin = CTF_Rules.CapturesToWin;
        [Tooltip("Time up with the score level: this many seconds of sudden death (next capture wins, both flags go home). 0 = a draw at once.")]
        [SerializeField, Min(0)] int suddenDeathSeconds = 120;
        [Tooltip("Seconds before a player or bot comes back. Long enough that a team that wipes a defence can run a flag home (try 1: at 4 s an 8-minute match saw 56 grabs and 1 capture).")]
        [SerializeField, Min(0.5f)] float respawnSeconds = 6f;
        [Tooltip("A player who joins after this share of the match has gone only shares a win by making a flag play (grab, capture or return).")]
        [SerializeField, Range(0f, 1f)] float lateJoinWinShare = 0.5f;
        [Header("Characters")]
        [Tooltip("Health every character (player or bot) spawns with. A sidearm hit deals 100, so 200 = two hits.")]
        [SerializeField, Min(1)] long characterHealth = 200;
        [SerializeField] float walkSpeed = 14f, sprintSpeed = 21f;
        [SerializeField, Min(0f)] float jumpHeight = 1.6f;
        [Header("Power-ups")]
        [SerializeField, Min(1f)] float powerUpRespawnSeconds = 20f;
        [SerializeField, Min(1)] long healAmount = 100;
        [SerializeField, Min(1f)] float speedBoostMultiplier = 1.35f;
        [SerializeField, Min(0.5f)] float speedBoostSeconds = 8f;
        [Tooltip("Seconds of speed boost for taking a flag off its stand, so the carrier can clear the base before the defenders respawn beside it (try 1: 1 to 3 captures in an 8-minute match without it). 0 turns it off.")]
        [SerializeField, Min(0f)] float grabHeadStartSeconds = 5f;
        [Header("Bots")]
        [SerializeField] CTF_BotSquadTuning bots = new();
        [SerializeField] CTF_BotBrainTuning botBrain = new();
        [Header("Guns and perks")]
        [SerializeField] CTF_LoadoutTuning loadoutTuning = new();
        [Header("Medals and MVP")]
        [SerializeField] CTF_MedalTuning medalTuning = new();
        [SerializeField] CTF_MvpWeights mvpWeights = new();
        [Header("Daily twist (a rule change per UTC day)")]
        [SerializeField] CTF_DailyTuning daily = new();
        [Header("Retention (levels, unlocks, daily challenges, streak)")]
        [SerializeField] RetentionTuning retentionTuning = CTF_Retention.DefaultTuning();
        [Header("Network")]
        [Tooltip("State broadcasts per second while playing (once a second otherwise).")]
        [SerializeField, Min(1f)] float snapshotsPerSecond = 10f;
        [Tooltip("Shortest time between two answered state requests from one client (seconds).")]
        [SerializeField, Min(0f)] float stateRequestInterval = 1f;

        const string CapturesColumn = "Flags";
        const string KillsColumn = "Kills";
        static readonly ToolEnum[] Loadout = { ToolEnum.Pistol, ToolEnum.Dagger };

        readonly Dictionary<NetworkConnection, CTF_Actor> players = new();
        readonly Dictionary<GameCharacter, CTF_Actor> bodies = new();
        readonly List<CTF_Actor> actors = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly System.Random random = new();
        /// <summary>
        /// Who was dealt into this match on which team, by player id, so a player who leaves and comes
        /// back plays on for the same team with the same join time (no switching to the winning side).
        /// </summary>
        readonly Dictionary<string, (CTF_Team team, float joinedAtShare)> seats = new();
        CTF_DailyTwist twist;
        float matchStart, matchEnd;
        RetentionService<CTF_SaveData> retention;
        CTF_LoadoutService loadouts;
        CTF_BotSquad squad;
        CTF_Map map;
        CTF_Phase phase;
        int redScore, blueScore, secondsLeft;
        string headline = "";
        float nextSnapshot, previousRespawnTime;
        bool firstMatchPlayed, suddenDeath;

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
            // The daily challenges and the twist read the same clock, so a QA day offset moves both.
            retention = new RetentionService<CTF_SaveData>(CTF_Retention.Rules(retentionTuning), save => save.retention,
                () => daily.Now(DateTime.UtcNow));
            retention.Ready += OnRetentionReady;
            retention.Unlocked += OnUnlocked;
            loadouts = new CTF_LoadoutService(loadoutTuning, retention);
            squad = new CTF_BotSquad(this, bots, botBrain);
            var server = InstanceFinder.ServerManager;
            server.RegisterBroadcast<CTF_StateRequest>(OnStateRequest);
            server.RegisterBroadcast<CTF_LoadoutRequest>(OnLoadoutRequest);
        }

        static TeamColor Color(CTF_Team team) => team == CTF_Team.Red ? TeamColor.Red : TeamColor.Blue;

        Vector3 SpawnPosition(NetworkConnection connection) {
            if (map == null) return Vector3.up * 2f;
            if (phase is CTF_Phase.Waiting || BotsOnly || !players.TryGetValue(connection, out var actor)) return map.lobbySpawn.position + Vector3.up;
            return TeamSpawn(actor.Team);
        }

        /// <summary>
        /// A spawn point of <paramref name="team"/> nobody stands on: the one farthest from every living
        /// character (ties broken at random), so a bot and a player never appear inside each other.
        /// </summary>
        Vector3 TeamSpawn(CTF_Team team) {
            var spawns = map.Spawns(team);
            int pick = random.Next(spawns.Length);
            float best = -1f;
            for (int k = 0; k < spawns.Length; k++) {
                int i = (pick + k) % spawns.Length;
                float nearest = float.MaxValue;
                foreach (var actor in actors)
                    if (actor.IsAlive) nearest = Mathf.Min(nearest, (actor.Character.transform.position - spawns[i].position).sqrMagnitude);
                if (nearest <= best) continue;
                best = nearest;
                pick = i;
            }
            return spawns[pick].position + Vector3.up;
        }

        /// <summary>Spawn facing the enemy base, so the first view is the field rather than a wall.</summary>
        Quaternion SpawnRotation(NetworkConnection connection) {
            if (map == null) return Quaternion.identity;
            if (phase is CTF_Phase.Waiting || BotsOnly || !players.TryGetValue(connection, out var actor)) return map.lobbySpawn.rotation;
            return Facing(actor.Team);
        }

        Quaternion Facing(CTF_Team team) {
            Vector3 toward = map.Stand(CTF_Rules.Other(team)) - map.Stand(team);
            toward.y = 0f;
            return toward.sqrMagnitude > 0f ? Quaternion.LookRotation(toward) : Quaternion.identity;
        }

        // ---------------------------------------------------------------- bot world (what bots may know)
        public bool Moving => phase == CTF_Phase.Playing;
        public bool Fighting => phase == CTF_Phase.Playing;
        public CTF_Map Map => map;
        public IReadOnlyList<CTF_Actor> Actors => actors;
        public CTF_FlagState FlagState(CTF_Team flag) => flags[(int)flag].state;
        public Vector3 FlagPosition(CTF_Team flag) => flags[(int)flag].position;
        public CTF_Actor CarrierOf(CTF_Team flag) => flags[(int)flag].state == CTF_FlagState.Carried ? flags[(int)flag].carrier : null;
        public bool PowerUpReady(int index) => CTF_Rules.IsReady(powerUpsReady, index);
        public float SprintSpeed => sprintSpeed;

        public float SpeedMultiplier(CTF_Actor actor) =>
            CTF_Rules.SpeedMultiplier(IsCarrying(actor), loadoutTuning.CarrierSpeed(actor.LivePerk), actor.Boosted(Time.time), speedBoostMultiplier,
                loadoutTuning.SpeedPerk(actor.LivePerk));

        // ---------------------------------------------------------------- bot squad host
        CTF_IBotWorld CTF_BotSquad.IHost.World => this;
        long CTF_BotSquad.IHost.CharacterHealth => characterHealth;
        float CTF_BotSquad.IHost.RespawnSeconds => respawnSeconds;
        CTF_LoadoutTuning CTF_BotSquad.IHost.Loadouts => loadoutTuning;
        Pose CTF_BotSquad.IHost.SpawnPose(CTF_Team team) => new(TeamSpawn(team), Facing(team));

        void CTF_BotSquad.IHost.BodySpawned(CTF_Actor bot) {
            Watch(bot, bot.Character);
            // Clients find a bot's body (for the flag it carries) by its network object, new every life.
            BroadcastRoster(null);
        }

        void CTF_BotSquad.IHost.BotRemoved(CTF_Actor bot) {
            DropFlagsOf(bot, bot.Character != null ? bot.Character.transform.position : (Vector3?)null);
            Forget(bot.Character);
        }

        void CTF_BotSquad.IHost.RosterChanged() {
            RebuildActors();
            BroadcastRoster(null);
        }

        void RebuildActors() {
            actors.Clear();
            // The QA baseline leaves the players in the lobby, out of the match: bots neither see nor count them.
            if (!BotsOnly) actors.AddRange(players.Values);
            if (squad != null) actors.AddRange(squad.Bots);
        }

        /// <summary>QA: a bots-only match (see <see cref="CTF_BotSquadTuning.qaBotsOnly"/>).</summary>
        bool BotsOnly => squad != null && squad.BotsOnly;

        int Humans(CTF_Team team) {
            if (BotsOnly) return 0;
            int n = 0;
            foreach (var a in players.Values) if (a.Team == team && a.Connection.IsActive) n++;
            return n;
        }

        /// <summary>Bots fill whatever the players leave open (during a match only).</summary>
        void RebalanceBots() {
            if (phase is CTF_Phase.Countdown or CTF_Phase.Playing) squad.Rebalance(Humans(CTF_Team.Red), Humans(CTF_Team.Blue), spawn: true);
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
            var actor = ActorOf(player.Owner);
            PlayerProgress.Load<CTF_SaveData>(player, CTF_SaveData.Universe).Forget(e => Debug.LogException(e, this));
            retention.PlayerJoined(player);
            // Joining a match under way: the summary card covers the rest of it, and a bot makes room.
            if (phase == CTF_Phase.Playing) {
                retention.BeginMatch(player.Owner);
                Debug.Log($"[FlagWars] {actor} joined with {actor.JoinedAtShare:P0} of the match gone (win counts only with a flag play past {lateJoinWinShare:P0}).", this);
            }
            RebalanceBots();
            BroadcastRoster(actor.Connection);
        }

        /// <summary>The player's actor, created (and put on the team with fewer players) on first sight.</summary>
        CTF_Actor ActorOf(NetworkConnection connection) {
            if (!players.TryGetValue(connection, out var actor)) {
                var team = CTF_Rules.PickTeam(Humans(CTF_Team.Red), Humans(CTF_Team.Blue), redScore, blueScore, CTF_BotRules.FirstSide(random.Next()));
                float joinedAt = phase == CTF_Phase.Playing ? CTF_Rules.ShareGone(matchStart, matchEnd, Time.time) : 0f;
                // Back in the same match: the same team and the join time of the first visit.
                if (phase is CTF_Phase.Countdown or CTF_Phase.Playing && seats.TryGetValue(SeatKey(connection), out var seat))
                    (team, joinedAt) = seat;
                actor = CTF_Actor.Player(connection, team);
                actor.JoinedAtShare = joinedAt;
                players[connection] = actor;
                if (phase is CTF_Phase.Countdown or CTF_Phase.Playing) seats[SeatKey(connection)] = (team, joinedAt);
                RebuildActors();
            }
            SendTeam(actor);
            return actor;
        }

        static string SeatKey(NetworkConnection connection) {
            string id = PlayerData.TryGetPlayerData(connection, out var player) ? player.player_id.Value : null;
            return string.IsNullOrEmpty(id) ? "conn:" + connection.ClientId : id;
        }

        void SendTeam(CTF_Actor actor) {
            if (PlayerData.TryGetPlayerData(actor.Connection, out var player)) player.SetPlayerTeam(new TeamConfig(Color(actor.Team)));
            InstanceFinder.ServerManager.Broadcast(actor.Connection, new CTF_TeamBroadcast { team = actor.Team });
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            var actor = ActorOf(character.Owner);
            character.SetTeam(new TeamConfig(Color(actor.Team)));
            character.Init(characterHealth);
            // PlayerData.OnPlayerAdded and the character spawn are separate subscribers: make sure the loadout exists.
            foreach (var tool in Loadout) ServerTool.Instance.SpawnTool(character.NetworkObject, tool);
            Watch(actor, character);
            actor.NewLife();
            loadouts.Arm(actor);
            // A fresh body keeps a running boost's speed and the perk's.
            RefreshSpeed(actor);
            // Everyone's board and this body's gun look (clients find the body by its object id).
            RosterDirty();
        }

        void OnRetentionReady(NetworkConnection connection, int level) {
            if (!players.TryGetValue(connection, out var actor)) return;
            loadouts.Ready(actor);
            SendCareer(connection);
            RosterDirty();
            // Before the match the saved pick is worn at once; during play from the next life.
            if (phase != CTF_Phase.Playing && actor.IsAlive) {
                loadouts.Arm(actor);
                RefreshSpeed(actor);
            }
        }

        /// <summary>A level-up unlocked something (the kit shows the toast): the loadout card can offer it now.</summary>
        void OnUnlocked(NetworkConnection connection, UnlockDefinition unlock) {
            if (!players.TryGetValue(connection, out var actor)) return;
            loadouts.Refresh(actor);
            // A late unlock (trail, gold tracers) shows to everyone at once.
            RosterDirty();
        }

        void OnPlayerLeft(PlayerData player) {
            var connection = player.Owner;
            if (connection == null || !players.TryGetValue(connection, out var actor)) return;
            DropFlagsOf(actor, actor.Character != null ? actor.Character.transform.position : (Vector3?)null);
            Forget(actor.Character);
            players.Remove(connection);
            requestTimes.Remove(connection);
            loadouts.Left(connection);
            RebuildActors();
            // Nobody left to play with: the bots leave too (they come back with the next player).
            if (ActiveHumans() == 0) squad.Clear();
            else RebalanceBots();
            BroadcastRoster(null);
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
            firstBlood = false;
            seats.Clear();
            twist = daily.Today(DateTime.UtcNow);
            loadouts.Twist = twist;
            BroadcastRoster(null);
            Debug.Log($"[FlagWars] daily twist: {CTF_Daily.Describe(twist, daily.xpMultiplier, daily.toughBotsResultBonus)} (day offset {daily.previewDayOffset}).", this);
            foreach (var flag in flags) Return(flag, false);
            ResetPowerUps();
            ShuffleTeams();
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();

            await WaitForPlayersAsync(token);

            // ---- countdown: bots join and everyone lines up at their base
            phase = CTF_Phase.Countdown;
            ShuffleTeams();
            seats.Clear();
            foreach (var actor in players.Values) {
                actor.JoinedAtShare = 0f;
                if (actor.Connection.IsActive) seats[SeatKey(actor.Connection)] = (actor.Team, 0f);
                loadouts.Refresh(actor);
            }
            ChooseBots();
            squad.Rebalance(Humans(CTF_Team.Red), Humans(CTF_Team.Blue), spawn: false);
            RebuildActors();
            squad.SpawnAll();
            foreach (var actor in players.Values) actor.ResetMatch();
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            for (int i = countdownSeconds; i > 0; i--) {
                secondsLeft = i;
                headline = $"Capture {capturesToWin} flags to win. Starting in {i}";
                SetTopMessage(headline);
                BroadcastState();
                await AwaitTime(1000, token);
            }

            // ---- play
            phase = CTF_Phase.Playing;
            SetGlobalInvul(false);
            if (!BotsOnly) foreach (var actor in players.Values) retention.BeginMatch(actor.Connection);
            matchStart = Time.time;
            float end = matchEnd = Time.time + matchSeconds;
            suddenDeath = false;
            while (true) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(end - Time.time));
                if (CTF_Rules.StartsSuddenDeath(redScore, blueScore, secondsLeft, suddenDeath, suddenDeathSeconds)) {
                    // A level score at time-up ends on a moment: both flags home, next capture wins.
                    suddenDeath = true;
                    end = Time.time + suddenDeathSeconds;
                    secondsLeft = suddenDeathSeconds;
                    foreach (var flag in flags) Return(flag, false);
                    Debug.Log($"[FlagWars] sudden death at {redScore}-{blueScore} ({suddenDeathSeconds} s).", this);
                    BroadcastState();
                }
                if (CTF_Rules.MatchOver(redScore, blueScore, capturesToWin, secondsLeft, suddenDeath)) break;
                headline = suddenDeath ? "SUDDEN DEATH - next capture wins" : $"RED {redScore}  -  {blueScore} BLUE";
                SetTopMessage(suddenDeath ? "Sudden death" : playingTopMessage);
                await AwaitTime(suddenDeath ? 100 : 500, token);
            }

            // ---- results
            phase = CTF_Phase.Results;
            SetGlobalInvul(true);
            foreach (var flag in flags) Return(flag, false);
            ClearBoosts();
            var winner = CTF_Rules.Winner(redScore, blueScore);
            headline = winner is CTF_Team w ? $"{CTF_Rules.Name(w)} TEAM WINS {Math.Max(redScore, blueScore)}-{Math.Min(redScore, blueScore)}!" : $"DRAW {redScore}-{blueScore}";
            SetMusic(MusicSelection.VictoryMusic);
            FinishMatch(winner);
            firstMatchPlayed = true;
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                SetTopMessage($"Next match in {i}");
                BroadcastState();
                await AwaitTime(1000, token);
            }
        }

        /// <summary>
        /// Waits for <see cref="minHumans"/> players (two without bots). Before the first match a lone
        /// player gets <see cref="lobbySeconds"/> for friends to join, then bots fill the teams.
        /// </summary>
        async UniTask WaitForPlayersAsync(CancellationToken token) {
            int needed = squad.Fills ? minHumans : Math.Max(2, minHumans);
            int full = squad.TeamSize * 2;
            int lobbyLeft = firstMatchPlayed ? 0 : lobbySeconds;
            while (true) {
                int humans = ActiveHumans();
                if (humans >= needed && (lobbyLeft <= 0 || humans >= full)) return;
                if (humans < needed) {
                    lobbyLeft = firstMatchPlayed ? 0 : lobbySeconds;
                    headline = $"Waiting for players ({humans}/{needed})";
                } else {
                    secondsLeft = lobbyLeft;
                    headline = $"Bots fill the empty slots in {lobbyLeft}. Invite friends to play together!";
                    lobbyLeft--;
                }
                SetTopMessage(headline);
                BroadcastState();
                await AwaitTime(1000, token);
            }
        }

        int ActiveHumans() => players.Values.Count(a => a.Connection.IsActive);

        /// <summary>
        /// Splits the players evenly at random (bots then even the numbers). Which colour the first
        /// player gets is random too, so a lone player plays from both bases over time.
        /// </summary>
        void ShuffleTeams() {
            var active = players.Values.Where(a => a.Connection.IsActive).ToList();
            TeamSplit.Shuffle(active, random);
            var first = CTF_BotRules.FirstSide(random.Next());
            for (int i = 0; i < active.Count; i++) {
                active[i].Team = i % 2 == 0 ? first : CTF_Rules.Other(first);
                SendTeam(active[i]);
            }
        }

        /// <summary>
        /// Each team's bot tier and size for the match, from its players' saved results (see
        /// <see cref="CTF_BotRules.Plan"/>), and the log line QA reads.
        /// </summary>
        void ChooseBots() {
            var red = Standing(CTF_Team.Red);
            var blue = Standing(CTF_Team.Blue);
            var plan = squad.ChooseSkill(red, blue, CTF_Daily.BotTierBonus(twist, daily.toughBotTiers));
            Debug.Log($"[FlagWars] bots: Red {plan.red}{ShortNote(plan.redShort)}, Blue {plan.blue}{ShortNote(plan.blueShort)} (base {plan.baseSkill}" +
                      $"{(BotsOnly ? ", QA bots only" : "")}{(twist == CTF_DailyTwist.ToughBots ? $", Tough Bots +{daily.toughBotTiers} unless a loss" : "")}); " +
                      $"players Red {Describe(red)}, Blue {Describe(blue)}.", this);
        }

        static string ShortNote(int shortBy) => shortBy > 0 ? $" ({shortBy} short)" : "";

        static string Describe(in CTF_TeamStanding s) =>
            s.humans == 0 ? "none" : $"{s.humans} (level {s.level:0.#}, tier {(CTF_BotSkill)s.tier}, losses {s.lossStreak}{(s.lostLast ? ", lost last" : "")}{(s.newcomer ? ", no win yet" : "")})";

        CTF_TeamStanding Standing(CTF_Team team) {
            var s = new CTF_TeamStanding { tier = CTF_BotRules.TierCount - 1 };
            if (BotsOnly) return default;
            float levels = 0f;
            foreach (var actor in players.Values) {
                if (!actor.Connection.IsActive || actor.Team != team) continue;
                s.humans++;
                levels += retention.LevelOf(actor.Connection);
                // A save not read yet counts as a new player's.
                var save = PlayerProgress.TryGet<CTF_SaveData>(actor.Connection, out var loaded) ? loaded : null;
                s.tier = Math.Min(s.tier, save?.botTier ?? 0);
                s.lossStreak = Math.Max(s.lossStreak, save?.lossStreak ?? 0);
                s.lostLast |= (save?.lastMargin ?? 0) < 0;
                s.newcomer |= (save?.wins ?? 0) == 0;
            }
            if (s.humans == 0) return default;
            s.level = levels / s.humans;
            return s;
        }

        void Update() {
            if (map == null) return;
            if (phase is CTF_Phase.Countdown or CTF_Phase.Playing) squad.Tick(Time.time);
            FlushRoster();
        }

        void FixedUpdate() {
            if (map == null) return;
            foreach (var actor in actors)
                if (actor.IsAlive && actor.Character.transform.position.y < map.killY) actor.Character.Kill(DamageType.Despawn);
            ExpireBoosts();
            if (phase != CTF_Phase.Playing) {
                if (Time.time > nextSnapshot) { nextSnapshot = Time.time + 1f; BroadcastState(); }
                return;
            }
            foreach (var flag in flags) UpdateFlag(flag);
            UpdatePowerUps();
            if (Time.time > nextSnapshot) { nextSnapshot = Time.time + 1f / snapshotsPerSecond; BroadcastState(); }
        }

        // ---------------------------------------------------------------- messages
        void OnStateRequest(NetworkConnection connection, CTF_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < stateRequestInterval) return;
            requestTimes[connection] = Time.unscaledTime;
            if (players.TryGetValue(connection, out var actor)) {
                SendTeam(actor);
                loadouts.Refresh(actor);
            }
            BroadcastRoster(connection);
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        void OnLoadoutRequest(NetworkConnection connection, CTF_LoadoutRequest request, Channel channel) {
            if (!connection.IsAuthenticated || !players.TryGetValue(connection, out var actor)) return;
            if (!loadouts.Request(actor, request)) return;
            // Before the match the new pick is worn at once, so players can try each gun in the lobby.
            if (phase != CTF_Phase.Playing && actor.IsAlive) {
                loadouts.Arm(actor);
                RefreshSpeed(actor);
            }
        }

        string NameOf(CTF_Actor actor) {
            if (actor == null) return "";
            if (actor.IsBot) return actor.BotName;
            string name = PlayerData.GetPlayerName(actor.Connection);
            if (string.IsNullOrEmpty(name)) name = actor.Character != null ? actor.Character.DisplayName : "";
            // The top of the track: a title everyone sees in the feeds and on the MVP line.
            return retention.IsUnlocked(actor.Connection, CTF_Retention.UnlockFlagLegend) ? $"{CTF_Retention.LegendTitle} {name}" : name;
        }

        /// <summary>The twist the last roster sent to everyone carried (null = none sent yet).</summary>
        CTF_DailyTwist? twistSent;

        readonly List<CTF_BotView> viewBuffer = new();
        bool rosterDirty;
        float nextRosterAt;
        /// <summary>Shortest gap between two roster broadcasts sent for a stat change (seconds).</summary>
        const float RosterInterval = 0.25f;

        /// <summary>A kill, flag play or new life changed the board: send the roster soon (batched).</summary>
        void RosterDirty() => rosterDirty = true;

        void FlushRoster() {
            if (!rosterDirty || Time.time < nextRosterAt) return;
            BroadcastRoster(null);
        }

        CTF_BotView View(CTF_Actor actor, string name) => new() {
            actorId = actor.Id,
            objectId = actor.Character != null && actor.Character.IsSpawned ? actor.Character.ObjectId : -1,
            name = name,
            team = actor.Team,
            weapon = actor.IsBot ? actor.BotWeapon : actor.LiveWeapon,
            kills = actor.Stats.kills, deaths = actor.Stats.deaths, captures = actor.Stats.captures, returns = actor.Stats.returns,
            alive = actor.IsAlive,
            flagTrail = !actor.IsBot && retention.IsUnlocked(actor.Connection, CTF_Retention.UnlockFlagTrail),
            goldTracers = !actor.IsBot && retention.IsUnlocked(actor.Connection, CTF_Retention.UnlockGoldTracers),
        };

        void BroadcastRoster(NetworkConnection only) {
            if (squad == null || InstanceFinder.ServerManager == null) return;
            if (only == null) {
                rosterDirty = false;
                nextRosterAt = Time.time + RosterInterval;
            }
            var list = squad.Bots;
            var views = new CTF_BotView[list.Count];
            for (int i = 0; i < list.Count; i++) views[i] = View(list[i], list[i].BotName);
            viewBuffer.Clear();
            if (!BotsOnly)
                foreach (var actor in players.Values)
                    if (actor.Connection.IsActive) viewBuffer.Add(View(actor, NameOf(actor)));
            var humans = viewBuffer.ToArray();
            var plan = squad.Plan;
            // The twist rides along only when it changed (or to the one client asking): bots respawn often.
            bool sendTwist = only != null || twistSent != twist;
            var message = new CTF_RosterBroadcast {
                bots = views, skill = squad.Skill, teamSize = squad.Fills ? squad.TeamSize : 0,
                hasTwist = sendTwist,
                twist = sendTwist ? twist : CTF_DailyTwist.None,
                twistText = sendTwist ? CTF_Daily.Describe(twist, daily.xpMultiplier, daily.toughBotsResultBonus) : "",
                redSkill = plan.red, blueSkill = plan.blue, redShort = plan.redShort, blueShort = plan.blueShort,
                players = humans,
            };
            if (only == null) twistSent = twist;
            if (only != null) {
                if (only.IsActive) InstanceFinder.ServerManager.Broadcast(only, message);
            } else InstanceFinder.ServerManager.Broadcast(message);
        }

        static CTF_FlagSnap Snap(Flag flag) => new() {
            state = flag.state, position = flag.position, carrierId = flag.carrier != null ? flag.carrier.Id : -1
        };

        CTF_StateBroadcast MakeState() => new() {
            phase = phase, secondsLeft = secondsLeft, redScore = redScore, blueScore = blueScore,
            redFlag = Snap(flags[0]), blueFlag = Snap(flags[1]), headline = headline,
            powerUpsReady = powerUpsReady,
            sequence = ++stateSequence,
            capturesToWin = capturesToWin,
            suddenDeath = suddenDeath && phase == CTF_Phase.Playing,
        };

        uint stateSequence;

        void BroadcastState() => InstanceFinder.ServerManager.Broadcast(MakeState(), true, Channel.Unreliable);

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
            squad?.Clear();
            foreach (var watch in watches.Values) watch.Release();
            watches.Clear();
            if (retention != null) {
                retention.Ready -= OnRetentionReady;
                retention.Unlocked -= OnUnlocked;
                retention.Dispose();
            }
            var server = InstanceFinder.ServerManager;
            if (server != null) {
                server.UnregisterBroadcast<CTF_StateRequest>(OnStateRequest);
                server.UnregisterBroadcast<CTF_LoadoutRequest>(OnLoadoutRequest);
            }
            base.OnDestroy();
        }
    }
}
