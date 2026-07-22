using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Shared;

namespace RyanAssets.Characters.Server
{
    public enum NPCTargetingType
    {
        None,
        Random,
        Character,
        Flee
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class LocalNPC : IRagdoll
    {
        public override bool disableOnRagdoll => true;
        [Header("Movement")]
        [SerializeField] private float WalkSpeed = 18.0f;
        [SerializeField] private float FleeSpeed = 28.0f;
        public static float WalkSpeedMultiplier = 1.0f;
        public static float FleeSpeedMultiplier = 1.0f;

        [Header("Flee")]
        [SerializeField] public Transform[] FleeTargets;
        [SerializeField] private float FleeEnterRadius = 8f;   // threat this close → start fleeing
        [SerializeField] private float FleeDistance = 18f;
        [SerializeField] private float FleeRecalcInterval = 1f;
        [SerializeField] private int FleeCandidateCount = 16;
        [SerializeField] private float FleeRandomRadius = 2f;
        [SerializeField] private float ThreatClearRadius = 15f;

        public NPCTargetingType TargetingType = NPCTargetingType.Random;
        private NPCTargetingType _previousTargetingType = NPCTargetingType.Random;

        public GameObject PreviousTarget;
        private Vector3? PreviousTargetVec;

        public NavMeshAgent agent;
        private Coroutine _fleeCoroutine;
        void Awake() {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
            agent.updateUpAxis = false;
        }
        void OnEnable()
        {
            agent.enabled = true;
            UpdateSpeed();
        }
        void OnDisable() {
            SetTargetingType(NPCTargetingType.None);
            agent.enabled = false;
        }
        public void UpdateSpeed(){
            if (TargetingType == NPCTargetingType.Flee){
                agent.speed = FleeSpeed * FleeSpeedMultiplier;
            } else {
                agent.speed = WalkSpeed * WalkSpeedMultiplier;
            }
        }

        /// <summary>
        /// The correct way to change state. Saves pre-flee state so it can be
        /// restored automatically when the threat clears.
        /// </summary>
        public void SetTargetingType(NPCTargetingType newType)
        {
            if (newType == TargetingType) return;

            // Only snapshot non-Flee states as the "previous" to return to.
            // This prevents Flee → Flee from clobbering the real prior state.
            if (TargetingType != NPCTargetingType.Flee)
                _previousTargetingType = TargetingType;

            TargetingType = newType;
            UpdateSpeed();

            if (newType == NPCTargetingType.Flee)
            {
                if (_fleeCoroutine != null) StopCoroutine(_fleeCoroutine);
                _fleeCoroutine = StartCoroutine(FleeRoutine());
            }
            else
            {
                if (_fleeCoroutine != null)
                {
                    StopCoroutine(_fleeCoroutine);
                    _fleeCoroutine = null;
                }
            }
        }

        // Reverts to whatever state was active before Flee. Safe to call even
        // if we're not in Flee state.
        private void RevertFromFlee()
        {
            SetTargetingType(_previousTargetingType);
        }

        void Update()
        {
            HandleRotation();

            if (TargetingType != NPCTargetingType.Flee && FleeTargets != null)
            {
                foreach (Transform threat in FleeTargets)
                {
                    if (threat == null) continue;
                    if (Vector3.Distance(transform.position, threat.position) < FleeEnterRadius)
                    {
                        SetTargetingType(NPCTargetingType.Flee);
                        break;
                    }
                }
            }


            switch (TargetingType)
            {
                case NPCTargetingType.Random:
                    HandleRandom();
                    break;
                case NPCTargetingType.Character:
                    ServerPathfinding.UpdateTarget(agent, ref PreviousTarget, "Player", 0f);
                    break;
                case NPCTargetingType.Flee:
                    // Driven by FleeRoutine coroutine; only check threat-clear here.
                    if (FleeTargets == null || FleeTargets.Length == 0 || !AnyThreatInRange())
                        RevertFromFlee();
                    break;
                case NPCTargetingType.None:
                    agent.ResetPath();
                    break;
            }
        }

        // ─── Rotation ────────────────────────────────────────────────────────

        private void HandleRotation()
        {
            if (!agent.pathPending && agent.velocity.sqrMagnitude > 0.001f)
            {
                Vector3 dir = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(dir.magnitude == 0 ? Vector3.forward : dir),
                    agent.angularSpeed * Time.deltaTime
                );
            }
        }

        // ─── Random wandering ─────────────────────────────────────────────────

