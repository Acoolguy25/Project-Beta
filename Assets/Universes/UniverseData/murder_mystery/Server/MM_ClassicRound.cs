using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Item.FloatingTool;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// The Classic round structure on top of <see cref="MM_ServerRunner"/>: role reveal, the hunt, body
    /// reports, emergency meetings with an accusation vote, ejections, the end-of-round reveal, the
    /// murderers' grace period and kill cooldown, and weapons dropped by the dead.
    /// The runner still owns spawning, roles, tools and win conditions. Numbers come from
    /// <see cref="MM_ClassicTuning"/> on the runner.
    /// </summary>
    public sealed class MM_ClassicRound {
        // The shared vote window fits three options in a row.
        const int MaxWitnesses = 3;
        const int MinSuspects = 3;
        // A report is accepted from a little further than the prompt shows, to absorb movement lag.
        const float ReportDistanceTolerance = 1.5f;
        // Dropped weapons float at the middle of the body they fell from.
        const float DropHeightFraction = 0.5f;

        sealed class Body {
            public int id;
            public Vector3 position;
            public string victimName;
            public List<GameCharacter> witnesses;
            public bool reported;
        }

        /// <summary>Someone who had a role this round, kept for the final reveal.</summary>
        sealed class Participant {
            public GameCharacter character;
            public PlayerData player; // null for NPCs
            public string alias, username;
            public MM_Role role;
            public bool ejected;
        }

        readonly MM_ServerRunner runner;
        readonly MM_ClassicTuning tuning;
        readonly List<Body> bodies = new();
        readonly List<Participant> participants = new();
        readonly Dictionary<GameCharacter, int> kills = new();
        int nextBodyId;
        int meetingsHeld;
        float reportAllowedAt;
        bool ejecting;
        UniTaskCompletionSource<Body> reportSignal = new();
        MM_KillClock killClock;
        FloatingToolShared droppedGun;

        public int Round { get; private set; }
        public MM_Phase Phase { get; private set; } = MM_Phase.Lobby;
        public bool Active { get; private set; }
        public int SecondsLeft { get; set; }
        public int MeetingsLeft => Mathf.Max(0, tuning.meetingsPerRound - meetingsHeld);

        public MM_ClassicRound(MM_ServerRunner runner, MM_ClassicTuning tuning) {
            this.runner = runner;
            this.tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            killClock = new MM_KillClock(tuning);
        }

        // ---------- lifecycle ----------

        public void Begin(int round) {
            Round = round;
            Active = true;
            bodies.Clear();
            participants.Clear();
            kills.Clear();
            nextBodyId = 0;
            meetingsHeld = 0;
            ejecting = false;
            reportSignal = new UniTaskCompletionSource<Body>();
            // Rebuilt every round so Inspector changes to the tuning apply from the next round on.
            killClock = new MM_KillClock(tuning);
            droppedGun = null;
            Phase = MM_Phase.RoleReveal;
        }

        public void End() {
            Active = false;
            Phase = MM_Phase.Lobby;
            bodies.Clear();
            participants.Clear();
            kills.Clear();
            killClock.Clear();
            droppedGun = null;
            reportSignal.TrySetCanceled();
        }

        public void AddParticipant(GameCharacter character, PlayerData player, TeamColor team) {
            if (character == null)
                return;
            participants.Add(new Participant {
                character = character,
                player = player,
                alias = character.DisplayName,
                username = player != null ? player.username.Value : null,
                role = RoleOf(team),
            });
        }

        public static MM_Role RoleOf(TeamColor team) => team switch {
            TeamColor.Red => MM_Role.Murderer,
            TeamColor.Blue => MM_Role.Sheriff,
            TeamColor.Green => MM_Role.Innocent,
            _ => MM_Role.None,
        };

        public static TeamColor TeamOf(MM_Role role) => role switch {
            MM_Role.Murderer => TeamColor.Red,
            MM_Role.Sheriff => TeamColor.Blue,
            MM_Role.Innocent => TeamColor.Green,
            _ => TeamColor.None,
        };

        // ---------- murderer balance ----------

        /// <summary>
        /// Whether this murderer may kill right now: only during the hunt, after the grace period, and once
        /// their own kill cooldown has run out. NPC murderers ask before every attack.
        /// </summary>
        public bool CanKill(GameCharacter murderer) =>
            Active && Phase == MM_Phase.Hunt && murderer != null && killClock.CanKill(KillerId(murderer), Time.time);

        static int KillerId(GameCharacter character) => character.NetworkObject.ObjectId;

        /// <summary>
        /// Puts every living player murderer's dagger on the server cooldown the kill clock dictates, so their
        /// client shows it and refuses to swing early. NPC murderers are held back through <see cref="CanKill"/>
        /// before they swing; <see cref="OnCharacterDied"/> also locks their dagger after a kill.
        /// </summary>
        void SyncPlayerDaggers() {
            foreach (Participant participant in participants)
                if (participant.player != null && participant.role == MM_Role.Murderer)
                    SyncDagger(participant.character);
        }

        /// <summary>
        /// Locks the murderer's dagger for as long as the kill clock says. Applies to NPCs too: one swing of
        /// an NPC's dagger can sweep several victims, and the server cooldown drops every hit after the first.
        /// </summary>
        void SyncDagger(GameCharacter murderer) {
            if (!IsAlive(murderer))
                return;
            ToolBaseShared dagger = ServerTool.Instance.GetTool(murderer.NetworkObject, ToolEnum.Dagger);
            if (dagger != null)
                dagger.UpdateServerCooldown(killClock.SecondsUntilReady(KillerId(murderer), Time.time));
        }

        // ---------- deaths and reports ----------

        public void OnCharacterDied(GameCharacter victim, GameCharacter killer) {
            if (!Active || victim == null)
                return;
            if (killer != null && killer != victim) {
                kills[killer] = kills.TryGetValue(killer, out int k) ? k + 1 : 1;
                MM_SaveData.RecordKill(killer, victim);
                if (RoleOf(killer.GetTeam().realTeam) == MM_Role.Murderer) {
                    killClock.RecordKill(KillerId(killer), Time.time);
                    SyncDagger(killer);
                }
            }
            if (ejecting)
                return;

            Vector3 position = victim.transform.position;
            // The registry only holds living characters, so this is "who was around".
            List<GameCharacter> witnesses = LivingCharacters()
                .Where(c => c != victim && (c.transform.position - position).sqrMagnitude <= tuning.witnessDistance * tuning.witnessDistance)
                .OrderBy(c => (c.transform.position - position).sqrMagnitude)
                .Take(MaxWitnesses)
                .ToList();
            bodies.Add(new Body {
                id = nextBodyId++,
                position = position,
                victimName = victim.DisplayName,
                witnesses = witnesses,
            });
        }

        public void OnReportRequest(NetworkConnection conn, MM_ReportRequest request, FishNet.Transporting.Channel channel) {
            if (!Active || Phase != MM_Phase.Hunt || MeetingsLeft <= 0 || Time.time < reportAllowedAt)
                return;
            if (!TryGetLivingCharacter(conn, out GameCharacter reporter))
                return;
            Body body = bodies.FirstOrDefault(b => b.id == request.bodyId && !b.reported);
            if (body == null || (reporter.transform.position - body.position).sqrMagnitude > tuning.reportDistance * tuning.reportDistance * ReportDistanceTolerance)
                return;
            body.reported = true;
            pendingReporter = reporter;
            reportSignal.TrySetResult(body);
        }

        GameCharacter pendingReporter;

        // ---------- dropped weapons ----------

        /// <summary>
        /// A character died: the sheriff's gun (and, when enabled, a murderer's dagger) drops where they fell for
        /// an innocent player to take over. The role is read before the runner recolours the corpse.
        /// </summary>
        public void DropWeapons(GameCharacter character) {
            if (!Active || character == null)
                return;
            MM_DroppedWeapon weapon = MM_DropRules.WeaponLeftBy(RoleOf(character.GetTeam().realTeam), tuning.dropDaggerOnMurdererDeath);
            if (weapon == MM_DroppedWeapon.None)
                return;
            Vector3 position = character.transform.position + character.transform.lossyScale.y * DropHeightFraction * Vector3.up;
            FloatingToolShared floating = ServerTool.Instance.SpawnFloatingTool(ToolFor(weapon), position);
            floating.OnToolCollectedFunc = OnDroppedWeaponCollected;
            if (weapon == MM_DroppedWeapon.Gun) {
                droppedGun = floating;
                ServerChat.SendSystemMessage(new("The sheriff is down! Their gun lies where they fell. An innocent can pick it up.", SystemMessageSource.CustomMessage));
            }
        }

        static ToolEnum ToolFor(MM_DroppedWeapon weapon) => weapon == MM_DroppedWeapon.Gun ? ToolEnum.Pistol : ToolEnum.Dagger;

        static MM_DroppedWeapon WeaponOf(ToolEnum tool) => tool switch {
            ToolEnum.Pistol => MM_DroppedWeapon.Gun,
            ToolEnum.Dagger => MM_DroppedWeapon.Dagger,
            _ => MM_DroppedWeapon.None,
        };

        /// <summary>Server side of a pickup: the collector takes the weapon and, secretly, the role that goes with it.</summary>
        bool OnDroppedWeaponCollected(NetworkBehaviour collector, ToolEnum tool) {
            if (!Active || Phase != MM_Phase.Hunt || !collector.TryGetComponent(out GameCharacter character))
                return false;
            MM_DroppedWeapon weapon = WeaponOf(tool);
            MM_Role current = RoleOf(character.GetTeam().realTeam);
            if (weapon == MM_DroppedWeapon.None || !MM_DropRules.CanPickUp(current, IsAlive(character), character.Tools.Length))
                return false;

            MM_Role role = MM_DropRules.RoleFor(weapon);
            // The display team stays hidden, exactly like a role dealt at the start of the round.
            character.SetTeam(new TeamConfig(TeamOf(role), TeamColor.None));
            if (ServerTool.Instance.SpawnTool(character.NetworkObject, tool) == null)
                return false;
            Participant participant = participants.FirstOrDefault(p => p.character == character);
            if (participant != null)
                participant.role = role;
            if (weapon == MM_DroppedWeapon.Gun) {
                droppedGun = null;
                MM_SaveData.RecordGunPickup(character);
            }
            if (role == MM_Role.Murderer)
                SyncDagger(character);
            if (character.Owner.IsValid)
                ServerChat.SendSystemMessage(character.Owner, new(role == MM_Role.Sheriff
                    ? "You picked up the gun. You are the Sheriff now: find the murderer."
                    : "You picked up the dagger. You are a Murderer now: don't get caught.", SystemMessageSource.CustomMessage));
            return true;
        }

        // ---------- phases ----------

        public async UniTask RunRoleReveal(CancellationToken token) {
            Phase = MM_Phase.RoleReveal;
            for (int i = tuning.roleRevealSeconds; i > 0; i--) {
                SecondsLeft = i;
                ServerRunner.SetTopMessage($"Round {Round}: check your role! ({i})");
                if (await runner.AwaitTime(1000, token))
                    break;
            }
        }

        /// <summary>
        /// Counts the hunt down, pausing it for meetings. Returns when time runs out, the round is decided,
        /// or the timer is skipped.
        /// </summary>
        public async UniTask RunHunt(int seconds, Func<bool> roundOver, CancellationToken token) {
            Phase = MM_Phase.Hunt;
            SecondsLeft = seconds;
            reportAllowedAt = Time.time + tuning.firstReportDelaySeconds;
            killClock.BeginHunt(Time.time);
            SyncPlayerDaggers();
            while (SecondsLeft > 0 && !roundOver()) {
                ServerRunner.SetTopMessage($"Mystery In Progress ({SecondsLeft})");
                UniTask<bool> tick = runner.AwaitTime(1000, token);
                var (winner, report, skipped) = await UniTask.WhenAny(reportSignal.Task.SuppressCancellationThrow(), tick);
                token.ThrowIfCancellationRequested();
                if (winner == 0) {
                    reportSignal = new UniTaskCompletionSource<Body>();
                    if (report.IsCanceled || report.Result == null)
                        continue;
                    int huntLeft = SecondsLeft; // meetings count their own phases down in SecondsLeft
                    await RunMeeting(report.Result, pendingReporter, token);
                    SecondsLeft = huntLeft;
                    Phase = MM_Phase.Hunt;
                    reportAllowedAt = Time.time + tuning.reportCooldownSeconds;
                    killClock.EndMeeting(Time.time);
                    SyncPlayerDaggers();
                    continue;
                }
                if (skipped)
                    break;
                SecondsLeft--;
            }
        }

        async UniTask RunMeeting(Body body, GameCharacter reporter, CancellationToken token) {
            meetingsHeld++;
            string reporterName = reporter != null ? reporter.DisplayName : "Someone";
            var freeze = FreezeEveryone();
            try {
                // Suspects: who was near the body when it happened and is still alive, padded with whoever
                // is near it now so the killer is never the only name on the list.
                List<GameCharacter> suspects = body.witnesses.Where(IsAlive).ToList();
                foreach (GameCharacter extra in LivingCharacters()
                             .Where(c => !suspects.Contains(c))
                             .OrderBy(c => (c.transform.position - body.position).sqrMagnitude)) {
                    if (suspects.Count >= MinSuspects)
                        break;
                    suspects.Add(extra);
                }
                suspects = suspects.OrderBy(_ => UnityEngine.Random.value).ToList();
                string[] names = UniqueNames(suspects);

                Phase = MM_Phase.Meeting;
                InstanceFinder.ServerManager.Broadcast(new MM_MeetingBroadcast {
                    round = Round,
                    meeting = meetingsHeld,
                    reporterName = reporterName,
                    victimName = body.victimName,
                    suspects = names,
                    discussionSeconds = tuning.discussionSeconds,
                });
                ServerChat.SendSystemMessage(new($"EMERGENCY MEETING: {reporterName} found the body of {body.victimName}. Seen nearby: {string.Join(", ", names)}.", SystemMessageSource.CustomMessage));
                for (int i = tuning.discussionSeconds; i > 0; i--) {
                    SecondsLeft = i;
                    ServerRunner.SetTopMessage($"Meeting: discuss who killed {body.victimName}! ({i})");
                    if (await runner.AwaitTime(1000, token))
                        break;
                }

                if (suspects.Count == 0) {
                    await ShowEjection(new MM_EjectionBroadcast { round = Round, headline = "Nobody near the body is still alive." }, token);
                    return;
                }

                Phase = MM_Phase.Voting;
                SecondsLeft = tuning.voteSeconds;
                string[] descriptions = suspects.Select(s => s.Owner.IsValid ? "Player" : "Townsperson").ToArray();
                VoteResult result = await ServerVote.StartDynamicVote(
                    $"Who killed {body.victimName}?",
                    $"Reported by {reporterName}. These were seen near the body. Most votes is ejected; a tie ejects nobody.",
                    names, descriptions, tuning.voteSeconds, token, CanVote);
                MM_SaveData.RecordVotes(suspects);

                MM_EjectionBroadcast ejection = new() { round = Round };
                GameCharacter chosen = result.winner >= 0 && result.winner < suspects.Count ? suspects[result.winner] : null;
                if (chosen != null && IsAlive(chosen)) {
                    MM_Role role = RoleOf(chosen.GetTeam().realTeam);
                    ejection.ejected = true;
                    ejection.name = names[result.winner];
                    ejection.role = role;
                    ejection.wasPlayer = chosen.Owner.IsValid;
                    ejection.headline = role == MM_Role.Murderer
                        ? $"{ejection.name} was a Murderer!"
                        : $"{ejection.name} was not a Murderer. They were {(role == MM_Role.Sheriff ? "the Sheriff" : "Innocent")}.";
                    Participant p = participants.FirstOrDefault(x => x.character == chosen);
                    if (p != null)
                        p.ejected = true;
                    ejecting = true;
                    try {
                        chosen.Kill(DamageType.Command);
                    } finally {
                        ejecting = false;
                    }
                } else {
                    ejection.headline = result.tied ? "The vote was tied. Nobody was ejected." : "Nobody was ejected.";
                }
                ServerChat.SendSystemMessage(new(ejection.headline, SystemMessageSource.CustomMessage));
                await ShowEjection(ejection, token);
            } finally {
                Unfreeze(freeze);
            }
        }

        async UniTask ShowEjection(MM_EjectionBroadcast ejection, CancellationToken token) {
            Phase = MM_Phase.Ejection;
            ejection.seconds = tuning.ejectionSeconds;
            InstanceFinder.ServerManager.Broadcast(ejection);
            for (int i = tuning.ejectionSeconds; i > 0; i--) {
                SecondsLeft = i;
                ServerRunner.SetTopMessage($"{ejection.headline} ({i})");
                if (await runner.AwaitTime(1000, token))
                    break;
            }
        }

        public async UniTask RunResults(string winner, string summary, int seconds, CancellationToken token) {
            Phase = MM_Phase.Results;
            MM_ResultEntry[] entries = participants
                .Where(p => p.player != null || p.role == MM_Role.Murderer)
                .OrderBy(p => p.player == null)
                .ThenBy(p => p.role == MM_Role.Murderer ? 0 : p.role == MM_Role.Sheriff ? 1 : 2)
                .Select(p => new MM_ResultEntry {
                    name = p.alias,
                    username = p.username,
                    role = p.role,
                    isPlayer = p.player != null,
                    survived = p.character != null && !p.character.IsDead,
                    ejected = p.ejected,
                    kills = p.character != null && kills.TryGetValue(p.character, out int k) ? k : 0,
                })
                .ToArray();
            for (int i = seconds; i > 0; i--) {
                SecondsLeft = i;
                InstanceFinder.ServerManager.Broadcast(new MM_ResultsBroadcast {
                    round = Round, winner = winner, summary = summary, entries = entries, secondsLeft = i,
                });
                ServerRunner.SetTopMessage($"{winner} win! Next round in {i}");
                if (await runner.AwaitTime(1000, token))
                    break;
            }
        }

        // ---------- per-player state ----------

        public MM_StateBroadcast BuildState(PlayerData player, bool classic) {
            bool gunDropped = Active && classic && droppedGun != null && droppedGun.IsSpawned;
            MM_StateBroadcast state = new() {
                round = Round,
                phase = Active ? Phase : MM_Phase.Lobby,
                classic = classic,
                secondsLeft = SecondsLeft,
                meetingsLeft = MeetingsLeft,
                reportCooldown = Mathf.Max(0, Mathf.CeilToInt(reportAllowedAt - Time.time)),
                reportableBody = -1,
                nearbyBodies = Array.Empty<Vector3>(),
                gunDropped = gunDropped,
                gunPosition = gunDropped ? droppedGun.transform.position : Vector3.zero,
            };
            bool alive = TryGetLivingCharacter(player.Owner, out GameCharacter character);
            state.alive = alive;
            // Death recolours the character, so prefer the role the player was dealt this round.
            Participant dealt = Active ? participants.FirstOrDefault(p => p.player == player) : null;
            MM_Role role = dealt != null ? dealt.role : RoleOf(player.team.Value.realTeam);
            state.role = Active && classic && role == MM_Role.None ? MM_Role.Spectator : role;
            if (!Active || !classic || !alive)
                return state;

            if (state.role == MM_Role.Murderer && killClock.Running)
                state.killCooldown = Mathf.CeilToInt(killClock.SecondsUntilReady(KillerId(character), Time.time));

            Vector3 at = character.transform.position;
            List<Vector3> markers = new();
            float best = float.MaxValue;
            foreach (Body body in bodies) {
                if (body.reported)
                    continue;
                float sqr = (body.position - at).sqrMagnitude;
                if (sqr <= tuning.markerDistance * tuning.markerDistance)
                    markers.Add(body.position);
                if (sqr <= tuning.reportDistance * tuning.reportDistance && sqr < best) {
                    best = sqr;
                    state.reportableBody = body.id;
                    state.reportableBodyName = body.victimName;
                }
            }
            state.nearbyBodies = markers.ToArray();
            return state;
        }

        // ---------- helpers ----------

        static IEnumerable<GameCharacter> LivingCharacters() =>
            GameCharacter.TeamToCharacter.Values.SelectMany(list => list).Where(c => c != null && !c.IsDead).Distinct().ToList();

        static bool IsAlive(GameCharacter character) => character != null && !character.IsDead;

        static bool TryGetLivingCharacter(NetworkConnection conn, out GameCharacter character) {
            character = null;
            if (conn == null || !LocalCharacter.Characters.TryGetValue(conn, out LocalCharacter local) || local == null || local.IsDead)
                return false;
            character = local;
            return true;
        }

        /// <summary>Only players still alive in the round may accuse.</summary>
        bool CanVote(PlayerData player) =>
            TryGetLivingCharacter(player.Owner, out _) && RoleOf(player.team.Value.realTeam) != MM_Role.None;

        static string[] UniqueNames(List<GameCharacter> characters) {
            string[] names = new string[characters.Count];
            Dictionary<string, int> seen = new();
            for (int i = 0; i < characters.Count; i++) {
                string name = string.IsNullOrWhiteSpace(characters[i].DisplayName) ? "Unknown" : characters[i].DisplayName;
                seen[name] = seen.TryGetValue(name, out int n) ? n + 1 : 1;
                names[i] = seen[name] > 1 ? $"{name} ({seen[name]})" : name;
            }
            return names;
        }

        struct FreezeState {
            public float walk, flee, attack;
            public Dictionary<PlayerData, (float walk, float sprint)> players;
        }

        /// <summary>Stops every NPC and player in place for the meeting and makes everyone invulnerable.</summary>
        FreezeState FreezeEveryone() {
            FreezeState state = new() {
                walk = LocalNPC.WalkSpeedMultiplier,
                flee = LocalNPC.FleeSpeedMultiplier,
                attack = LocalNPC.AttackSpeedMultiplier,
                players = new(),
            };
            LocalNPC.WalkSpeedMultiplier = LocalNPC.FleeSpeedMultiplier = LocalNPC.AttackSpeedMultiplier = 0f;
            MM_ServerRunner.RefreshNPCSpeeds();
            foreach (PlayerData player in PlayerData.Players.Values) {
                state.players[player] = (player.walkSpeed.Value, player.sprintSpeed.Value);
                player.walkSpeed.Value = 0f;
                player.sprintSpeed.Value = 0f;
            }
            runner.SetGlobalInvul(true);
            return state;
        }

        void Unfreeze(FreezeState state) {
            LocalNPC.WalkSpeedMultiplier = state.walk;
            LocalNPC.FleeSpeedMultiplier = state.flee;
            LocalNPC.AttackSpeedMultiplier = state.attack;
            MM_ServerRunner.RefreshNPCSpeeds();
            foreach (var (player, speeds) in state.players) {
                if (player == null)
                    continue;
                player.walkSpeed.Value = speeds.walk;
                player.sprintSpeed.Value = speeds.sprint;
            }
            runner.SetGlobalInvul(false);
        }
    }
}
