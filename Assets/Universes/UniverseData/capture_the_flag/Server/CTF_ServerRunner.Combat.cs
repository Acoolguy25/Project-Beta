using System;
using System.Collections.Generic;
using FishNet;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    // Hits, kills and medals: players and bots go through the same path.
    public sealed partial class CTF_ServerRunner {
        /// <summary>
        /// Watches one body: who last damaged it (so the health drop that follows is reported to the
        /// attacker as a confirmed hit and counts toward assists) and its death. Lives as long as the
        /// body; the subscriptions die with it.
        /// </summary>
        sealed class DamageWatch {
            readonly CTF_ServerRunner runner;
            readonly CTF_Actor owner;
            readonly GameCharacter body;
            IEntity attacker;

            public DamageWatch(CTF_ServerRunner runner, CTF_Actor owner, GameCharacter body) {
                this.runner = runner;
                this.owner = owner;
                this.body = body;
                body.OnDamage += OnDamage;
                body.Health.OnChange += OnHealthChanged;
                body.OnDied += OnDied;
            }

            void OnDamage(DamageType type, IEntity source) => attacker = source;

            void OnHealthChanged(long previous, long next, bool asServer) {
                if (!asServer || next >= previous || attacker == null) return;
                var source = attacker;
                attacker = null;
                runner.ReportHit(source, owner, body, previous - next, next == 0);
            }

            void OnDied(DamageType source, IEntity killer) {
                // A body dies once: let go of all three, so the dead body holds no reference to the runner.
                runner.watches.Remove(body);
                Release();
                runner.OnBodyDied(owner, body, source, killer);
            }

            /// <summary>Unsubscribes all three handlers (on death, and when the body leaves the match alive: despawn, leave, bot removed).</summary>
            public void Release() {
                if (body == null) return;
                body.OnDied -= OnDied;
                body.OnDamage -= OnDamage;
                if (body.Health != null) body.Health.OnChange -= OnHealthChanged;
            }
        }

        readonly List<CTF_Medal> medalScratch = new();
        readonly List<GameCharacter> staleBodies = new();
        readonly Dictionary<GameCharacter, DamageWatch> watches = new();

        /// <summary>A body no longer speaks for its actor: drop it and its damage watch.</summary>
        void Forget(GameCharacter body) {
            if (body == null) return;
            bodies.Remove(body);
            if (watches.Remove(body, out var watch)) watch.Release();
        }
        bool firstBlood;

        /// <summary>A body joined the match: kills on and by it are credited to <paramref name="actor"/>.</summary>
        void Watch(CTF_Actor actor, GameCharacter body) {
            if (actor == null || body == null) return;
            // The previous body (dead, or despawned) no longer speaks for this actor.
            staleBodies.Clear();
            foreach (var pair in bodies) if (pair.Value == actor && pair.Key != body) staleBodies.Add(pair.Key);
            foreach (var old in staleBodies) Forget(old);
            bodies[body] = actor;
            actor.Character = body;
            if (!watches.ContainsKey(body)) watches[body] = new DamageWatch(this, actor, body);
        }

        CTF_Actor ActorOfEntity(IEntity entity) =>
            entity is GameCharacter body && body != null && bodies.TryGetValue(body, out var actor) ? actor : null;

        void ReportHit(IEntity attacker, CTF_Actor victim, GameCharacter body, long damage, bool killed) {
            var shooter = ActorOfEntity(attacker);
            if (shooter == null || victim == null || body == null) return;
            if (shooter != victim && shooter.Team != victim.Team) victim.NoteAttacker(shooter.Id, Time.time);
            InstanceFinder.ServerManager.Broadcast(new CTF_HitBroadcast {
                attackerId = shooter.Id,
                victimId = victim.Id,
                position = body.transform.position,
                damage = (int)Math.Min(damage, int.MaxValue),
                killed = killed
            });
        }

        void OnBodyDied(CTF_Actor victim, GameCharacter body, DamageType source, IEntity killer) {
            Vector3 at = body != null ? body.transform.position : Vector3.zero;
            var killerActor = ActorOfEntity(killer);
            bool credited = killerActor != null && killerActor != victim && killerActor.Team != victim.Team;
            // Medal facts are read before the victim's flag falls.
            CTF_KillContext context = credited ? KillContext(killerActor, victim, at) : default;
            DropFlagsOf(victim, body != null ? at : (Vector3?)null);
            victim.Medals.Died();
            BroadcastKill(victim, credited ? killerActor : null, killerActor == null && killer is GameCharacter other ? other.DisplayName : null, source);
            if (victim.IsBot) squad.Died(victim);
            if (phase != CTF_Phase.Playing) return;

            victim.Stats.deaths++;
            if (!victim.IsBot) PlayerProgress.Modify<CTF_SaveData>(victim.Connection, save => save.deaths++);
            if (!credited) return;
            victim.LastKillerId = killerActor.Id;
            firstBlood = true;
            CreditKill(killerActor, context);
            CreditAssists(killerActor, victim);
        }

        CTF_KillContext KillContext(CTF_Actor killer, CTF_Actor victim, Vector3 at) {
            CTF_Team mine = killer.Team, theirs = victim.Team;
            Flag ourFlag = flags[(int)mine], theirFlag = flags[(int)theirs];
            var context = new CTF_KillContext {
                firstOfMatch = !firstBlood,
                victimCarriedOurFlag = ourFlag.state == CTF_FlagState.Carried && ourFlag.carrier == victim,
                nearOwnStand = map != null && CTF_Rules.InReach(at, map.Stand(mine), medalTuning.defendRadius),
                revenge = killer.LastKillerId == victim.Id,
            };
            // Bodyguard: our carrier (holding their flag) was close by, and it was not the killer itself.
            var carrier = theirFlag.state == CTF_FlagState.Carried ? theirFlag.carrier : null;
            context.protectedCarrier = carrier != null && carrier != killer && carrier.IsAlive
                                       && CTF_Rules.InReach(at, carrier.Character.transform.position, medalTuning.bodyguardRadius);
            return context;
        }

        void CreditKill(CTF_Actor killer, in CTF_KillContext context) {
            killer.Stats.kills++;
            medalScratch.Clear();
            bool multi = killer.Medals.Kill(Time.time, context, medalTuning, medalScratch);
            if (context.victimCarriedOurFlag) killer.Stats.carrierKills++;
            bool defend = medalScratch.Contains(CTF_Medal.Defender) || medalScratch.Contains(CTF_Medal.Bodyguard);
            if (defend) killer.Stats.defends++;
            KillRewards(killer, context.victimCarriedOurFlag, defend, multi);
            foreach (var medal in medalScratch) AwardMedal(killer, medal);
            if (!killer.IsAlive) return;
            switch (killer.LivePerk) {
                // Second Wind: every elimination patches the killer up.
                case CTF_Perk.SecondWind:
                    Heal(killer, loadoutTuning.secondWindHeal, CTF_Perk.SecondWind);
                    break;
                // Adrenaline: every elimination is a short burst of speed (the speed power-up's multiplier).
                case CTF_Perk.Adrenaline:
                    killer.BoostedUntil = Math.Max(killer.BoostedUntil, Time.time + loadoutTuning.adrenalineSeconds);
                    RefreshSpeed(killer);
                    PerkFired(killer, CTF_Perk.Adrenaline, loadoutTuning.adrenalineSeconds);
                    break;
            }
        }

        /// <summary>Heals <paramref name="actor"/> by up to <paramref name="amount"/> and shows <paramref name="perk"/> doing it.</summary>
        void Heal(CTF_Actor actor, long amount, CTF_Perk perk) {
            if (!actor.IsAlive) return;
            var body = actor.Character;
            long heal = CTF_Rules.HealAmount(body.Health.Value, body.MaxHealth.Value, amount);
            if (heal <= 0) return;
            body.HealHealth(heal);
            PerkFired(actor, perk, 0f);
        }

        /// <summary>Field Medic: a flag play (grab, return, capture) heals to full.</summary>
        void FieldMedic(CTF_Actor actor) {
            if (actor != null && actor.LivePerk == CTF_Perk.FieldMedic && actor.IsAlive)
                Heal(actor, actor.Character.MaxHealth.Value, CTF_Perk.FieldMedic);
        }

        void PerkFired(CTF_Actor actor, CTF_Perk perk, float seconds) =>
            InstanceFinder.ServerManager.Broadcast(new CTF_PerkBroadcast {
                actorId = actor.Id, perk = perk, position = actor.Character.transform.position, seconds = seconds,
            });

        CTF_Actor ActorById(int id) {
            foreach (var actor in actors) if (actor.Id == id) return actor;
            return null;
        }

        /// <summary>Teammates of the killer who hurt the victim within the assist window get an assist.</summary>
        void CreditAssists(CTF_Actor killer, CTF_Actor victim) {
            float since = Time.time - medalTuning.assistWindow;
            foreach (var (attackerId, time) in victim.RecentAttackers) {
                if (attackerId == killer.Id || time < since) continue;
                var helper = ActorById(attackerId);
                if (helper == null || helper.Team != killer.Team) continue;
                helper.Stats.assists++;
                AwardMedal(helper, CTF_Medal.Assist);
            }
            victim.RecentAttackers.Clear();
        }

        /// <summary>
        /// Tells everyone who eliminated whom. <paramref name="killer"/> is the credited actor, if any;
        /// <paramref name="otherKillerName"/> names something that is not in the match (never credited).
        /// </summary>
        void BroadcastKill(CTF_Actor victim, CTF_Actor killer, string otherKillerName, DamageType source) {
            InstanceFinder.ServerManager.Broadcast(new CTF_KillBroadcast {
                killerId = killer != null ? killer.Id : -1,
                killerName = killer != null ? NameOf(killer) : otherKillerName ?? "",
                killerTeam = killer != null ? killer.Team : CTF_Rules.Other(victim.Team),
                victimId = victim.Id,
                victimName = NameOf(victim),
                victimTeam = victim.Team,
                cause = Cause(source),
                respawnSeconds = victim.IsBot ? respawnSeconds : ServerPlayerCharacter.RespawnTime,
                weapon = killer != null ? killer.LiveWeapon : CTF_Weapon.Sidearm,
                killerMastered = Mastered(killer),
            });
            RosterDirty();
        }

        static CTF_KillCause Cause(DamageType source) => source switch {
            DamageType.Gun => CTF_KillCause.Gun,
            DamageType.Melee => CTF_KillCause.Melee,
            DamageType.Fall or DamageType.Despawn => CTF_KillCause.Fall,
            _ => CTF_KillCause.Other
        };

        /// <summary>
        /// Gives <paramref name="actor"/> a medal: counted for the MVP and the medal challenge, its XP paid
        /// (players), and shown to everyone (the earner gets the big popup, others a toast for major ones).
        /// </summary>
        void AwardMedal(CTF_Actor actor, CTF_Medal medal) {
            actor.Stats.medals++;
            int xp = CTF_MedalTracker.Xp(medal, medalTuning);
            if (!actor.IsBot) MedalRewards(actor, medal, xp);
            InstanceFinder.ServerManager.Broadcast(new CTF_MedalBroadcast {
                actorId = actor.Id,
                name = NameOf(actor),
                team = actor.Team,
                medal = medal,
                xp = actor.IsBot ? 0 : xp,
                streak = actor.Medals.Streak,
            });
        }
    }
}