        private void HandleRandom()
        {
            if (!agent.pathPending &&
                agent.remainingDistance <= agent.stoppingDistance)
            {
                PreviousTarget = null;
                PreviousTargetVec = null;
            }

            if (PreviousTargetVec == null)
            {
                PreviousTargetVec = ServerPathfinding.GetRandomPosition();
                agent.SetDestination(PreviousTargetVec.Value);
            }
        }

        // ─── Flee coroutine ───────────────────────────────────────────────────

        private IEnumerator FleeRoutine()
        {
            //Debug.Log($"[{name}] FleeRoutine started");

            // Always pick a destination immediately on enter
            PickFleeDestination();

            while (true)
            {
                yield return new WaitForSeconds(FleeRecalcInterval);

                // Skip recalc if we're still making good progress toward the destination
                if (agent.hasPath
                    && agent.pathStatus == NavMeshPathStatus.PathComplete
                    && agent.remainingDistance > agent.stoppingDistance + 0.5f)
                    continue;

                PickFleeDestination();
            }
        }

        private void PickFleeDestination()
        {
            if (FleeTargets == null || FleeTargets.Length == 0) return;
            Vector3 fleeDir = ComputeFleeVector();
            Vector3 best = FindBestFleeDestination(fleeDir);
            if (best != Vector3.zero)
            {
                _lastFleeDestination = best;
                agent.SetDestination(best);
            }
        }

        // Weighted sum of repulsion vectors from all nearby threats.
        private Vector3 ComputeFleeVector()
        {
            Vector3 flee = Vector3.zero;
            foreach (Transform threat in FleeTargets)
            {
                if (threat == null) continue;
                Vector3 delta = transform.position - threat.position;
                // Inverse-square weighting so close threats dominate.
                flee += delta.normalized / Mathf.Max(delta.sqrMagnitude, 0.1f);
            }
            return flee.sqrMagnitude > 0f ? flee.normalized : transform.forward;
        }

        // Samples candidate points, rejects unreachable ones, scores the rest.
        private Vector3 FindBestFleeDestination(Vector3 fleeDir)
        {
            Vector3 bestPos = Vector3.zero;
            float bestScore = float.NegativeInfinity;
            NavMeshPath path = new NavMeshPath();

            float angleStep = 180f / (FleeCandidateCount - 1);

            for (int i = 0; i < FleeCandidateCount; i++)
            {
                float angle = -90f + angleStep * i;
                Vector3 rotated = Quaternion.Euler(0, angle, 0) * fleeDir;
                Vector3 candidate = transform.position + rotated * FleeDistance;

                Vector3 jitter = UnityEngine.Random.insideUnitSphere * FleeRandomRadius;
                jitter.y = 0f;
                candidate += jitter;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    continue;

                // Reject destinations too close — these are almost always wall hugs or bad edges
                if (Vector3.Distance(transform.position, hit.position) < 2f)
                    continue;

                if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path))
                    continue;

                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float score = ScoreCandidate(hit.position, path);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = hit.position;
                }
            }

            return bestPos;
        }

        private Vector3 _lastFleeDestination = Vector3.zero;

        private float ScoreCandidate(Vector3 candidate, NavMeshPath path)
        {
            float minThreatDist = float.MaxValue;
            foreach (Transform threat in FleeTargets)
            {
                if (threat == null) continue;
                float d = Vector3.Distance(candidate, threat.position);
                if (d < minThreatDist) minThreatDist = d;
            }

            float pathLen = PathLength(path);
            float losPenalty = 0f;
            foreach (Transform threat in FleeTargets)
            {
                if (threat == null) continue;
                Vector3 toCandidate = candidate - threat.position;
                if (!Physics.Raycast(threat.position, toCandidate.normalized, toCandidate.magnitude))
                    losPenalty += 4f;
            }

            // Penalize returning to where we just were — this kills oscillation.
            // The closer a candidate is to our last destination, the cheaper it scores.
            float reversalPenalty = 0f;
            if (_lastFleeDestination != Vector3.zero)
                reversalPenalty = Mathf.Clamp(
                    8f - Vector3.Distance(candidate, _lastFleeDestination), 0f, 8f);

            return minThreatDist * 2f
                 - pathLen * 0.5f
                 - losPenalty
                 - reversalPenalty;
        }

        private float PathLength(NavMeshPath path)
        {
            float len = 0f;
            Vector3[] corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
                len += Vector3.Distance(corners[i - 1], corners[i]);
            return len;
        }

        // Returns true if any threat is within ThreatClearRadius.
        private bool AnyThreatInRange()
        {
            foreach (Transform threat in FleeTargets)
            {
                if (threat == null) continue;
                if (Vector3.Distance(transform.position, threat.position) < ThreatClearRadius)
                    return true;
            }
            return false;
        }
    }
}