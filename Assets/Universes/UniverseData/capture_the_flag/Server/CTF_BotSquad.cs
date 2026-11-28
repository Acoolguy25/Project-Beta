using System;
using System.Collections.Generic;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// The bots of a match: how many each team needs (<see cref="CTF_BotRules.BotsFor"/>), their slots,
    /// names, guns and roles, and their bodies. Bodies are the shared server-owned robot NPC
    /// (<see cref="ServerNPC.SpawnNPC(NPCCharacter, Vector3?, UnityEngine.SceneManagement.Scene)"/>) with
    /// a <see cref="CTF_BotGun"/> and a <see cref="CTF_Bot"/> brain added; every life gets a fresh body,
    /// as a player does. The runner owns the rules (flags, kills, scoring); this only fields the bots.
    /// </summary>
    public sealed class CTF_BotSquad {
        /// <summary>What the squad needs from the match.</summary>
        public interface IHost {
            CTF_IBotWorld World { get; }
            long CharacterHealth { get; }
            float RespawnSeconds { get; }
            CTF_LoadoutTuning Loadouts { get; }
            /// <summary>Where a member of <paramref name="team"/> (re)spawns, and which way it faces.</summary>
            Pose SpawnPose(CTF_Team team);
            /// <summary>A fresh body is up (watch its damage and death).</summary>
            void BodySpawned(CTF_Actor bot);
            /// <summary>A bot left the match (a player took its place): drop anything it held.</summary>
            void BotRemoved(CTF_Actor bot);
            /// <summary>The roster changed: tell the clients.</summary>
            void RosterChanged();
        }

        readonly IHost host;
        readonly CTF_BotSquadTuning squad;
        readonly CTF_BotBrainTuning brain;
        readonly List<CTF_Actor> bots = new();
        readonly System.Random random = new();
        int seedCounter;

        public CTF_BotSquad(IHost host, CTF_BotSquadTuning squad, CTF_BotBrainTuning brain) {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.squad = squad ?? new CTF_BotSquadTuning();
            this.brain = brain ?? new CTF_BotBrainTuning();
            plan = Fixed(this.squad.fixedSkill);
        }

        static readonly CTF_Team[] Teams = { CTF_Team.Red, CTF_Team.Blue };
        CTF_BotPlan plan;

        public IReadOnlyList<CTF_Actor> Bots => bots;
        /// <summary>The tier both teams start from this match (a team's bots may play one above it, see <see cref="SkillOf"/>).</summary>
        public CTF_BotSkill Skill => plan.baseSkill;
        public CTF_BotPlan Plan => plan;
        /// <summary>The tier of <paramref name="team"/>'s bots this match.</summary>
        public CTF_BotSkill SkillOf(CTF_Team team) => plan.SkillOf(team);
        public int TeamSize => squad.teamSize;
        public bool Fills => squad.fillWithBots;
        /// <summary>QA baseline: bots on both teams, players wait in the lobby.</summary>
        public bool BotsOnly => squad.qaBotsOnly && squad.fillWithBots;

        static CTF_BotPlan Fixed(CTF_BotSkill skill) => new() { baseSkill = skill, red = skill, blue = skill };

        /// <summary>
        /// Picks each team's tier and size for the next match from how its players stand (see
        /// <see cref="CTF_BotRules.Plan"/>); <paramref name="toughBonus"/> is the daily twist's extra tiers.
        /// </summary>
        public CTF_BotPlan ChooseSkill(in CTF_TeamStanding red, in CTF_TeamStanding blue, int toughBonus) {
            // The QA baseline plays fixedSkill on both sides, so a probe can set the tier it measures.
            plan = squad.autoSkill && !BotsOnly
                ? CTF_BotRules.Plan(red, blue, squad.PlanTuning(), toughBonus)
                : Fixed(squad.fixedSkill);
            return plan;
        }

        /// <summary>QA: every bot plays <paramref name="skill"/> (the bots-only baseline).</summary>
        public void ForceSkill(CTF_BotSkill skill) => plan = Fixed(skill);

        /// <summary>Saved tier for a player at <paramref name="level"/> after a match ending <paramref name="margin"/> for them.</summary>
        public int NextTier(int tier, int margin, float level) =>
            CTF_BotRules.NextTier(tier, margin, squad.adjustMargin, CTF_BotRules.Ceiling(level, squad.skillLevels));

        int Count(CTF_Team team) {
            int n = 0;
            foreach (var bot in bots) if (bot.Team == team && !bot.Retired) n++;
            return n;
        }

        /// <summary>
        /// Adds or removes bots so each team fields the team size beside its <paramref name="redHumans"/>
        /// and <paramref name="blueHumans"/>. With <paramref name="spawn"/> new bots get a body at once.
        /// A bot carrying a flag is not pulled off the field: it retires and leaves when it dies.
        /// </summary>
        public void Rebalance(int redHumans, int blueHumans, bool spawn) {
            bool changed = false;
            foreach (var team in Teams) {
                int want = CTF_BotRules.BotsFor(team == CTF_Team.Red ? redHumans : blueHumans, squad.teamSize, squad.fillWithBots, plan.ShortOf(team));
                while (Count(team) < want) {
                    Add(team, spawn);
                    changed = true;
                }
                while (Count(team) > want) {
                    Remove(PickToRemove(team));
                    changed = true;
                }
            }
            if (!changed) return;
            AssignRoles(redHumans, blueHumans);
            host.RosterChanged();
        }

        void Add(CTF_Team team, bool spawn) {
            int slot = 0;
            while (SlotTaken(slot)) slot++;
            var tier = squad.Tier(SkillOf(team));
            // The gun cycles by the bot's place in its own team, not its global slot: with global slots Red
            // (slots 0-2, 6) and Blue (3-5, 7) carried different gun mixes from Regular up (try-2 baseline).
            var bot = CTF_Actor.Bot(slot, team, CTF_BotRules.NameFor(slot, squad.nameFormat), CTF_BotRules.WeaponFor(tier, Count(team)));
            bots.Add(bot);
            if (spawn) Spawn(bot);
        }

        /// <summary>The bot's place among its team's bots, in the order they joined.</summary>
        int TeamIndex(CTF_Actor bot) {
            int index = 0;
            foreach (var other in bots) {
                if (other == bot) return index;
                if (other.Team == bot.Team) index++;
            }
            return index;
        }

        bool SlotTaken(int slot) {
            foreach (var bot in bots) if (bot.BotSlot == slot) return true;
            return false;
        }

        /// <summary>A dead bot first, then the newest; one carrying a flag only if nothing else is left.</summary>
        CTF_Actor PickToRemove(CTF_Team team) {
            CTF_Actor pick = null;
            for (int i = bots.Count - 1; i >= 0; i--) {
                var bot = bots[i];
                if (bot.Team != team || bot.Retired) continue;
                if (!bot.IsAlive) return bot;
                if (pick == null || (IsCarrier(pick) && !IsCarrier(bot))) pick = bot;
            }
            return pick;
        }

        bool IsCarrier(CTF_Actor bot) {
            var world = host.World;
            return world != null && (world.CarrierOf(CTF_Team.Red) == bot || world.CarrierOf(CTF_Team.Blue) == bot);
        }

        void Remove(CTF_Actor bot) {
            if (bot == null) return;
            if (bot.IsAlive && IsCarrier(bot)) {
                bot.Retired = true;
                return;
            }
            bots.Remove(bot);
            host.BotRemoved(bot);
            DespawnBody(bot);
        }

        /// <summary>Gives every bot its role for the team it is on (see <see cref="CTF_BotRules.RoleFor"/>).</summary>
        void AssignRoles(int redHumans, int blueHumans) {
            foreach (var team in Teams) {
                int humans = team == CTF_Team.Red ? redHumans : blueHumans;
                int total = Count(team), index = 0;
                foreach (var bot in bots) {
                    if (bot.Team != team || bot.Retired) continue;
                    bot.Role = CTF_BotRules.RoleFor(index++, total, humans);
                    if (bot.Character != null && bot.Character.TryGetComponent(out CTF_Bot brainOf)) brainOf.SetRole(bot.Role);
                }
            }
        }

        /// <summary>A new match: every bot takes its team's tier's gun and a fresh body at its team's spawn.</summary>
        public void SpawnAll() {
            foreach (var bot in bots) {
                bot.BotWeapon = CTF_BotRules.WeaponFor(squad.Tier(SkillOf(bot.Team)), TeamIndex(bot));
                bot.ResetMatch();
                Spawn(bot);
            }
            host.RosterChanged();
        }

        /// <summary>A bot's body died: it comes back after the respawn time, or leaves if it was retired.</summary>
        public void Died(CTF_Actor bot) {
            if (bot == null || !bots.Contains(bot)) return;
            if (bot.Retired) {
                bots.Remove(bot);
                host.BotRemoved(bot);
                host.RosterChanged();
                // The body lies there a moment like anyone else's; Tick clears it with the others.
                corpses.Add((bot.Character, Time.time + host.RespawnSeconds));
                bot.Character = null;
                return;
            }
            bot.RespawnAt = Time.time + host.RespawnSeconds;
        }

        readonly List<(GameCharacter body, float at)> corpses = new();

        /// <summary>Respawns bots whose time has come and clears old bodies. Call every frame while bots may play.</summary>
        public void Tick(float now) {
            for (int i = corpses.Count - 1; i >= 0; i--) {
                if (now < corpses[i].at) continue;
                Despawn(corpses[i].body);
                corpses.RemoveAt(i);
            }
            foreach (var bot in bots) {
                bool due = bot.RespawnAt > 0f && now >= bot.RespawnAt;
                // A body cleared by someone else (the shared NPC reset) is replaced at once.
                bool lost = bot.RespawnAt <= 0f && (bot.Character == null || !bot.Character.IsSpawned);
                if (due || lost) Spawn(bot);
            }
        }

        /// <summary>Despawns every bot body and forgets the bots (the server is stopping or bots were turned off).</summary>
        public void Clear() {
            foreach (var bot in bots) DespawnBody(bot);
            bots.Clear();
            foreach (var corpse in corpses) Despawn(corpse.body);
            corpses.Clear();
            host.RosterChanged();
        }

        void Spawn(CTF_Actor bot) {
            DespawnBody(bot);
            bot.RespawnAt = 0f;
            bot.NewLife();
            Pose pose = host.SpawnPose(bot.Team);
            LocalNPC npc;
            try {
                npc = ServerNPC.SpawnNPC(NPCCharacter.Robot, pose.position);
            } catch (Exception e) {
                Debug.LogException(e);
                bot.RespawnAt = Time.time + host.RespawnSeconds; // try again later rather than every frame
                return;
            }
            var body = npc.GetComponent<GameCharacter>();
            TeamColor color = bot.Team == CTF_Team.Red ? TeamColor.Red : TeamColor.Blue;
            body.Init(host.CharacterHealth);
            body.SetTeam(new TeamConfig(color));
            body.DisplayName = bot.BotName;
            if (body.TryGetComponent(out RobotColor paint)) paint.ApplyColor(color);
            body.transform.rotation = pose.rotation;
            PlaceOnNavMesh(npc.agent, pose.position);

            var stats = host.Loadouts.StatsFor(bot.BotWeapon, CTF_Perk.None);
            bot.LiveWeapon = bot.BotWeapon;
            bot.LivePerk = CTF_Perk.None;
            var pistol = ServerTool.Instance != null ? ServerTool.Instance.SpawnTool(body.NetworkObject, ToolEnum.Pistol) : null;
            CTF_GunSetup.ApplyReplicated(pistol, stats);
            var gun = body.gameObject.AddComponent<CTF_BotGun>();
            gun.Arm(pistol, stats);
            var mind = body.gameObject.AddComponent<CTF_Bot>();
            mind.Init(host.World, bot, squad.Tier(SkillOf(bot.Team)), brain, bot.Role, unchecked(random.Next() + seedCounter++));
            bot.Character = body;
            host.BodySpawned(bot);
        }

        void PlaceOnNavMesh(NavMeshAgent agent, Vector3 position) {
            if (agent == null) return;
            if (!agent.enabled) agent.enabled = true;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, brain.navMeshSnap, agent.areaMask)) agent.Warp(hit.position);
            else Debug.LogWarning($"Flag Wars: no NavMesh within {brain.navMeshSnap} m of bot spawn {position}; the bot cannot walk. " +
                                  "Rebuild the arena with Ryan > Flag Wars > Build Client Assets (it bakes the NavMesh).");
        }

        void DespawnBody(CTF_Actor bot) {
            var body = bot.Character;
            bot.Character = null;
            Despawn(body);
        }

        static void Despawn(GameCharacter body) {
            if (body == null || !body.IsSpawned) return;
            if (ServerTool.Instance != null) ServerTool.Instance.DespawnTools(body.NetworkObject);
            body.Despawn();
        }
    }
}
