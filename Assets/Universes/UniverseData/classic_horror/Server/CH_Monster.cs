using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>Haunting-specific perception layered over LocalNPC navigation.</summary>
    public sealed class CH_Monster : MonoBehaviour {
        const float FootstepPaceMultiplier = 3f;
        const float NavigationRadius = 0.1f;
        const float NavigationHeight = 1.05f;
        const float UnreachableGiveUpSeconds = 10f;
        const float UnreachableRetrySeconds = 12f;
        const float ReachableApproachRadius = CH_MonsterTuning.OffMeshApproachRadius;
        // How close to its approach point counts as having arrived there (stand-off), and to the
        // last sensed position when searching.
        const float StandOffArrivalRadius = 1f, SearchArrivalRadius = 1.5f;
        static readonly Vector3 HitboxSize = new(0.2f, 0.9f, 0.2f);
        static readonly Vector3 HitboxCenter = new(0, 0.45f, 0);
        static readonly Vector3[] ReachabilityProbeDirections = {
            Vector3.right, Vector3.forward, Vector3.left, Vector3.back,
            new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized,
            new Vector3(-1, 0, -1).normalized, new Vector3(1, 0, -1).normalized
        };

        CH_ServerRunner runner;
        CH_MonsterTuning tuning;
        LocalNPC locomotion;
        GameCharacter entity;
        CH_Temperament temperament;
        SeededStoryRandom random;
        readonly System.Collections.Generic.Dictionary<LocalCharacter, Vector3> previousPositions = new();
        readonly System.Collections.Generic.List<LocalCharacter> staleCharacters = new();
        readonly System.Collections.Generic.Dictionary<LocalCharacter, float> ignoredUntil = new();
        readonly System.Collections.Generic.Dictionary<LocalCharacter, Reachability> reachability = new();
        readonly System.Collections.Generic.List<SensedCandidate> sensedCandidates = new();
        UnityEngine.AI.NavMeshPath reachPath;
        LocalCharacter pursued;
        float lastProgress;
        public int AbandonedChases { get; private set; }
        public bool IsChasing => !suspended && pursued != null && Time.time - lastSeen < tuning.PursuitMemory(livingInvestigators);
        /// <summary>True while a scare holds it in place.</summary>
        public bool IsStaggered => Time.time < staggeredUntil;
        public string State { get; private set; } = "Emerging";
        Vector3 patrolPoint, progressPosition;
        float nextThink, lastThink, nextAttack, lastSeen = -100, repelledUntil, lightHeldUntil, enragedUntil, nextWhisper, nextPatrol;
        // Negative while it is not held by a scare; the retreat follows once the hold ends.
        float staggeredUntil = -1f, retreatAfterStagger;
        float unreachableSince = -1f;
        // When it first stood at its approach point without being able to strike, or negative.
        float stalledSince = -1f;
        // Where it last saw or heard its quarry: a search goes there, not to where they are now.
        Vector3 lastSeenPosition;
        int livingInvestigators = 1;
        Vector3 committedPursuitDestination;
        float nextPursuitRepath;
        bool hasCommittedPursuitDestination;
        bool suspended;
        int warningIndex;
        int obstacleMask;

        readonly struct SensedCandidate {
            public readonly LocalCharacter Character;
            public readonly float Distance;
            public readonly bool Visible;
            public SensedCandidate(LocalCharacter character, float distance, bool visible) {
                Character = character;
                Distance = distance;
                Visible = visible;
            }
        }

        readonly struct Reachability {
            public readonly Vector3 Position;
            public readonly Vector3 Destination;
            public readonly float NextCheck;
            public readonly bool Reachable;
            public Reachability(Vector3 position, Vector3 destination, float nextCheck, bool reachable) {
                Position = position;
                Destination = destination;
                NextCheck = nextCheck;
                Reachable = reachable;
            }
        }

        public void Initialize(CH_ServerRunner owner, LocalNPC npc, CH_Temperament behavior, int seed, CH_MonsterTuning hunt) {
            runner = owner; locomotion = npc; temperament = behavior; tuning = hunt;
            if (TryGetComponent(out CharacterAnimator characterAnimator))
                characterAnimator.FootstepPaceMultiplier = FootstepPaceMultiplier;
            reachPath = new UnityEngine.AI.NavMeshPath();
            entity = GetComponent<GameCharacter>();
            random = new SeededStoryRandom(seed ^ 0x714ade);
            locomotion.AutomaticTargeting = false;
            // The Presence deliberately uses a much smaller navigation capsule than
            // its visuals so porch steps, narrow doors, and furnished interiors do
            // not break pursuit.
            locomotion.agent.radius = NavigationRadius;
            locomotion.agent.height = NavigationHeight;
            locomotion.agent.acceleration = 38f;
            locomotion.agent.angularSpeed = 480f;
            locomotion.agent.stoppingDistance = CH_MonsterTuning.AgentStoppingDistance;
            locomotion.agent.autoRepath = true;
            locomotion.agent.autoBraking = false;
            locomotion.agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            if (TryGetComponent(out BoxCollider hitbox)) {
                hitbox.size = HitboxSize;
                hitbox.center = HitboxCenter;
            }
            locomotion.SetTargetingType(NPCTargetingType.External);
            obstacleMask = ~LayerMask.GetMask("Character", "LocalCharacter", "Ignore Raycast", "UI");
            // Investigators already have 15 seconds of spawn protection. Begin
            // hunting as soon as that protection expires instead of roaming for
            // another ten seconds.
            repelledUntil = Time.time + 15f;
            nextWhisper = Time.time + 30f;
            lastThink = Time.time;
            progressPosition = transform.position;
            lastProgress = Time.time;
        }
        public void Enrage(float seconds) { enragedUntil = Time.time + seconds; repelledUntil = 0; }
        public void Repel(float seconds) { repelledUntil = Time.time + seconds; nextPatrol = 0; pursued = null; lastSeen = -100; unreachableSince = -1f; stalledSince = -1f; hasCommittedPursuitDestination = false; }
        public void Suspend() { suspended = true; locomotion.SetTargetingType(NPCTargetingType.None); }

        /// <summary>
        /// Freezes it where it stands for <paramref name="seconds"/>: a scare that fills an
        /// investigator's screen also holds the Presence, so they can still run when the face fades.
        /// A strike is never due during it. With <paramref name="retreatSeconds"/> above zero it then
        /// withdraws for that long (as after a catch); otherwise it resumes the same hunt.
        /// </summary>
        public void Stagger(float seconds, float retreatSeconds = 0f) {
            if (seconds <= 0f) return;
            staggeredUntil = Mathf.Max(staggeredUntil, Time.time + seconds);
            retreatAfterStagger = Mathf.Max(retreatAfterStagger, retreatSeconds);
            nextAttack = Mathf.Max(nextAttack, staggeredUntil);
            var agent = locomotion.agent;
            if (agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            State = "Staggered";
        }

        /// <summary>Lets the agent move again once a stagger is over, withdrawing first if one was asked for.</summary>
        void EndStagger() {
            staggeredUntil = -1f;
            lastProgress = Time.time;
            var agent = locomotion.agent;
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
            if (retreatAfterStagger > 0f) Repel(retreatAfterStagger);
            retreatAfterStagger = 0f;
        }

        static int CountLivingInvestigators() {
            int living = 0;
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) living++;
            return living;
        }

        void Update() {
            if (runner == null || suspended || !runner.CaseActive || Time.time < nextThink) return;
            if (staggeredUntil >= 0f) {
                if (IsStaggered) return;
                EndStagger();
            }
            livingInvestigators = CountLivingInvestigators();
            float elapsed = Mathf.Max(0.01f, Time.time - lastThink);
            lastThink = Time.time;
            nextThink = Time.time + 0.15f;
            LocalCharacter sensed = null;
            float closest = float.MaxValue;
            bool sensedVisible = false;
            bool heldByLight = false;
            sensedCandidates.Clear();
            Vector3 eye = entity.CharacterCamera != null ? entity.CharacterCamera.position : transform.position + Vector3.up * 1.75f;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (!IsEligibleTarget(character)) continue;
                Vector3 position = character.transform.position;
                float movement = previousPositions.TryGetValue(character, out Vector3 previous) ? Vector3.Distance(previous, position) / elapsed : 0;
                previousPositions[character] = position;
                if (character != pursued && ignoredUntil.TryGetValue(character, out float ignoreTime) && Time.time < ignoreTime) continue;
                Vector3 targetEye = character.CharacterCamera != null ? character.CharacterCamera.position : position + Vector3.up * 2;
                float distance = Vector3.Distance(eye, targetEye);
                var light = ServerTool.Instance.GetTool(character.NetworkObject, ToolEnum.Flashlight) as ToolFlashlightShared;
                bool lit = light != null && light.IsIlluminating;
                bool sight = WorldInteraction.CanReach(eye, targetEye, tuning.MaxSightRange, obstacleMask);
                bool beamOnMonster = lit && sight && distance < tuning.beamRepelRange
                    && Vector3.Dot(light.BeamDirection, (eye - targetEye).normalized) > tuning.beamRepelAlignment;
                if (temperament == CH_Temperament.LightShy && beamOnMonster) heldByLight = true;
                // Temperament sets how far it notices someone; a chase holds them further, except alone.
                float sightRange = tuning.SightRange(temperament, lit, movement, Time.time < enragedUntil, character == pursued, livingInvestigators);
                // Footsteps carry through cover.
                bool heard = distance < tuning.HearingRange(temperament, movement);
                if ((sight && distance < sightRange) || heard)
                    sensedCandidates.Add(new SensedCandidate(character, distance, sight));
            }
            staleCharacters.Clear();
            foreach (var pair in previousPositions) if (pair.Key == null || !pair.Key.IsSpawned) staleCharacters.Add(pair.Key);
            foreach (var stale in staleCharacters) {
                previousPositions.Remove(stale);
                ignoredUntil.Remove(stale);
                reachability.Remove(stale);
            }

            if (heldByLight) lightHeldUntil = Time.time + tuning.lightHoldSeconds;
            if (Time.time < lightHeldUntil || Time.time < repelledUntil) {
                // Keep the same pursuit while light-shy retreat is active. A short
                // hold prevents tiny beam/occlusion changes from flipping between
                // retreat and hunt on consecutive perception ticks.
                unreachableSince = -1f;
                State = Time.time < lightHeldUntil ? "Retreating from light" : "Keeping distance";
                Roam(true);
                return;
            }

            // Protection may begin after a target was acquired (for example, when an
            // investigator is revived or enters an invulnerability window). Do not
            // retain that character through pursuit memory or use them for scares.
            if (pursued != null && !IsEligibleTarget(pursued))
                GiveUp(pursued);

            if (PursuitHasBeenUnreachable()) {
                LocalCharacter unreachableTarget = pursued;
                ignoredUntil[unreachableTarget] = Time.time + UnreachableRetrySeconds;
                GiveUp(unreachableTarget);
            }

            sensedCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            foreach (SensedCandidate candidate in sensedCandidates) {
                // Commit to an eligible investigator through cover and nearby
                // teammates. Reconsider others only after this pursuit ends.
                if (pursued != null && candidate.Character != pursued) continue;
                if (ignoredUntil.TryGetValue(candidate.Character, out float ignoreTime) && Time.time < ignoreTime)
                    continue;
                // A pursuit that was reachable when acquired remains valid while its
                // investigator briefly jumps or crosses a narrow mesh edge. MoveTo will
                // keep following the last complete route until projection succeeds again.
                if (candidate.Character != pursued && !CanReachTarget(candidate.Character)) {
                    ignoredUntil[candidate.Character] = Time.time + 1f;
                    continue;
                }
                sensed = candidate.Character;
                closest = candidate.Distance;
                sensedVisible = candidate.Visible;
                break;
            }
            if (sensed != null) {
                State = "Hunting";
                if (pursued != sensed) {
                    pursued = sensed;
                    unreachableSince = -1f;
                    lastProgress = Time.time;
                    progressPosition = transform.position;
                    runner.OnMonsterChaseStarted();
                }
                lastSeen = Time.time;
                lastSeenPosition = sensed.transform.position;
                NavigatePursued(tuning.HuntSpeed(runner.CurrentCase.Phase != CH_Phase.Investigation, Time.time < enragedUntil, livingInvestigators));
                Vector3 sensedEye = sensed.CharacterCamera != null ? sensed.CharacterCamera.position : sensed.transform.position + Vector3.up * 2f;
                // An investigator standing just off the walkable area keeps it at its approach point, a
                // little beyond normal strike range. Once it has stood there for a moment it lunges;
                // if it cannot see them from there either, it leaves instead of waiting beside them.
                UpdateStandOff(closest);
                float stalledFor = stalledSince < 0f ? 0f : Time.time - stalledSince;
                float strikeRange = tuning.StrikeRange(stalledFor);
                bool strikeClear = WorldInteraction.CanReach(eye, sensedEye, Mathf.Max(tuning.strikeReach, strikeRange), obstacleMask);
                if (stalledFor >= tuning.standOffGiveUpSeconds && !strikeClear) {
                    ignoredUntil[sensed] = Time.time + UnreachableRetrySeconds;
                    GiveUp(sensed);
                    return;
                }
                if (closest < strikeRange && Time.time >= nextAttack && strikeClear) {
                    stalledSince = -1f;
                    nextAttack = Time.time + tuning.attackInterval;
                    // Use the health contract so a successful hit is lethal at any
                    // player health, while still respecting spawn protection.
                    sensed.TakeDamage(System.Math.Max(1L, sensed.Health.Value), DamageType.Melee, (IEntity)entity);
                }
                // Face to face in the dark: the runner rations these so they stay rare, and holds the
                // Presence while the face is up (Stagger).
                if (CH_ScareRules.ShouldWarn(sensedVisible, closest, tuning.strikeDistance, runner.ProximityScareDistance))
                    runner.TryEncounterScare(sensed.Owner, CH_ScareKind.Warning);
                if (closest < tuning.whisperDistance && Time.time > nextWhisper
                    && runner.TrySpeakWarning(runner.CurrentCase.Warnings[warningIndex % runner.CurrentCase.Warnings.Length])) {
                    warningIndex++;
                    nextWhisper = Time.time + tuning.whisperInterval;
                }
            } else if (Time.time - lastSeen < tuning.PursuitMemory(livingInvestigators) && IsEligibleTarget(pursued)) {
                State = "Searching";
                stalledSince = -1f;
                // It goes to where it last saw or heard them (through a doorway into a house if need be),
                // not to where they are now, so breaking its line of sight really shakes it off.
                Search(Mathf.Min(tuning.searchSpeed, tuning.HuntSpeed(true, false, livingInvestigators)));
            } else {
                if (pursued != null) GiveUp(pursued);
                State = "Roaming"; Roam(false);
            }
        }

        // Use the same damage context for perception as the eventual attack. This
        // keeps spawn/global/team protection from becoming a chase target at all.
        bool IsEligibleTarget(LocalCharacter character) =>
            character != null && !character.IsDead && !character.IsProtected(entity, DamageType.Melee);

        bool CanReachTarget(LocalCharacter target) {
            var agent = locomotion.agent;
            if (!agent.enabled || !agent.isOnNavMesh) return false;
            Vector3 targetPosition = target.transform.position;
            if (reachability.TryGetValue(target, out Reachability cached)
                && Time.time < cached.NextCheck
                && (targetPosition - cached.Position).sqrMagnitude < 2.25f)
                return cached.Reachable;

            reachPath ??= new UnityEngine.AI.NavMeshPath();
            var filter = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            bool result = TryCalculateReachablePath(targetPosition, filter, out Vector3 pathDestination);
            reachability[target] = new Reachability(targetPosition, pathDestination,
                Time.time + (result ? 1.25f : 0.5f), result);
            return result;
        }
        bool TryCalculateReachablePath(Vector3 targetPosition, UnityEngine.AI.NavMeshQueryFilter filter, out Vector3 pathDestination) {
            // SamplePosition can select a tiny disconnected polygon around shoreline
            // props even when the main walkable region is only a step away. Prefer the
            // exact projection, then probe a small deterministic ring for the nearest
            // complete approach path before declaring the investigator unreachable.
            if (TryCalculatePathToSample(targetPosition, 2.5f, targetPosition, filter, out pathDestination))
                return true;

            for (float radius = 0.75f; radius <= ReachableApproachRadius; radius += 0.75f) {
                foreach (Vector3 direction in ReachabilityProbeDirections) {
                    Vector3 candidate = targetPosition + direction * radius;
                    if (TryCalculatePathToSample(candidate, 0.5f, targetPosition, filter, out pathDestination))
                        return true;
                }
            }
            pathDestination = targetPosition;
            return false;
        }
        bool TryCalculatePathToSample(Vector3 sampleCenter, float sampleRadius, Vector3 targetPosition,
            UnityEngine.AI.NavMeshQueryFilter filter, out Vector3 pathDestination) {
            pathDestination = targetPosition;
            if (!UnityEngine.AI.NavMesh.SamplePosition(sampleCenter, out var hit, sampleRadius, filter)
                || Mathf.Abs(hit.position.y - targetPosition.y) >= 1.5f
                || !locomotion.agent.CalculatePath(hit.position, reachPath)
                || reachPath.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                return false;
            pathDestination = hit.position;
            return true;
        }
        bool PursuitHasBeenUnreachable() {
            if (pursued == null || pursued.IsDead) {
                unreachableSince = -1f;
                return false;
            }
            if (CanReachTarget(pursued)) {
                unreachableSince = -1f;
                return false;
            }
            if (unreachableSince < 0f) {
                unreachableSince = Time.time;
                return false;
            }
            return Time.time - unreachableSince >= UnreachableGiveUpSeconds;
        }
        /// <summary>Starts or clears the stand-off clock: it is at its approach point but outside strike range.</summary>
        void UpdateStandOff(float distance) {
            var agent = locomotion.agent;
            bool atApproach = agent.enabled && agent.isOnNavMesh && hasCommittedPursuitDestination
                && (transform.position - committedPursuitDestination).sqrMagnitude <= StandOffArrivalRadius * StandOffArrivalRadius;
            if (!atApproach || distance < tuning.strikeDistance) stalledSince = -1f;
            else if (stalledSince < 0f) stalledSince = Time.time;
        }

        /// <summary>Walks to where it last sensed its quarry and waits there until its memory runs out.</summary>
        void Search(float speed) {
            if ((transform.position - lastSeenPosition).sqrMagnitude <= SearchArrivalRadius * SearchArrivalRadius) {
                var agent = locomotion.agent;
                if (agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
                return;
            }
            locomotion.agent.autoBraking = true;
            hasCommittedPursuitDestination = false;
            Navigate(lastSeenPosition, speed);
        }

        void GiveUp(LocalCharacter target) {
            if (target != null) AbandonedChases++;
            if (pursued == target) pursued = null;
            lastSeen = -100; nextPatrol = 0; lastProgress = Time.time; unreachableSince = -1f; stalledSince = -1f;
            hasCommittedPursuitDestination = false;
            State = "Giving up pursuit";
        }
        float NearestInvestigator(Vector3 position) {
            float result = 1000f;
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) result = Mathf.Min(result, Vector3.Distance(position, character.transform.position));
            return result;
        }
        void Roam(bool retreat) {
            // Patrol points are pass-through destinations. Chases use braking so
            // the agent settles at its target instead of overshooting and turning
            // back every frame.
            locomotion.agent.autoBraking = false;
            hasCommittedPursuitDestination = false;
            if (Time.time > nextPatrol || (transform.position - patrolPoint).sqrMagnitude < 4f || Time.time - lastProgress > 5f) {
                nextPatrol = 0;
                for (int i = 0; i < runner.Map.searchLocations.Length * 2; i++) {
                    Vector3 candidate = runner.Map.searchLocations[random.Next(runner.Map.searchLocations.Length)].position;
                    if ((candidate - transform.position).sqrMagnitude < 36f) continue;
                    if (retreat && i < runner.Map.searchLocations.Length && NearestInvestigator(candidate) < NearestInvestigator(transform.position) + 4f) continue;
                    if (!locomotion.MoveTo(candidate, tuning.patrolSpeed)) continue;
                    patrolPoint = candidate; nextPatrol = Time.time + 25f;
                    lastProgress = Time.time; progressPosition = transform.position;
                    break;
                }
            }
            if (nextPatrol > 0 && !Navigate(patrolPoint, tuning.patrolSpeed)) nextPatrol = 0;
        }

        bool NavigatePursued(float speed) {
            var agent = locomotion.agent;
            if (!agent.enabled || !agent.isOnNavMesh || pursued == null) return false;
            agent.autoBraking = true;
            locomotion.SetTargetingType(NPCTargetingType.External);
            agent.speed = speed;

            // Use the reachable projection selected for this investigator. MoveTo
            // samples a second time with a wider radius, which can select an
            // isolated polygon beside a house and repeatedly reject the chase.
            Reachability route = default;
            bool reachable = CanReachTarget(pursued)
                && reachability.TryGetValue(pursued, out route) && route.Reachable;
            bool usablePath = agent.hasPath && !agent.isPathStale
                && agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            if (!reachable) {
                if (usablePath) agent.isStopped = false;
                return usablePath;
            }

            Vector3 destination = route.Destination;
            if (usablePath && hasCommittedPursuitDestination
                && Time.time < nextPursuitRepath
                && (destination - committedPursuitDestination).sqrMagnitude < 1.56f) {
                agent.isStopped = false;
                return true;
            }

            reachPath ??= new UnityEngine.AI.NavMeshPath();
            if (!agent.CalculatePath(destination, reachPath)
                || reachPath.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                if (usablePath) agent.isStopped = false;
                return usablePath;
            }
            agent.isStopped = false;
            if (!agent.SetPath(reachPath)) return usablePath;
            committedPursuitDestination = destination;
            hasCommittedPursuitDestination = true;
            nextPursuitRepath = Time.time + 0.5f;
            return true;
        }

        // Patrol destinations still use the shared navigation validation.
        bool Navigate(Vector3 destination, float speed) {
            if ((transform.position - progressPosition).sqrMagnitude > 0.09f) {
                lastProgress = Time.time; progressPosition = transform.position;
            }
            bool accepted = locomotion.MoveTo(destination, speed);
            var agent = locomotion.agent;
            bool usablePath = agent.hasPath && !agent.isPathStale
                && agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathInvalid;
            return accepted || usablePath;
        }
    }
}
