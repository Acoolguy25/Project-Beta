using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// A Flag Wars bot's brain. It runs on a server-spawned robot NPC and drives it through the shared
    /// <see cref="LocalNPC"/> (NavMesh movement, external destinations) with its own targeting turned
    /// off: every few tenths of a second it picks a goal from the flags (<see cref="CTF_BotRules.GoalFor"/>),
    /// walks there, looks for enemies it can actually hit, and shoots with <see cref="CTF_BotGun"/> after a
    /// tier-dependent reaction time and with tier-dependent aim error. Flag pickups, captures, returns
    /// and power-ups are the server runner's, exactly as for players: a bot only has to stand there.
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter), typeof(CTF_BotGun))]
    public sealed class CTF_Bot : MonoBehaviour {
        CTF_IBotWorld world;
        CTF_Actor actor;
        CTF_BotSkillTuning tier;
        CTF_BotBrainTuning brain;
        CTF_BotRole role;
        System.Random random;

        LocalNPC npc;
        NavMeshAgent agent;
        GameCharacter character;
        CTF_BotGun gun;

        CTF_BotGoal goal;
        Vector3 goalPoint;
        bool hasGoalPoint;
        float nextThink, nextScan, nextShot, nextPatrol, strafeUntil;
        Vector3 patrolPoint, strafePoint;
        bool hasPatrol;

        GameCharacter target;
        float targetSince, targetSeenAt;
        Vector3 targetLastPosition;
        float targetSampleTime;
        Vector3 targetVelocity;

        Vector3 progressFrom;
        float progressAt, unstickUntil;
        Vector3 unstickPoint;

        public CTF_BotGoal Goal => goal;
        public CTF_BotRole Role => role;
        public GameCharacter Target => target;

        /// <summary>Wires the brain to the match. Called once, right after the body is spawned and armed.</summary>
        public void Init(CTF_IBotWorld matchWorld, CTF_Actor self, CTF_BotSkillTuning skill, CTF_BotBrainTuning tuning, CTF_BotRole botRole, int seed) {
            world = matchWorld;
            actor = self;
            tier = skill;
            brain = tuning;
            role = botRole;
            random = new System.Random(seed);
            npc = GetComponent<LocalNPC>();
            agent = npc.agent;
            character = GetComponent<GameCharacter>();
            gun = GetComponent<CTF_BotGun>();
            // The match decides where to go and whom to shoot; the shared NPC only walks.
            npc.AutomaticTargeting = false;
            npc.AllowAttackTargetOverrides = false;
            npc.SetTargetingType(NPCTargetingType.None);
            progressFrom = transform.position;
            progressAt = Time.time;
            nextThink = Time.time + Random01() * brain.thinkSeconds;
        }

        /// <summary>A new role (the squad re-balances roles when the team changes).</summary>
        public void SetRole(CTF_BotRole botRole) {
            role = botRole;
            hasPatrol = false;
        }

        float Random01() => (float)random.NextDouble();

        Vector2 RandomDirection() {
            float angle = Random01() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        static Vector3 Flat(Vector2 v) => new(v.x, 0f, v.y);
        float Range(Vector2 range) => Mathf.Lerp(range.x, range.y, Random01());

        void Update() {
            if (world == null || character.IsDead || !character.IsSpawned) return;
            if (!world.Moving) {
                Stop();
                return;
            }
            float now = Time.time;
            if (now >= nextScan) {
                nextScan = now + brain.scanSeconds;
                Scan(now);
            }
            if (now >= nextThink) {
                nextThink = now + brain.thinkSeconds;
                Think();
            }
            Move(now);
            if (world.Fighting) Shoot(now);
        }

        void LateUpdate() {
            if (world == null || character.IsDead || target == null) return;
            // Face what it shoots at (the shared NPC faces its walking direction, which reads as shooting sideways).
            Vector3 look = target.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), brain.turnDegreesPerSecond * Time.deltaTime);
        }

        // ---------------------------------------------------------------- goals
        void Think() {
            CTF_Team mine = actor.Team, theirs = CTF_Rules.Other(mine);
            var situation = new CTF_BotSituation {
                role = role,
                carrying = world.CarrierOf(theirs) == actor,
                ourFlag = world.FlagState(mine),
                theirFlag = world.FlagState(theirs),
                rankToOurFlag = RankAmongTeam(world.FlagPosition(mine)),
                rankToTheirFlag = RankAmongTeam(world.FlagPosition(theirs)),
                wantsHealth = WantsHealth(out _),
            };
            CTF_BotGoal next = CTF_BotRules.GoalFor(situation, brain.chasers, brain.returners);
            if (next != goal) hasPatrol = false;
            goal = next;
            hasGoalPoint = GoalPoint(goal, out goalPoint);
        }

        /// <summary>Living teammates (players and bots) nearer than this bot to <paramref name="point"/>.</summary>
        int RankAmongTeam(Vector3 point) {
            float mine = (transform.position - point).sqrMagnitude;
            int nearer = 0;
            foreach (var other in world.Actors) {
                if (other == actor || other.Team != actor.Team || !other.IsAlive) continue;
                if ((other.Character.transform.position - point).sqrMagnitude < mine) nearer++;
            }
            return nearer;
        }

        bool WantsHealth(out Vector3 pad) {
            pad = default;
            long max = character.MaxHealth.Value;
            if (max <= 0 || character.Health.Value > max * tier.seekHealthBelow) return false;
            float best = brain.healthSearchRange * brain.healthSearchRange;
            bool found = false;
            var spots = world.Map.powerUps;
            for (int i = 0; i < spots.Length; i++) {
                if (spots[i] == null || spots[i].kind != CTF_PowerUpKind.Health || !world.PowerUpReady(i)) continue;
                float d = (spots[i].Position - transform.position).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                pad = spots[i].Position;
                found = true;
            }
            return found;
        }

        bool GoalPoint(CTF_BotGoal g, out Vector3 point) {
            CTF_Team mine = actor.Team, theirs = CTF_Rules.Other(mine);
            var map = world.Map;
            switch (g) {
                case CTF_BotGoal.CarryHome:
                    point = map.Stand(mine);
                    return true;
                case CTF_BotGoal.ReturnFlag:
                    point = world.FlagPosition(mine);
                    return true;
                case CTF_BotGoal.ChaseCarrier: {
                    var carrier = world.CarrierOf(mine);
                    point = carrier != null && carrier.IsAlive ? carrier.Character.transform.position : world.FlagPosition(mine);
                    return true;
                }
                case CTF_BotGoal.TakeFlag:
                    point = world.FlagPosition(theirs);
                    return true;
                case CTF_BotGoal.Escort: {
                    var carrier = world.CarrierOf(theirs);
                    if (carrier == null || !carrier.IsAlive) {
                        point = map.Stand(mine);
                        return true;
                    }
                    Transform body = carrier.Character.transform;
                    // Keep a few metres behind the carrier (between it and the enemy base), screening it from chasers.
                    Vector3 toEnemy = map.Stand(theirs) - body.position;
                    toEnemy.y = 0f;
                    point = body.position + (toEnemy.sqrMagnitude > 0.01f ? toEnemy.normalized : Vector3.forward) * brain.escortDistance;
                    return true;
                }
                case CTF_BotGoal.Heal:
                    return WantsHealth(out point);
                case CTF_BotGoal.Defend:
                    point = Patrol(map.Stand(mine), brain.defendRadius);
                    return true;
                case CTF_BotGoal.HoldMiddle:
                    point = Patrol((map.Stand(mine) + map.Stand(theirs)) * 0.5f, brain.middleRadius);
                    return true;
                default:
                    point = transform.position;
                    return false;
            }
        }

        /// <summary>A spot near <paramref name="centre"/>, kept for a few seconds, on the NavMesh.</summary>
        Vector3 Patrol(Vector3 centre, float radius) {
            if (hasPatrol && Time.time < nextPatrol) return patrolPoint;
            nextPatrol = Time.time + Range(brain.patrolSeconds);
            Vector3 candidate = centre + Flat(RandomDirection() * (radius * Mathf.Sqrt(Random01())));
            patrolPoint = NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, agent.areaMask) ? hit.position : centre;
            hasPatrol = true;
            return patrolPoint;
        }

        // ---------------------------------------------------------------- movement
        void Move(float now) {
            float speed = tier.moveSpeed * world.SpeedMultiplier(actor);
            if (now < unstickUntil) {
                npc.MoveTo(unstickPoint, speed);
                return;
            }
            Vector3 destination;
            // A bot with a flag to take, carry, return or chase shoots on the move; only one holding ground stops to fight.
            bool engaged = target != null && !CTF_BotRules.ShootsOnTheMove(goal);
            if (engaged && world.Fighting) {
                // Short guns close in; the rest sidestep (or, for a recruit, stand and shoot).
                if (gun.Range < brain.closeInBelowRange) destination = target.transform.position;
                else if (tier.strafes) destination = Strafe(now);
                else {
                    Stop();
                    TrackProgress(now, false);
                    return;
                }
            } else if (hasGoalPoint) {
                destination = goalPoint;
            } else {
                Stop();
                TrackProgress(now, false);
                return;
            }
            npc.MoveTo(destination, speed);
            TrackProgress(now, (destination - transform.position).sqrMagnitude > brain.stuckDistance * brain.stuckDistance * 4f);
        }

        Vector3 Strafe(float now) {
            if (now < strafeUntil) return strafePoint;
            strafeUntil = now + Range(brain.strafeSeconds);
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward);
            float sign = Random01() < 0.5f ? -1f : 1f;
            Vector3 candidate = transform.position + side * (sign * brain.strafeDistance);
            strafePoint = NavMesh.SamplePosition(candidate, out NavMeshHit hit, brain.strafeDistance, agent.areaMask) ? hit.position : transform.position;
            return strafePoint;
        }

        /// <summary>Notices when the bot has not moved for a while although it wants to, and sends it a few metres aside.</summary>
        void TrackProgress(float now, bool wantsToMove) {
            if ((transform.position - progressFrom).sqrMagnitude >= brain.stuckDistance * brain.stuckDistance || !wantsToMove) {
                progressFrom = transform.position;
                progressAt = now;
                return;
            }
            if (now - progressAt < brain.stuckSeconds) return;
            progressAt = now;
            Vector3 candidate = transform.position + Flat(RandomDirection() * brain.unstickDistance);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, brain.unstickDistance, agent.areaMask)) return;
            unstickPoint = hit.position;
            unstickUntil = now + brain.stuckSeconds * 0.75f;
        }

        void Stop() {
            if (npc.TargetingType != NPCTargetingType.None) npc.SetTargetingType(NPCTargetingType.None);
        }

        // ---------------------------------------------------------------- targets
        void Scan(float now) {
            if (target != null) {
                if (target.IsDead || !target.IsSpawned) target = null;
                else SampleTargetVelocity(now);
            }
            float reach = Mathf.Min(tier.sightRange, gun.Range);
            GameCharacter best = null;
            float bestDistance = float.MaxValue;
            bool bestIsCarrier = false;
            var ourCarrier = world.CarrierOf(actor.Team);
            foreach (var other in world.Actors) {
                if (other.Team == actor.Team || !other.IsAlive) continue;
                var body = other.Character;
                float distance = Vector3.Distance(transform.position, body.transform.position);
                if (distance > reach || !Notices(other, distance, now) || !gun.HasClearShot(body, AimCentre(body))) continue;
                // Whoever carries our flag comes first.
                bool carrier = other == ourCarrier;
                if (bestIsCarrier && !carrier) continue;
                if (!carrier || bestIsCarrier) {
                    if (distance >= bestDistance) continue;
                }
                best = body;
                bestDistance = distance;
                bestIsCarrier = carrier;
            }

            if (best == target) {
                if (best != null) targetSeenAt = now;
                else if (target != null && now - targetSeenAt > brain.targetMemorySeconds) target = null;
                return;
            }
            if (best == null) {
                if (target != null && now - targetSeenAt > brain.targetMemorySeconds) target = null;
                return;
            }
            // Keep shooting the current one unless the new one is our carrier or much closer.
            if (target != null && now - targetSeenAt <= brain.targetMemorySeconds && !bestIsCarrier) {
                float current = Vector3.Distance(transform.position, target.transform.position);
                if (bestDistance > current * brain.switchTargetCloser) return;
            }
            target = best;
            targetSince = now;
            targetSeenAt = now;
            targetLastPosition = best.transform.position;
            targetSampleTime = now;
            targetVelocity = Vector3.zero;
        }

        /// <summary>In front (the field of view), close enough to hear, or it just shot this bot.</summary>
        bool Notices(CTF_Actor enemy, float distance, float now) {
            if (distance <= brain.nearbySense) return true;
            foreach (var (attackerId, time) in actor.RecentAttackers)
                if (attackerId == enemy.Id && now - time <= brain.hurtMemorySeconds) return true;
            Vector3 to = enemy.Character.transform.position - transform.position;
            to.y = 0f;
            return to.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, to) <= brain.viewDegrees * 0.5f;
        }

        void SampleTargetVelocity(float now) {
            float dt = now - targetSampleTime;
            if (dt < 0.05f) return;
            Vector3 position = target.transform.position;
            targetVelocity = (position - targetLastPosition) / dt;
            targetLastPosition = position;
            targetSampleTime = now;
        }

        Vector3 AimCentre(GameCharacter body) => body.transform.position + Vector3.up * brain.aimHeight;

        void Shoot(float now) {
            if (target == null || target.IsDead || now < nextShot || now < targetSince + tier.reactionSeconds) return;
            Vector3 centre = AimCentre(target);
            Vector3 line = centre - transform.position;
            float distance = line.magnitude;
            if (distance < 0.1f) return;
            Vector3 forward = line / distance;
            // How fast the target crosses the line of fire (moving straight at us or away is easy to hit).
            Vector3 crossing = targetVelocity - Vector3.Dot(targetVelocity, forward) * forward;
            float error = CTF_BotRules.AimErrorDegrees(tier, crossing.magnitude, world.SprintSpeed);
            float miss = CTF_BotRules.MissRadius(error, distance) * Mathf.Sqrt(Random01());
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);
            float angle = Random01() * Mathf.PI * 2f;
            Vector3 aim = centre + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * miss;
            if (gun.TryFire(target, aim)) nextShot = now + tier.shotHesitation * Random01();
        }
    }
}
