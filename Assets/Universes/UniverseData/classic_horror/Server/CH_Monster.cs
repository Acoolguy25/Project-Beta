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
        CH_ServerRunner runner;
        LocalNPC locomotion;
        GameCharacter entity;
        CH_Temperament temperament;
        SeededStoryRandom random;
        readonly System.Collections.Generic.Dictionary<LocalCharacter, Vector3> previousPositions = new();
        readonly System.Collections.Generic.List<LocalCharacter> staleCharacters = new();
        readonly System.Collections.Generic.Dictionary<LocalCharacter, float> ignoredUntil = new();
        UnityEngine.AI.NavMeshPath reachPath;
        LocalCharacter pursued;
        float lastProgress;
        public int AbandonedChases { get; private set; }
        public string State { get; private set; } = "Emerging";
        Vector3 lastKnown, patrolPoint, progressPosition, detour;
        float nextThink, lastThink, nextAttack, lastSeen = -100, repelledUntil, enragedUntil, nextWhisper, nextPatrol;
        float nextProgressCheck, detourUntil;
        bool suspended;
        int warningIndex;
        int obstacleMask;
        public void Initialize(CH_ServerRunner owner, LocalNPC npc, CH_Temperament behavior, int seed) {
            runner = owner; locomotion = npc; temperament = behavior;
            reachPath = new UnityEngine.AI.NavMeshPath();
            entity = GetComponent<GameCharacter>();
            random = new SeededStoryRandom(seed ^ 0x714ade);
            locomotion.AutomaticTargeting = false;
            locomotion.agent.radius = 0.3f;
            locomotion.agent.height = 2.05f;
            locomotion.agent.acceleration = 38f;
            locomotion.agent.angularSpeed = 480f;
            locomotion.agent.stoppingDistance = 0.65f;
            locomotion.agent.autoRepath = true;
            locomotion.agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            locomotion.SetTargetingType(NPCTargetingType.External);
            obstacleMask = ~LayerMask.GetMask("Character", "LocalCharacter", "Ignore Raycast", "UI");
            repelledUntil = Time.time + 25f;
            nextWhisper = Time.time + 30f;
            lastThink = Time.time;
            progressPosition = transform.position;
            nextProgressCheck = Time.time + 2f;
            lastProgress = Time.time;
        }
        public void Enrage(float seconds) { enragedUntil = Time.time + seconds; repelledUntil = 0; }
        public void Repel(float seconds) { repelledUntil = Time.time + seconds; nextPatrol = 0; pursued = null; lastSeen = -100; }
        public void Suspend() { suspended = true; locomotion.SetTargetingType(NPCTargetingType.None); }

        void Update() {
            if (runner == null || suspended || !runner.CaseActive || Time.time < nextThink) return;
            float elapsed = Mathf.Max(0.01f, Time.time - lastThink);
            lastThink = Time.time;
            nextThink = Time.time + 0.15f;
            LocalCharacter sensed = null;
            float closest = float.MaxValue;
            bool heldByLight = false;
            Vector3 eye = entity.CharacterCamera != null ? entity.CharacterCamera.position : transform.position + Vector3.up * 1.75f;
            foreach (var character in LocalCharacter.Characters.Values) {
                if (character == null || character.IsDead) continue;
                Vector3 position = character.transform.position;
                float movement = previousPositions.TryGetValue(character, out Vector3 previous) ? Vector3.Distance(previous, position) / elapsed : 0;
                previousPositions[character] = position;
                if (ignoredUntil.TryGetValue(character, out float ignoreTime) && Time.time < ignoreTime) continue;
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
                // Sound crosses cover only at short range; sight/light never do.
                bool heard = temperament == CH_Temperament.Listener && movement > 6f && distance < 32f;
                if ((sight || heard) && distance < range && distance < closest) {
                    if (!CanReachTarget(character)) { GiveUp(character); continue; }
                    sensed = character; closest = distance;
                }
            }
            staleCharacters.Clear();
            foreach (var pair in previousPositions) if (pair.Key == null || !pair.Key.IsSpawned) staleCharacters.Add(pair.Key);
            foreach (var stale in staleCharacters) { previousPositions.Remove(stale); ignoredUntil.Remove(stale); }
            if (heldByLight || Time.time < repelledUntil) {
                State = heldByLight ? "Retreating from light" : "Keeping distance";
                pursued = null; lastSeen = -100;
                Roam(true);
                return;
            }
            if (sensed != null) {
                State = "Hunting";
                if (pursued != sensed) { pursued = sensed; lastProgress = Time.time; progressPosition = transform.position; }
                lastSeen = Time.time;
                lastKnown = sensed.transform.position;
                float speed = runner.CurrentCase.Phase == CH_Phase.Investigation ? 11f : 13f;
                if (Time.time < enragedUntil) speed = 15f;
                if (!Navigate(lastKnown, speed) || Time.time - lastProgress > 6f && closest > 1.85f) {
                    GiveUp(sensed); Roam(true); return;
                }
                if (closest < 11f && WorldInteraction.CanReach(eye, sensed.CharacterCamera.position, 12f, obstacleMask)) runner.Scare(sensed);
                if (closest < 1.85f && Time.time >= nextAttack && WorldInteraction.CanReach(eye, sensed.CharacterCamera.position, 2.2f, obstacleMask)) {
                    nextAttack = Time.time + 2f;
                    sensed.TakeDamage(45, DamageType.Melee, (IEntity)entity);
                }
                if (closest < 38f && Time.time > nextWhisper) {
                    nextWhisper = Time.time + 35f;
                    runner.Speak(runner.CurrentCase.Warnings[warningIndex++ % runner.CurrentCase.Warnings.Length]);
                }
            } else if (Time.time - lastSeen < 9f && pursued != null) {
                State = "Searching";
                if (!Navigate(lastKnown, 10f) || Time.time - lastProgress > 5f) GiveUp(pursued);
            } else {
                pursued = null; State = "Roaming"; Roam(false);
            }
        }

        bool CanReachTarget(LocalCharacter target) {
            var agent = locomotion.agent;
            if (!agent.enabled || !agent.isOnNavMesh) return false;
            reachPath ??= new UnityEngine.AI.NavMeshPath();
            var filter = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            // A ground path underneath a player on an inaccessible roof is not a
            // route to that player. Do not repeatedly chase the projected floor.
            return UnityEngine.AI.NavMesh.SamplePosition(target.transform.position, out var hit, 1f, filter)
                && Mathf.Abs(hit.position.y - target.transform.position.y) < 0.85f
                && agent.CalculatePath(hit.position, reachPath)
                && reachPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
        }
        void GiveUp(LocalCharacter target) {
            if (target != null) { ignoredUntil[target] = Time.time + 18f; AbandonedChases++; }
            if (pursued == target) pursued = null;
            lastSeen = -100; nextPatrol = 0; detourUntil = 0; lastProgress = Time.time;
            State = "Giving up pursuit";
        }
        float NearestInvestigator(Vector3 position) {
            float result = 1000f;
            foreach (var character in LocalCharacter.Characters.Values)
                if (character != null && !character.IsDead) result = Mathf.Min(result, Vector3.Distance(position, character.transform.position));
            return result;
        }
        void Roam(bool retreat) {
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

        // Preserve a valid route while tracking. If physics stalls an agent at a
        // corner, take a short reachable side step and then retry the target.
        // Every recovery uses the baked mesh; the monster never teleports through walls.
        bool Navigate(Vector3 destination, float speed) {
            if ((transform.position - progressPosition).sqrMagnitude > 0.09f) {
                lastProgress = Time.time; progressPosition = transform.position;
            }
            if (Time.time < detourUntil) return locomotion.MoveTo(detour, speed);
            bool accepted = locomotion.MoveTo(destination, speed);
            if (Time.time < nextProgressCheck) return accepted;
            bool stalled = Time.time - lastProgress > 1.5f
                && (destination - transform.position).sqrMagnitude > 9f;
            nextProgressCheck = Time.time + 1.5f;
            if (!stalled && accepted) return true;
            Vector3 forward = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).normalized;
            for (int i = 0; i < 8; i++) {
                float angle = (i % 2 == 0 ? 1 : -1) * (45f + i / 2 * 35f);
                Vector3 candidate = transform.position + Quaternion.Euler(0, angle, 0) * forward * 2.5f;
                if (!locomotion.MoveTo(candidate, speed)) continue;
                detour = candidate;
                detourUntil = Time.time + 0.55f;
                return true;
            }
            return accepted;
        }
    }
}
