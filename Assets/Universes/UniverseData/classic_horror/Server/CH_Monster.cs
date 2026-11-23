using System.Collections.Generic;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>
    /// Father Crane's hunt, layered over LocalNPC navigation. He patrols towards the investigators, sees in a cone ahead
    /// of him (much further when your flashlight is on), hears footsteps through walls and always hears his own bell.
    /// A chase aims where his quarry is going, lunges when close, and does not give up the moment he loses sight: he
    /// keeps going where they were heading, then sweeps the area, opening hiding places he watched someone climb into.
    /// When nobody has been near him for a while he rises out of the water somewhere closer.
    /// </summary>
    public sealed class CH_Monster : MonoBehaviour {
        public enum Mode { Patrol, Chase, Search, OpenHidingPlace, Feed, Held }

        const float ThinkInterval = 0.1f;
        const float NavigationRadius = 0.25f, NavigationHeight = 1.9f;
        const float SamplePositionRadius = 3f;
        static readonly Vector3 HitboxSize = new(0.5f, 1.9f, 0.5f), HitboxCenter = new(0, 0.95f, 0);

        CH_ServerRunner runner;
        CH_MonsterTuning tuning;
        LocalNPC locomotion;
        NavMeshAgent agent;
        GameCharacter entity;
        System.Random random;
        int obstacleMask;

        readonly Dictionary<LocalCharacter, Vector3> previousPositions = new();
        readonly Dictionary<LocalCharacter, float> speeds = new();
        /// <summary>When he last saw or heard each investigator: hiding right after means he watched them go in.</summary>
        readonly Dictionary<LocalCharacter, float> lastSensed = new();
        readonly List<LocalCharacter> stale = new();
        readonly List<Vector3> sweepPoints = new();

        public Mode State { get; private set; } = Mode.Patrol;
        public LocalCharacter Quarry { get; private set; }
        public bool IsChasing => State == Mode.Chase;
        /// <summary>Chasing, sweeping, or on his way to drag someone out: the chase music keeps playing.</summary>
        public bool IsHunting => State is Mode.Chase or Mode.Search or Mode.OpenHidingPlace;
        public int Catches { get; private set; }
        public int ChasesStarted { get; private set; }
        public int ChasesEscaped { get; private set; }

        Vector3 lastKnownPosition, lastKnownVelocity, patrolPoint;
        float lostAt = -1f, searchUntil, heldUntil, feedUntil, nextThink, lastThink;
        float lungeUntil, nextLunge, nextAttack, nextTaunt, nextRepath, lastSensedAnyone, nextRelocate, patrolUntil;
        float progressCheckAt;
        Vector3 progressPosition;
        int tauntIndex;
        bool suspended;
        CH_HidingSpot targetHidingPlace;
        LocalCharacter hidingQuarry;
        float openingGivesUpAt;
        // Reused for reachability checks, which run outside the per-frame path.
        NavMeshPath probePath;
        readonly HashSet<CH_HidingSpot> checkedHidingPlaces = new();

        public void Initialize(CH_ServerRunner owner, LocalNPC npc, int seed, CH_MonsterTuning hunt) {
            runner = owner;
            locomotion = npc;
            tuning = hunt;
            agent = npc.agent;
            entity = GetComponent<GameCharacter>();
            random = new System.Random(seed ^ 0x5eed);
            locomotion.AutomaticTargeting = false;
            agent.radius = NavigationRadius;
            agent.height = NavigationHeight;
            agent.acceleration = 40f;
            agent.angularSpeed = 540f;
            agent.stoppingDistance = CH_MonsterTuning.AgentStoppingDistance;
            agent.autoBraking = false;
            agent.autoRepath = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            if (TryGetComponent(out BoxCollider hitbox)) {
                hitbox.size = HitboxSize;
                hitbox.center = HitboxCenter;
            }
            if (TryGetComponent(out CharacterAnimator animator)) animator.FootstepPaceMultiplier = 2.2f;
            locomotion.SetTargetingType(NPCTargetingType.External);
            obstacleMask = ~LayerMask.GetMask("Character", "LocalCharacter", "Ignore Raycast", "UI", "Water");
            lastThink = Time.time;
            lastSensedAnyone = Time.time;
            nextRelocate = Time.time + tuning.relocateCooldown;
            nextTaunt = Time.time + 8f;
            progressPosition = transform.position;
        }

        /// <summary>Freezes him where he stands (a cutscene, a scare on someone's screen).</summary>
        public void Hold(float seconds) {
            heldUntil = Mathf.Max(heldUntil, Time.time + seconds);
            Stop();
        }

        public void Release() => heldUntil = 0f;

        /// <summary>Stops him for a scene or the end of the night. He forgets the hunt, so no chase carries across.</summary>
        public void Suspend() {
            suspended = true;
            Stop();
            ForgetQuarry();
            targetHidingPlace = null;
            hidingQuarry = null;
            State = Mode.Patrol;
            patrolUntil = 0f;
        }

        public void Resume() { suspended = false; lastThink = Time.time; lastSensedAnyone = Time.time; }

        /// <summary>Puts him somewhere new at once (a cutscene's end, the director), forgetting the chase.</summary>
        public void Warp(Vector3 position) {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, SamplePositionRadius, agent.areaMask) && agent.enabled)
                agent.Warp(hit.position);
            ForgetQuarry();
            State = Mode.Patrol;
            patrolUntil = 0f;
            progressPosition = transform.position;
        }

        /// <summary>Starts a chase on <paramref name="target"/> at once, wherever they are (the bell is lifted).</summary>
        public void Hunt(LocalCharacter target) {
            if (target == null || target.IsDead) return;
            BeginChase(target);
            lastKnownPosition = target.transform.position;
        }

        void Stop() {
            if (agent != null && agent.enabled && agent.isOnNavMesh) {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
        }

        void Update() {
            if (runner == null || suspended || !runner.Hunting) return;
            if (Time.time < heldUntil) { Stop(); State = Mode.Held; return; }
            if (Time.time < nextThink) return;
            float elapsed = Mathf.Max(0.02f, Time.time - lastThink);
            lastThink = Time.time;
            nextThink = Time.time + ThinkInterval;
            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (State == Mode.Held) State = Quarry != null ? Mode.Chase : Mode.Patrol;
            if (State == Mode.Feed) {
                if (Time.time < feedUntil) { Stop(); return; }
                State = Mode.Patrol;
                patrolUntil = 0f;
            }

            TrackMovement(elapsed);
            LocalCharacter sensed = Sense(out float sensedDistance, out bool sensedVisible);
            if (sensed != null) lastSensedAnyone = Time.time;
            WatchHidingPlaces();

            if (State == Mode.OpenHidingPlace) {
                // Someone in plain sight matters more than a wardrobe.
                if (sensed != null && sensedVisible && !runner.IsHidden(sensed)) {
                    targetHidingPlace = null;
                    hidingQuarry = null;
                    BeginChase(sensed);
                    Chase(sensed, sensedDistance, sensedVisible);
                } else {
                    OpenHidingPlace();
                }
                CheckProgress();
                return;
            }
            if (sensed != null) {
                if (Quarry != sensed || State != Mode.Chase) BeginChase(sensed);
                Chase(sensed, sensedDistance, sensedVisible);
            } else if (State == Mode.Chase && Quarry != null) {
                Pursue();
            } else if (State == Mode.Search) {
                Sweep();
            } else {
                Patrol();
            }
            Taunt();
            CheckProgress();
        }

        // ---- Senses --------------------------------------------------------------------------------------------------

        void TrackMovement(float elapsed) {
            foreach (var character in LocalCharacter.Characters.Values) {
                if (character == null) continue;
                Vector3 position = character.transform.position;
                float speed = previousPositions.TryGetValue(character, out Vector3 previous)
                    ? Vector3.Distance(Flat(previous), Flat(position)) / elapsed : 0f;
                previousPositions[character] = position;
                // A little smoothing, so one jittery tick is not a sprint.
                speeds[character] = speeds.TryGetValue(character, out float old) ? Mathf.Lerp(old, speed, 0.5f) : speed;
            }
            stale.Clear();
            foreach (var pair in previousPositions) if (pair.Key == null || !pair.Key.IsSpawned) stale.Add(pair.Key);
            foreach (var character in stale) { previousPositions.Remove(character); speeds.Remove(character); lastSensed.Remove(character); }
        }

        bool IsEligible(LocalCharacter character) =>
            character != null && !character.IsDead && !character.IsProtected(entity, DamageType.Melee) && !runner.IsHidden(character);

        Vector3 Eye => entity.CharacterCamera != null ? entity.CharacterCamera.position : transform.position + Vector3.up * 1.8f;

        static Vector3 EyeOf(LocalCharacter character) =>
            character.CharacterCamera != null ? character.CharacterCamera.position : character.transform.position + Vector3.up * 1.6f;

        /// <summary>The investigator he notices most (closest, his current quarry first), or null.</summary>
        LocalCharacter Sense(out float bestDistance, out bool bestVisible) {
            LocalCharacter best = null;
            bestDistance = float.MaxValue;
            bestVisible = false;
            Vector3 eye = Eye;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (!IsEligible(character)) continue;
                Vector3 target = EyeOf(character);
                float distance = Vector3.Distance(eye, target);
                bool chasing = character == Quarry && State == Mode.Chase;
                bool lit = IsLit(character);
                bool bell = runner.IsCarryingBell(character);
                bool clear = distance < tuning.SightRange(true, true) && WorldInteraction.CanReach(eye, target, distance + 0.5f, obstacleMask);
                bool seen = clear && distance < tuning.SightRange(lit, chasing)
                    && (chasing || distance < tuning.closeSenseRadius || tuning.InSightCone(transform.forward, target - eye));
                speeds.TryGetValue(character, out float speed);
                bool heard = distance < tuning.HearingRange(speed, bell);
                bool close = distance < tuning.closeSenseRadius;
                if (!seen && !heard && !close) continue;
                lastSensed[character] = Time.time;
                // Keep the chase on the same person: anyone else must be clearly closer to steal it.
                float score = distance - (character == Quarry ? 6f : 0f) - (bell ? 4f : 0f);
                if (score >= bestDistance) continue;
                bestDistance = score;
                best = character;
                bestVisible = seen || (close && clear);
            }
            if (best != null) bestDistance = Vector3.Distance(eye, EyeOf(best));
            return best;
        }

        static bool IsLit(LocalCharacter character) {
            var light = ServerTool.Instance != null ? ServerTool.Instance.GetTool(character.NetworkObject, ToolEnum.Flashlight) as ToolFlashlightShared : null;
            return light != null && light.IsIlluminating;
        }

        /// <summary>Someone who climbs into a hiding place right after he saw or heard them was watched going in.</summary>
        void WatchHidingPlaces() {
            if (State == Mode.OpenHidingPlace) return;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (character == null || character.IsDead || !runner.IsHidden(character)) continue;
                if (!lastSensed.TryGetValue(character, out float sensedAt)) continue;
                CH_HidingSpot spot = runner.HidingPlaceOf(character);
                if (spot == null || Time.time - sensedAt > tuning.sawHideSeconds || runner.HiddenSince(character) < sensedAt - 0.05f) continue;
                // He saw that.
                lastSensed.Remove(character);
                if (BeginOpening(spot, character)) return;
                // He cannot get to it: he waits nearby for them to come out.
                lastKnownPosition = spot.Exit;
                lastKnownVelocity = Vector3.zero;
                BeginSearch();
                return;
            }
        }

        // ---- Chase -----------------------------------------------------------------------------------------------------

        void BeginChase(LocalCharacter target) {
            bool fresh = State != Mode.Chase || Quarry != target;
            Quarry = target;
            State = Mode.Chase;
            lostAt = -1f;
            checkedHidingPlaces.Clear();
            if (!fresh) return;
            ChasesStarted++;
            nextRepath = 0f;
            runner.OnChaseStarted(target);
        }

        void Chase(LocalCharacter target, float distance, bool visible) {
            lostAt = -1f;
            Vector3 position = target.transform.position;
            Vector3 velocity = previousPositions.ContainsKey(target) && speeds.TryGetValue(target, out float speed)
                ? Flat(position - lastKnownPosition).normalized * speed : Vector3.zero;
            if (lastKnownPosition != Vector3.zero) lastKnownVelocity = Vector3.Lerp(lastKnownVelocity, velocity, 0.5f);
            lastKnownPosition = position;

            float pace = tuning.ChaseSpeed(runner.Act, runner.IsCarryingBell(target), runner.LivingInvestigators);
            if (visible && distance < tuning.lungeRange && distance > tuning.strikeDistance && Time.time >= nextLunge) {
                lungeUntil = Time.time + tuning.lungeSeconds;
                nextLunge = Time.time + tuning.lungeCooldown;
            }
            if (Time.time < lungeUntil) pace = tuning.lungeSpeed;

            // Close in on where they are going, not where they were; right on top of them, go straight for them.
            Vector3 aim = distance < 4f ? position : position + lastKnownVelocity * tuning.predictionSeconds;
            Steer(aim, pace, Time.time >= nextRepath || distance < 4f);

            if (visible && distance < tuning.strikeDistance + 0.6f && Time.time >= nextAttack
                && Vector3.Distance(Flat(transform.position), Flat(position)) < tuning.strikeDistance) {
                nextAttack = Time.time + tuning.attackCooldown;
                Strike(target);
                return;
            }
            if (visible) runner.OnCloseEncounter(target, distance);
        }

        void Strike(LocalCharacter target) {
            Catches++;
            target.TakeDamage(System.Math.Max(1L, target.Health.Value), DamageType.Melee, (IEntity)entity);
            if (!target.IsDead) return;
            ForgetQuarry();
            State = Mode.Feed;
            feedUntil = Time.time + tuning.feedSeconds;
            Stop();
        }

        /// <summary>He has lost every sense of them: he keeps going where they were heading, then sweeps.</summary>
        void Pursue() {
            if (lostAt < 0f) lostAt = Time.time;
            float lostFor = Time.time - lostAt;
            if (lostFor > tuning.memorySeconds || Quarry == null || Quarry.IsDead) {
                BeginSearch();
                return;
            }
            Vector3 guess = lastKnownPosition + lastKnownVelocity * Mathf.Min(lostFor + tuning.predictionSeconds, 2.5f);
            Steer(guess, tuning.ChaseSpeed(runner.Act, false, runner.LivingInvestigators), Time.time >= nextRepath);
        }

        void BeginSearch() {
            if (Quarry != null) ChasesEscaped++;
            ForgetQuarry();
            State = Mode.Search;
            searchUntil = Time.time + tuning.searchSeconds;
            sweepPoints.Clear();
            for (int i = 0; i < 4; i++) {
                Vector2 offset = RandomInCircle() * tuning.searchRadius;
                Vector3 candidate = lastKnownPosition + lastKnownVelocity.normalized * (tuning.searchRadius * 0.4f) + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SamplePositionRadius, agent.areaMask)) sweepPoints.Add(hit.position);
            }
            runner.OnChaseLost();
        }

        void Sweep() {
            if (Time.time > searchUntil) {
                State = Mode.Patrol;
                patrolUntil = 0f;
                return;
            }
            // Passing a hiding place close to where he lost someone, he may open it.
            CH_HidingSpot near = runner.NearestHidingPlace(transform.position, 4f);
            if (near != null && checkedHidingPlaces.Add(near) && random.NextDouble() < tuning.checkHidingChance && BeginOpening(near, null))
                return;
            if (sweepPoints.Count == 0) { Steer(lastKnownPosition, tuning.searchSpeed, false); return; }
            Vector3 point = sweepPoints[0];
            if (Flat(point - transform.position).sqrMagnitude < 2.25f) { sweepPoints.RemoveAt(0); return; }
            Steer(point, tuning.searchSpeed, false);
        }

        /// <summary>Heads for a hiding place he can actually walk to, with a deadline; false if there is no complete path.</summary>
        bool BeginOpening(CH_HidingSpot spot, LocalCharacter quarry) {
            probePath ??= new NavMeshPath();
            if (!NavMesh.SamplePosition(spot.Exit, out NavMeshHit hit, SamplePositionRadius, agent.areaMask)
                || !agent.CalculatePath(hit.position, probePath) || probePath.status != NavMeshPathStatus.PathComplete) return false;
            targetHidingPlace = spot;
            hidingQuarry = quarry;
            openingGivesUpAt = Time.time + tuning.openHidingGiveUpSeconds;
            State = Mode.OpenHidingPlace;
            return true;
        }

        /// <summary>Walks to a hiding place and pulls it open; whoever is inside is caught.</summary>
        void OpenHidingPlace() {
            if (targetHidingPlace == null) { State = Mode.Patrol; return; }
            if (Time.time > openingGivesUpAt) {
                targetHidingPlace = null;
                hidingQuarry = null;
                State = Mode.Search;
                return;
            }
            Vector3 front = targetHidingPlace.Exit;
            float pace = hidingQuarry != null ? tuning.ChaseSpeed(runner.Act, false, runner.LivingInvestigators) : tuning.searchSpeed;
            if (Flat(front - transform.position).sqrMagnitude > 1.6f * 1.6f) {
                Steer(front, pace, Time.time >= nextRepath);
                return;
            }
            CH_HidingSpot spot = targetHidingPlace;
            targetHidingPlace = null;
            hidingQuarry = null;
            LocalCharacter inside = runner.OccupantOf(spot);
            runner.OnHidingPlaceOpened(spot, inside);
            if (inside != null && !inside.IsDead) {
                Catches++;
                inside.TakeDamage(System.Math.Max(1L, inside.Health.Value), DamageType.Melee, (IEntity)entity);
                State = Mode.Feed;
                feedUntil = Time.time + tuning.feedSeconds;
                Stop();
                return;
            }
            State = Mode.Search;
        }

        void ForgetQuarry() {
            Quarry = null;
            lostAt = -1f;
            lungeUntil = 0f;
        }

        // ---- Patrol and the director --------------------------------------------------------------------------------------

        void Patrol() {
            if (TryRelocate()) return;
            bool arrived = Flat(patrolPoint - transform.position).sqrMagnitude < 4f;
            if (arrived || Time.time > patrolUntil) PickPatrolPoint();
            Steer(patrolPoint, tuning.patrolSpeed, false);
        }

        void PickPatrolPoint() {
            patrolUntil = Time.time + 22f;
            Vector3 candidate;
            LocalCharacter someone = RandomLivingInvestigator();
            if (someone != null && random.NextDouble() < tuning.patrolTowardInvestigators) {
                Vector2 offset = RandomInCircle() * 16f;
                candidate = someone.transform.position + new Vector3(offset.x, 0f, offset.y);
            } else {
                Transform[] spots = runner.Map.searchLocations;
                Transform spot = spots != null && spots.Length > 0 ? spots[random.Next(spots.Length)] : null;
                candidate = spot != null ? spot.position : runner.Map.monsterSpawn.position;
            }
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SamplePositionRadius * 2f, agent.areaMask)) patrolPoint = hit.position;
            else patrolPoint = runner.Map.monsterSpawn.position;
        }

        /// <summary>Nobody near for a long while: he rises out of the water closer to someone, out of their sight.</summary>
        bool TryRelocate() {
            if (Time.time < nextRelocate || Time.time - lastSensedAnyone < tuning.relocateAfterSeconds) return false;
            LocalCharacter someone = RandomLivingInvestigator();
            if (someone == null || NearestInvestigatorDistance(transform.position) < tuning.relocateWhenFurtherThan) return false;
            nextRelocate = Time.time + tuning.relocateCooldown;
            for (int attempt = 0; attempt < 16; attempt++) {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float radius = Mathf.Lerp(tuning.relocateDistance.x, tuning.relocateDistance.y, (float)random.NextDouble());
                Vector3 candidate = someone.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SamplePositionRadius, agent.areaMask)) continue;
                if (NearestInvestigatorDistance(hit.position) < tuning.relocateDistance.x * 0.8f || AnyoneCanSee(hit.position)) continue;
                probePath ??= new NavMeshPath();
                if (!NavMesh.CalculatePath(hit.position, someone.transform.position, agent.areaMask, probePath) || probePath.status != NavMeshPathStatus.PathComplete) continue;
                agent.Warp(hit.position);
                progressPosition = hit.position;
                patrolPoint = someone.transform.position;
                patrolUntil = Time.time + 22f;
                lastSensedAnyone = Time.time;
                runner.OnRelocated(hit.position);
                return true;
            }
            return false;
        }

        bool AnyoneCanSee(Vector3 point) {
            Vector3 head = point + Vector3.up * 1.8f;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (character == null || character.IsDead) continue;
                Vector3 eye = EyeOf(character);
                if (Vector3.Distance(eye, head) < 70f && WorldInteraction.CanReach(eye, head, 70f, obstacleMask)) return true;
            }
            return false;
        }

        LocalCharacter RandomLivingInvestigator() {
            LocalCharacter pick = null;
            int seen = 0;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (!IsEligible(character)) continue;
                if (random.Next(++seen) == 0) pick = character;
            }
            return pick;
        }

        float NearestInvestigatorDistance(Vector3 point) {
            float nearest = float.MaxValue;
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) nearest = Mathf.Min(nearest, Vector3.Distance(point, character.transform.position));
            return nearest;
        }

        // ---- Movement ------------------------------------------------------------------------------------------------------

        void Steer(Vector3 destination, float speed, bool repath) {
            locomotion.SetTargetingType(NPCTargetingType.External);
            agent.speed = speed * (entity != null ? entity.Build.Speed : 1f);
            agent.isStopped = false;
            if (!repath && agent.hasPath && !agent.isPathStale) return;
            nextRepath = Time.time + tuning.repathInterval;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, SamplePositionRadius, agent.areaMask))
                agent.SetDestination(hit.position);
        }

        /// <summary>Hunting and getting nowhere (a doorframe, a crate): he forces his way along his path.</summary>
        void CheckProgress() {
            if (Time.time < progressCheckAt) return;
            bool moving = State is Mode.Chase or Mode.Search or Mode.OpenHidingPlace;
            if (moving && agent.hasPath && agent.remainingDistance > 1.5f
                && Flat(transform.position - progressPosition).sqrMagnitude < 0.3f * 0.3f) {
                Vector3[] corners = agent.path.corners;
                Vector3 next = corners.Length > 1 ? corners[1] : agent.destination;
                Vector3 nudge = transform.position + Flat(next - transform.position).normalized * tuning.stuckNudge;
                if (NavMesh.SamplePosition(nudge, out NavMeshHit hit, 1.5f, agent.areaMask)) agent.Warp(hit.position);
                agent.SetDestination(agent.destination);
            }
            progressPosition = transform.position;
            progressCheckAt = Time.time + tuning.stuckSeconds;
        }

        void Taunt() {
            if (!IsHunting || Time.time < nextTaunt) return;
            nextTaunt = Time.time + tuning.tauntInterval * (0.8f + 0.4f * (float)random.NextDouble());
            string line = Quarry != null && runner.IsCarryingItem(Quarry) && tauntIndex % 3 == 0
                ? "carry_1" : CH_Story.Taunts[tauntIndex % CH_Story.Taunts.Length];
            tauntIndex++;
            runner.SpeakFromMonster(line);
        }

        Vector2 RandomInCircle() {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
            float radius = Mathf.Sqrt((float)random.NextDouble());
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);
    }
}
