using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// The Classic round structure on top of <see cref="MM_ServerRunner"/>: role reveal, the hunt, body
    /// reports, emergency meetings with an accusation vote, ejections, and the end-of-round reveal.
    /// The runner still owns spawning, roles, tools and win conditions.
    /// </summary>
    public sealed class MM_ClassicRound {
        public const float ReportDistance = 6f;
        const float MarkerDistance = 30f;
        const float WitnessDistance = 40f;
        // The shared vote window fits three options in a row.
        const int MaxWitnesses = 3;
        const int MinSuspects = 3;
        public const int MeetingsPerRound = 3;
        const int RoleRevealSeconds = 6;
        const int DiscussionSeconds = 15;
        const int VoteSeconds = 25;
        const int EjectionSeconds = 6;
        const int ReportCooldownSeconds = 20;
        const int FirstReportDelaySeconds = 10;

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
        readonly List<Body> bodies = new();
        readonly List<Participant> participants = new();
        readonly Dictionary<GameCharacter, int> kills = new();
        int nextBodyId;
        int meetingsHeld;
        float reportAllowedAt;
        bool ejecting;
        UniTaskCompletionSource<Body> reportSignal = new();

        public int Round { get; private set; }
        public MM_Phase Phase { get; private set; } = MM_Phase.Lobby;
        public bool Active { get; private set; }
        public int SecondsLeft { get; set; }
        public int MeetingsLeft => Mathf.Max(0, MeetingsPerRound - meetingsHeld);

        public MM_ClassicRound(MM_ServerRunner runner) {
            this.runner = runner;
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
            Phase = MM_Phase.RoleReveal;
        }

        public void End() {
            Active = false;
            Phase = MM_Phase.Lobby;
            bodies.Clear();
            participants.Clear();
            kills.Clear();
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

        // ---------- deaths and reports ----------

        public void OnCharacterDied(GameCharacter victim, GameCharacter killer) {
            if (!Active || victim == null)
                return;
            if (killer != null && killer != victim)
                kills[killer] = kills.TryGetValue(killer, out int k) ? k + 1 : 1;
            if (ejecting)
                return;

            Vector3 position = victim.transform.position;
            // The registry only holds living characters, so this is "who was around".
            List<GameCharacter> witnesses = LivingCharacters()
                .Where(c => c != victim && (c.transform.position - position).sqrMagnitude <= WitnessDistance * WitnessDistance)
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
            if (body == null || (reporter.transform.position - body.position).sqrMagnitude > ReportDistance * ReportDistance * 1.5f)
                return;
            body.reported = true;
            pendingReporter = reporter;
            reportSignal.TrySetResult(body);
        }

        GameCharacter pendingReporter;

        // ---------- phases ----------

        public async UniTask RunRoleReveal(CancellationToken token) {
            Phase = MM_Phase.RoleReveal;
            for (int i = RoleRevealSeconds; i > 0; i--) {
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
            reportAllowedAt = Time.time + FirstReportDelaySeconds;
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
                    reportAllowedAt = Time.time + ReportCooldownSeconds;
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
                    discussionSeconds = DiscussionSeconds,
                });
                ServerChat.SendSystemMessage(new($"EMERGENCY MEETING: {reporterName} found the body of {body.victimName}. Seen nearby: {string.Join(", ", names)}.", SystemMessageSource.CustomMessage));
                for (int i = DiscussionSeconds; i > 0; i--) {
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
                SecondsLeft = VoteSeconds;
                string[] descriptions = suspects.Select(s => s.Owner.IsValid ? "Player" : "Townsperson").ToArray();
                VoteResult result = await ServerVote.StartDynamicVote(
                    $"Who killed {body.victimName}?",
                    $"Reported by {reporterName}. These were seen near the body. Most votes is ejected; a tie ejects nobody.",
                    names, descriptions, VoteSeconds, token, CanVote);

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
            InstanceFinder.ServerManager.Broadcast(ejection);
            for (int i = EjectionSeconds; i > 0; i--) {
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
            MM_StateBroadcast state = new() {
                round = Round,
                phase = Active ? Phase : MM_Phase.Lobby,
                classic = classic,
                secondsLeft = SecondsLeft,
                meetingsLeft = MeetingsLeft,
                reportCooldown = Mathf.Max(0, Mathf.CeilToInt(reportAllowedAt - Time.time)),
                reportableBody = -1,
                nearbyBodies = Array.Empty<Vector3>(),
            };
            bool alive = TryGetLivingCharacter(player.Owner, out GameCharacter character);
            state.alive = alive;
            // Death recolours the character, so prefer the role the player was dealt this round.
            Participant dealt = Active ? participants.FirstOrDefault(p => p.player == player) : null;
            MM_Role role = dealt != null ? dealt.role : RoleOf(player.team.Value.realTeam);
            state.role = Active && classic && role == MM_Role.None ? MM_Role.Spectator : role;
            if (!Active || !classic || !alive)
                return state;

            Vector3 at = character.transform.position;
            List<Vector3> markers = new();
            float best = float.MaxValue;
            foreach (Body body in bodies) {
                if (body.reported)
                    continue;
                float sqr = (body.position - at).sqrMagnitude;
                if (sqr <= MarkerDistance * MarkerDistance)
                    markers.Add(body.position);
                if (sqr <= ReportDistance * ReportDistance && sqr < best) {
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
