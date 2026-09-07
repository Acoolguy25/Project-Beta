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
        const float PursuitMemorySeconds = 30f;
        const float UnreachableGiveUpSeconds = 3f;
        const float UnreachableRetrySeconds = 4f;
        const float ReachableApproachRadius = 3f;
        static readonly Vector3 HitboxSize = new(0.2f, 0.9f, 0.2f);
        static readonly Vector3 HitboxCenter = new(0, 0.45f, 0);
        static readonly Vector3[] ReachabilityProbeDirections = {
            Vector3.right, Vector3.forward, Vector3.left, Vector3.back,
            new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized,
            new Vector3(-1, 0, -1).normalized, new Vector3(1, 0, -1).normalized
        };

        CH_ServerRunner runner;
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
        public bool IsChasing => !suspended && pursued != null && Time.time - lastSeen < PursuitMemorySeconds;
        public string State { get; private set; } = "Emerging";
        Vector3 lastKnown, patrolPoint, progressPosition;
        float nextThink, lastThink, nextAttack, lastSeen = -100, repelledUntil, enragedUntil, nextWhisper, nextPatrol;
        float unreachableSince = -1f;
        bool suspended;
        int warningIndex;
        int obstacleMask;

        readonly struct SensedCandidate {
            public readonly LocalCharacter Character;
            public readonly float Distance;
            public readonly float Score;
            public SensedCandidate(LocalCharacter character, float distance, float score) {
                Character = character;
                Distance = distance;
                Score = score;
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

        public void Initialize(CH_ServerRunner owner, LocalNPC npc, CH_Temperament behavior, int seed) {
            runner = owner; locomotion = npc; temperament = behavior;
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
            locomotion.agent.stoppingDistance = 0.65f;
            locomotion.agent.autoRepath = true;
            locomotion.agent.autoBraking = false;
            locomotion.agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            if (TryGetComponent(out BoxCollider hitbox)) {
                hitbox.size = HitboxSize;
                hitbox.center = HitboxCenter;
            }
            locomotion.SetTargetingType(NPCTargetingType.External);
            obstacleMask = ~LayerMask.GetMask("Character", "LocalCharacter", "Ignore Raycast", "UI");
            repelledUntil = Time.time + 25f;
            nextWhisper = Time.time + 30f;
            lastThink = Time.time;
            progressPosition = transform.position;
            lastProgress = Time.time;
        }
        public void Enrage(float seconds) { enragedUntil = Time.time + seconds; repelledUntil = 0; }
        public void Repel(float seconds) { repelledUntil = Time.time + seconds; nextPatrol = 0; pursued = null; lastSeen = -100; unreachableSince = -1f; }
        public void Suspend() { suspended = true; locomotion.SetTargetingType(NPCTargetingType.None); }

        void Update() {
            if (runner == null || suspended || !runner.CaseActive || Time.time < nextThink) return;
            float elapsed = Mathf.Max(0.01f, Time.time - lastThink);
            lastThink = Time.time;
            nextThink = Time.time + 0.15f;
            LocalCharacter sensed = null;
            float closest = float.MaxValue;
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
                bool sight = WorldInteraction.CanReach(eye, targetEye, 85f, obstacleMask);
                bool beamOnMonster = lit && sight && distance < 32f && Vector3.Dot(light.BeamDirection, (eye - targetEye).normalized) > 0.86f;
                if (temperament == CH_Temperament.LightShy && beamOnMonster) heldByLight = true;
                float range = temperament switch {
                    CH_Temperament.LightSeeker => lit ? 78f : 17f,
                    CH_Temperament.Listener => movement > 6f ? 75f : 14f,
                    _ => 44f
                };
                if (Time.time < enragedUntil) range = 85f;
                // Temperament controls acquisition. Once a chase begins, retain a
                // visible investigator across the map instead of dropping them as
                // soon as they cross the shorter initial detection radius.
                if (character == pursued) range = Mathf.Max(range, 85f);
                // Sound crosses cover only at short range; sight/light never do.
                bool heard = temperament == CH_Temperament.Listener && movement > 6f && distance < 32f;
                if ((sight || heard) && distance < range)
                    // Retain a visible current pursuit unless another investigator is
                    // substantially closer. This prevents rapid target/path flipping in groups.
                    sensedCandidates.Add(new SensedCandidate(character, distance,
                        character == pursued ? distance * 0.75f : distance));
            }
            staleCharacters.Clear();
            foreach (var pair in previousPositions) if (pair.Key == null || !pair.Key.IsSpawned) staleCharacters.Add(pair.Key);
            foreach (var stale in staleCharacters) {
                previousPositions.Remove(stale);
                ignoredUntil.Remove(stale);
                reachability.Remove(stale);
            }

            if (heldByLight || Time.time < repelledUntil) {
                State = heldByLight ? "Retreating from light" : "Keeping distance";
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

            sensedCandidates.Sort((a, b) => a.Score.CompareTo(b.Score));
            foreach (SensedCandidate candidate in sensedCandidates) {
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
                break;
            }
            if (sensed != null) {
                State = "Hunting";
                if (pursued != sensed) {
                    pursued = sensed;
                    unreachableSince = -1f;
                    lastProgress = Time.time;
                    progressPosition = transform.position;
                }
                lastSeen = Time.time;
                lastKnown = sensed.transform.position;
                float speed = runner.CurrentCase.Phase == CH_Phase.Investigation ? 11f : 13f;
                if (Time.time < enragedUntil) speed = 15f;
                Navigate(lastKnown, speed);
                Vector3 sensedEye = sensed.CharacterCamera != null ? sensed.CharacterCamera.position : sensed.transform.position + Vector3.up * 2f;
                if (closest < 1.85f && Time.time >= nextAttack && WorldInteraction.CanReach(eye, sensedEye, 2.2f, obstacleMask)) {
                    nextAttack = Time.time + 2f;
                    // Use the health contract so a successful hit is lethal at any
                    // player health, while still respecting spawn protection.
                    sensed.TakeDamage(System.Math.Max(1L, sensed.Health.Value), DamageType.Melee, (IEntity)entity);
                } else if (closest < 6f && WorldInteraction.CanReach(eye, sensedEye, 6f, obstacleMask)) runner.Scare(sensed);
                if (closest < 38f && Time.time > nextWhisper) {
                    nextWhisper = Time.time + 35f;
                    runner.Speak(runner.CurrentCase.Warnings[warningIndex++ % runner.CurrentCase.Warnings.Length]);
                }
            } else if (Time.time - lastSeen < PursuitMemorySeconds && IsEligibleTarget(pursued)) {
                State = "Searching";
                // Once acquired, keep tracking the investigator through cover. This
                // lets the monster commit to entering a house instead of stopping at
                // the doorway where line of sight was lost.
                lastKnown = pursued.transform.position;
                Navigate(lastKnown, 10f);
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
        void GiveUp(LocalCharacter target) {
            if (target != null) AbandonedChases++;
            if (pursued == target) pursued = null;
            lastSeen = -100; nextPatrol = 0; lastProgress = Time.time; unreachableSince = -1f;
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
            if (Time.time > nextPatrol || (transform.position - patrolPoint).sqrMagnitude < 4f || Time.time - lastProgress > 5f) {
                nextPatrol = 0;
                for (int i = 0; i < runner.Map.searchLocations.Length * 2; i++) {
                    Vector3 candidate = runner.Map.searchLocations[random.Next(runner.Map.searchLocations.Length)].position;
                    if ((candidate - transform.position).sqrMagnitude < 36f) continue;
                    if (retreat && i < runner.Map.searchLocations.Length && NearestInvestigator(candidate) < NearestInvestigator(transform.position) + 4f) continue;
                    if (!locomotion.MoveTo(candidate, 7f)) continue;
                    patrolPoint = candidate; nextPatrol = Time.time + 25f;
                    lastProgress = Time.time; progressPosition = transform.position;
                    break;
                }
            }
            if (nextPatrol > 0 && !Navigate(patrolPoint, 7f)) nextPatrol = 0;
        }

        // Preserve a valid route while tracking and keep retrying transiently invalid
        // moving-player projections. Random side detours made the monster look
        // indecisive and could replace a correct route with a short path into a wall.
        bool Navigate(Vector3 destination, float speed) {
            locomotion.agent.autoBraking = State is "Hunting" or "Searching";
            if ((transform.position - progressPosition).sqrMagnitude > 0.09f) {
                lastProgress = Time.time; progressPosition = transform.position;
            }
            bool accepted = locomotion.MoveTo(destination, speed);
            var agent = locomotion.agent;
            // If the closest projection landed on a disconnected shoreline/prop
            // polygon, use the reachable approach selected during perception.
            if (!accepted && pursued != null
                && reachability.TryGetValue(pursued, out Reachability cached)
                && cached.Reachable
                && (destination - cached.Position).sqrMagnitude < 2.25f) {
                reachPath ??= new UnityEngine.AI.NavMeshPath();
                if (agent.CalculatePath(cached.Destination, reachPath)
                    && reachPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                    agent.isStopped = false;
                    accepted = agent.SetPath(reachPath);
                }
            }
            bool usablePath = agent.hasPath && !agent.isPathStale
                && agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathInvalid;
            // A moving player can be briefly unsampleable while jumping. Preserve the
            // previous route instead of abandoning the chase on that single frame.
            return accepted || usablePath;
        }
    }
}
