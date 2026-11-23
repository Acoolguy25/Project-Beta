using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// An NPC townsperson the round sent for a dead sheriff's gun nobody took: it heads for the gun and, on
    /// reaching it, the round makes it the hero (<see cref="MM_ClassicRound.OnNpcReachedGun"/>). It gives up
    /// when it dies, when someone else takes the gun, or when it cannot get there in time; then the round may
    /// send the next nearest NPC. Added at runtime by <see cref="MM_ClassicRound"/> and removed when done.
    /// </summary>
    [RequireComponent(typeof(LocalNPC), typeof(GameCharacter))]
    public sealed class MM_NpcGunSeeker : MonoBehaviour {
        // LocalNPC re-plans a path only when the destination moves, so re-asking this often is cheap.
        const float RepathSeconds = 0.25f;
        // An NPC that gets no closer than this in StallSeconds is stuck on the furniture: let the next one try.
        const float StallSeconds = 3f;
        const float StallProgress = 1f;

        MM_ClassicRound round;
        MM_ClassicTuning tuning;
        LocalNPC npc;
        NavMeshAgent agent;
        GameCharacter self;
        float giveUpAt, nextPath, stallCheckAt, stallDistance;
        bool stopped;

        void Awake() {
            npc = GetComponent<LocalNPC>();
            agent = GetComponent<NavMeshAgent>();
            self = GetComponent<GameCharacter>();
        }

        public void Begin(MM_ClassicRound classicRound, MM_ClassicTuning classicTuning) {
            round = classicRound;
            tuning = classicTuning;
            giveUpAt = Time.time + tuning.npcHeroGiveUpSeconds;
            nextPath = 0f;
            stallCheckAt = Time.time + StallSeconds;
            stallDistance = float.MaxValue;
        }

        void Update() {
            if (round == null || stopped)
                return;
            if (self.IsDead || !round.GunOnFloor || round.Phase != MM_Phase.Hunt || Time.time >= giveUpAt) {
                Stop();
                return;
            }
            Vector3 gun = round.GunPosition;
            Vector3 offset = gun - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= tuning.npcHeroReachDistance * tuning.npcHeroReachDistance) {
                stopped = true;
                ReleaseNpc();
                round.OnNpcReachedGun(this, self);
                Destroy(this);
                return;
            }
            if (Time.time >= stallCheckAt) {
                // Measured along the path, so walking round a wall is still progress.
                float distance = agent != null && agent.hasPath && !agent.pathPending ? agent.remainingDistance : offset.magnitude;
                if (distance > stallDistance - StallProgress) {
                    Stop();
                    return;
                }
                stallDistance = distance;
                stallCheckAt = Time.time + StallSeconds;
            }
            if (Time.time >= nextPath) {
                nextPath = Time.time + RepathSeconds;
                if (!npc.MoveTo(gun, tuning.npcHeroSpeed))
                    Stop();
            }
        }

        /// <summary>Gives up on the gun: the NPC goes back to wandering and the round is told.</summary>
        public void Stop() {
            if (stopped)
                return;
            stopped = true;
            ReleaseNpc();
            round?.OnGunSeekerStopped(this);
            Destroy(this);
        }

        void ReleaseNpc() {
            if (npc != null && !self.IsDead && npc.TargetingType == NPCTargetingType.External)
                npc.SetTargetingType(NPCTargetingType.Random);
        }
    }
}
