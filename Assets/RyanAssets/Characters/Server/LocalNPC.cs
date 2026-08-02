using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;

namespace RyanAssets.Characters.Server {
    public enum NPCTargetingType {
        None,
        Random,
        Character,
        Flee,
        Attack
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class LocalNPC : IRagdoll {
        public override bool disableOnRagdoll => true;
        private GameCharacter gameCharacter;

        [Header("Movement")]
        [SerializeField] private float WalkSpeed = 18.0f;
        [SerializeField] private float FleeSpeed = 28.0f;
        [SerializeField] private float AttackSpeed = 32.0f;
        public static float WalkSpeedMultiplier = 1.0f;
        public static float FleeSpeedMultiplier = 1.0f;
        public static float AttackSpeedMultiplier = 1.0f;

        [Header("Targeting")]
        public NPCTargetingType TargetingType = NPCTargetingType.Random;
        private NPCTargetingType _previousTargetingType = NPCTargetingType.Random;
        public GameObject PreviousTarget;
        private Vector3? PreviousTargetVec;

        [Header("Flee")]
        [SerializeField] public List<TeamColor> FleeTeams = new();
        [SerializeField] private float FleeEnterRadius = 8f;    // threat this close -> start fleeing
        [SerializeField] private float FleeDistance = 18f;
        [SerializeField] private float FleeRecalcInterval = 1f;
        [SerializeField] private int FleeCandidateCount = 16;
        [SerializeField] private float FleeRandomRadius = 2f;
        [SerializeField] private float ThreatClearRadius = 15f; // no threats this close -> stop fleeing

        // FleeTeams has to stay a List<TeamColor> so it's serializable in the Inspector, but every
        // lookup against it wants a HashSet. We keep both in sync instead of building a HashSet (or
        // calling ToList()) on the fly - that per-call conversion was one of the two main sources of
        // per-frame GC allocation that was causing the stutter/freezing.
        private readonly HashSet<TeamColor> _fleeTeamSet = new();

        [Header("Attack")]
        HashSet<TeamColor> EnemyTeams => SharedGlobalEvents.TeamEnemies[gameCharacter.GetTeam().team];
        [SerializeField] private float AttackDetectionRadius = 12f; // enemy this close -> start attacking
        [SerializeField] private float MinAttackRange = 0f;         // closer than this -> back off
        [SerializeField] private float MaxAttackRange = 9f;         // farther than this -> close the distance
        [SerializeField] private float AttackRetargetInterval = 1f;
        [SerializeField] private float AttackClearRadius = 24f;     // no enemies this close -> stop attacking
        [SerializeField] private float AttackCooldown = 1f;         // min seconds between AttackFunction invocations

        [Header("Attack Movement")]
        // Movement destination is only recalculated on this interval rather than every frame - calling
        // SetDestination every single frame while chasing was the other main cause of the stutter,
        // since it forces NavMesh to re-evaluate a path constantly instead of periodically.
        [SerializeField] private float AttackMoveRecalcInterval = 0.35f;
        [SerializeField] private float AttackApproachSideOffset = 0f;   // lateral offset so the approach weaves instead of beelining - 0 disables weaving entirely
        [SerializeField] private float AttackApproachStopShort = 0.3f;    // stop this far inside MaxAttackRange when closing in (only used when ApproachToMinRangeEdge is false)
        [SerializeField] private bool ApproachToMinRangeEdge = false;   // true: close the gap all the way down to MinAttackRange (as close as possible). false: stop just inside MaxAttackRange.
        [SerializeField] private bool AllowMovementWhileAttacking = true; // true: keeps walking straight at the target while inside the [Min, Max] band instead of freezing at the standoff point - use this for melee, where the computed standoff can land just short of actual hit range. Set false to keep the NPC planted in place while attacking (e.g. for ranged units that want to hold a fixed distance).
        [SerializeField] private bool RetreatToMinRangeEdge = true;    // true: always run all the way out to the absolute MinAttackRange edge. false: retreat using AttackRetreatDistance but stop as soon as we're back in range.
        [SerializeField] private float AttackRetreatDistance = 6f;      // only used when RetreatToMinRangeEdge is false
        [SerializeField] private float AttackPredictionLeadTime = 0f;   // seconds to lead the target's estimated velocity by - approach/retreat destinations aim at where the target will be, not where it currently is. 0 disables prediction entirely (chases actual current position).
        [SerializeField] private float DirectChaseRange = 4f;          // within this distance, if a raycast confirms a clear line of sight, chase every frame instead of waiting for AttackMoveRecalcInterval - this is what makes closing distance feel snappy instead of laggy
        [SerializeField] private LayerMask DirectChaseObstacleMask = ~0; // layers that count as blocking line-of-sight for the direct-chase raycast (e.g. walls/terrain, not other characters)

        // Assign this from elsewhere (e.g. another script's Awake/Start) to define what
        // "attacking" actually does for this NPC, e.g.:
        //   npc.AttackFunction = target => target.TakeDamage(10);
        // Not serialized - Unity can't show a delegate in the Inspector, so it must be
        // wired up in code.
        public Action<GameCharacter> AttackFunction;

        public NavMeshAgent agent;

        private Coroutine _fleeCoroutine;
        private Coroutine _attackCoroutine;
        private Coroutine _attackMoveCoroutine;

        private Vector3 _lastFleeDestination = Vector3.zero;

        private GameCharacter _currentAttackTarget;
        public GameCharacter CurrentAttackTarget => _currentAttackTarget;
        private float _lastAttackTime = -Mathf.Infinity;
        private bool _isRetreating;

        // Estimated target velocity, used to predict where it's going instead of chasing/fleeing
        // where it currently is. We have to estimate this ourselves by sampling its position over
        // time - there's no velocity we can read directly off an arbitrary GameCharacter.
        private GameCharacter _velocityTrackedTarget;
        private Vector3 _lastTargetPosition;
        private float _lastTargetSampleTime = -1f;
        private Vector3 _targetVelocity;

        void Awake() {
            gameCharacter = GetComponent<GameCharacter>();
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            RebuildFleeTeamSet();
        }

        void OnValidate() {
            RebuildFleeTeamSet();
            // Keep the range band sane if someone drags Min above Max in the Inspector.
            if (MaxAttackRange < MinAttackRange + 0.1f)
                MaxAttackRange = MinAttackRange + 0.1f;
        }

        void OnEnable() {
            agent.enabled = true;
            UpdateSpeed();
        }

        void OnDisable() {
            SetTargetingType(NPCTargetingType.None);
            agent.enabled = false;
        }

        public void UpdateSpeed() {
            switch (TargetingType) {
                case NPCTargetingType.Flee:
                    agent.speed = FleeSpeed * FleeSpeedMultiplier;
                    break;
                case NPCTargetingType.Attack:
                    agent.speed = AttackSpeed * AttackSpeedMultiplier;
                    break;
                default:
                    agent.speed = WalkSpeed * WalkSpeedMultiplier;
                    break;
            }
        }

        // Call this instead of mutating FleeTeams directly at runtime, so the cached HashSet
        // used for lookups stays in sync with the serialized list.
        public void SetFleeTeams(List<TeamColor> teams) {
            FleeTeams = teams ?? new List<TeamColor>();
            RebuildFleeTeamSet();
        }

        private void RebuildFleeTeamSet() {
            _fleeTeamSet.Clear();
            if (FleeTeams == null) return;
            foreach (TeamColor team in FleeTeams)
                _fleeTeamSet.Add(team);
        }

        /// <summary>
        /// The correct way to change state. Saves pre-Flee/pre-Attack state so it can be
        /// restored automatically once the threat/enemy clears.
        /// </summary>
        public void SetTargetingType(NPCTargetingType newType) {
            if (newType == TargetingType) return;

            // Only snapshot "base" states as the one to return to. This stops
            // Flee/Attack from clobbering the real prior state when they interrupt
            // each other (e.g. an enemy appears while already fleeing something else).
            if (TargetingType != NPCTargetingType.Flee && TargetingType != NPCTargetingType.Attack)
                _previousTargetingType = TargetingType;

            StopStateCoroutines();

            TargetingType = newType;
            UpdateSpeed();

            if (newType == NPCTargetingType.Flee)
                _fleeCoroutine = StartCoroutine(FleeRoutine());
            else if (newType == NPCTargetingType.Attack) {
                _attackCoroutine = StartCoroutine(AttackRoutine());
                _attackMoveCoroutine = StartCoroutine(AttackMovementRoutine());
            }
        }

        private void StopStateCoroutines() {
            if (_fleeCoroutine != null) {
                StopCoroutine(_fleeCoroutine);
                _fleeCoroutine = null;
            }
            if (_attackCoroutine != null) {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
                _currentAttackTarget = null;
                _velocityTrackedTarget = null;
                _targetVelocity = Vector3.zero;
                _lastTargetSampleTime = -1f;
            }
            if (_attackMoveCoroutine != null) {
                StopCoroutine(_attackMoveCoroutine);
                _attackMoveCoroutine = null;
            }
        }

        // Reverts to whatever state was active before Flee/Attack. Safe to call even
        // if we're not currently in either.
        private void RevertToPrevious() {
            SetTargetingType(_previousTargetingType);
        }

        void Update() {
            HandleRotation();

            // Flee takes priority over everything, including attacking.
            if (TargetingType != NPCTargetingType.Flee
                && _fleeTeamSet.Count > 0
                && AnyCharacterInRange(_fleeTeamSet, FleeEnterRadius)) {
                SetTargetingType(NPCTargetingType.Flee);
            }
            else if (TargetingType != NPCTargetingType.Flee
                && TargetingType != NPCTargetingType.Attack
                && EnemyTeams != null
                && AnyCharacterInRange(EnemyTeams, AttackDetectionRadius)) {
                SetTargetingType(NPCTargetingType.Attack);
            }

            switch (TargetingType) {
                case NPCTargetingType.Random:
                    HandleRandom();
                    break;
                case NPCTargetingType.Character:
                    ServerPathfinding.UpdateTarget(agent, ref PreviousTarget, "Player", 0f);
                    break;
                case NPCTargetingType.Flee:
                    // Destination is driven by FleeRoutine; only check threat-clear here.
                    if (_fleeTeamSet.Count == 0 || !AnyCharacterInRange(_fleeTeamSet, ThreatClearRadius))
                        RevertToPrevious();
                    break;
                case NPCTargetingType.Attack:
                    if (EnemyTeams == null || EnemyTeams.Count == 0 || !AnyCharacterInRange(EnemyTeams, AttackClearRadius))
                        RevertToPrevious();
                    else
                        HandleAttackTick();
                    break;
                case NPCTargetingType.None:
                    agent.ResetPath();
                    break;
            }
        }

        // ─── Rotation ────────────────────────────────────────────────────────

        private void HandleRotation() {
            if (!agent.pathPending && agent.velocity.sqrMagnitude > 0.001f) {
                Vector3 dir = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(dir.magnitude == 0 ? Vector3.forward : dir),
                    agent.angularSpeed * Time.deltaTime
                );
            }
        }

        // ─── Random wandering ────────────────────────────────────────────────

        private void HandleRandom() {
            if (!agent.pathPending &&
                agent.remainingDistance <= agent.stoppingDistance) {
                PreviousTarget = null;
                PreviousTargetVec = null;
            }

            if (PreviousTargetVec == null) {
                PreviousTargetVec = ServerPathfinding.GetRandomPosition();
                agent.SetDestination(PreviousTargetVec.Value);
            }
        }

        // ─── Shared team-based detection (used by both Flee and Attack) ────────
        // Takes ICollection<TeamColor> so both FleeTeams' cached HashSet and EnemyTeams'
        // native HashSet can be passed straight in - no more ToList() copies per call.

        // Cheap early-out check: is any matching character within radius? Safe to call every frame.
        private bool AnyCharacterInRange(ICollection<TeamColor> teams, float radius) {
            foreach (TeamColor team in teams) {
                if (!GameCharacter.TeamToCharacter.ContainsKey(team)) continue;
                foreach (GameCharacter character in GameCharacter.TeamToCharacter[team]) {
                    if (character == null || character.IsDead()) continue;
                    if (Vector3.Distance(transform.position, character.transform.position) < radius)
                        return true;
                }
            }
            return false;
        }

        // Full list of matching characters within radius. More expensive - use sparingly
        // (e.g. on a recalc interval), not every frame.
        private List<GameCharacter> GetCharactersInRange(ICollection<TeamColor> teams, float radius) {
            List<GameCharacter> result = new List<GameCharacter>();
            if (teams == null) return result;

            foreach (TeamColor team in teams) {
                if (!GameCharacter.TeamToCharacter.ContainsKey(team)) continue;
                foreach (GameCharacter character in GameCharacter.TeamToCharacter[team]) {
                    if (character == null) continue;
                    if (Vector3.Distance(transform.position, character.transform.position) < radius)
                        result.Add(character);
                }
            }
            return result;
        }

        // Nearest matching character within maxRadius, or null if none found.
        private GameCharacter GetNearestCharacter(ICollection<TeamColor> teams, float maxRadius) {
            if (teams == null) return null;

            GameCharacter nearest = null;
            float nearestDist = float.MaxValue;

            foreach (TeamColor team in teams) {
                if (!GameCharacter.TeamToCharacter.ContainsKey(team)) continue;
                foreach (GameCharacter character in GameCharacter.TeamToCharacter[team]) {
                    if (character == null) continue;
                    float dist = Vector3.Distance(transform.position, character.transform.position);
                    if (dist < maxRadius && dist < nearestDist) {
                        nearestDist = dist;
                        nearest = character;
                    }
                }
            }
            return nearest;
        }

        // ─── Attack ──────────────────────────────────────────────────────────

        private IEnumerator AttackRoutine() {
            _currentAttackTarget = GetNearestCharacter(EnemyTeams, AttackClearRadius);

            while (true) {
                yield return new WaitForSeconds(AttackRetargetInterval);

                // Re-acquire target if it's gone or has wandered out of range.
                if (_currentAttackTarget == null
                    || Vector3.Distance(transform.position, _currentAttackTarget.transform.position) > AttackClearRadius) {
                    _currentAttackTarget = GetNearestCharacter(EnemyTeams, AttackClearRadius);
                }
            }
        }

        // Runs every frame from Update(). Deliberately cheap - one distance check, no NavMesh
        // or allocation work - since the actual movement destination is handled on its own
        // interval by AttackMovementRoutine below.
        private void HandleAttackTick() {
            if (_currentAttackTarget == null) {
                _currentAttackTarget = GetNearestCharacter(EnemyTeams, AttackClearRadius);
                if (_currentAttackTarget == null) return;
            }

            TrackTargetVelocity();

            float dist = Vector3.Distance(transform.position, _currentAttackTarget.transform.position);

            // RetreatToMinRangeEdge=false means retreat destinations are computed with the full
            // AttackRetreatDistance rather than stopping exactly at MinAttackRange - so cut the
            // retreat short here, every frame, the instant we're back in range instead of waiting
            // for the next AttackMoveRecalcInterval tick to notice and overshooting in the meantime.
            if (_isRetreating && !RetreatToMinRangeEdge && dist >= MinAttackRange) {
                agent.ResetPath();
                _isRetreating = false;
            }

            // When close and unobstructed, skip waiting on AttackMoveRecalcInterval entirely and
            // just chase every frame - the periodic recalc is fine for long-range approaches, but
            // it reads as laggy once the target is close and moving, since a whole interval can
            // pass before the destination catches up. Only takes over when we're not retreating
            // and there's a clear line of sight, so it never overrides backing off or fights the
            // NavMesh-routed approach when something's actually in the way.
            if (!_isRetreating && dist > MinAttackRange && dist <= DirectChaseRange && HasLineOfSightToTarget())
                agent.SetDestination(_currentAttackTarget.transform.position);

            // Only attack while sitting inside the [Min, Max] band - too close means we're
            // backing off, too far means we're still closing the distance.
            if (dist >= MinAttackRange && dist <= MaxAttackRange)
                TryAttack();
        }

        // Estimates the target's velocity by sampling its position over time. Resets cleanly
        // whenever the tracked target changes so we don't get a velocity spike from comparing
        // two different characters' positions.
        private void TrackTargetVelocity() {
            if (_currentAttackTarget == null) return;

            if (_velocityTrackedTarget != _currentAttackTarget) {
                _velocityTrackedTarget = _currentAttackTarget;
                _lastTargetPosition = _currentAttackTarget.transform.position;
                _lastTargetSampleTime = Time.time;
                _targetVelocity = Vector3.zero;
                return;
            }

            float dt = Time.time - _lastTargetSampleTime;
            if (dt > 0.0001f) {
                Vector3 currentPos = _currentAttackTarget.transform.position;
                _targetVelocity = (currentPos - _lastTargetPosition) / dt;
                _lastTargetPosition = currentPos;
                _lastTargetSampleTime = Time.time;
            }
        }

        // Where the target is predicted to be AttackPredictionLeadTime seconds from now, based on
        // our own estimated velocity for it.
        private Vector3 GetPredictedTargetPosition() {
            if (_currentAttackTarget == null) return Vector3.zero;
            return _currentAttackTarget.transform.position + _targetVelocity * AttackPredictionLeadTime;
        }

        // Cheap-ish line-of-sight check for the direct chase: a single raycast between roughly
        // chest height on both sides, so it doesn't skim the floor on slopes. Only counts layers
        // in DirectChaseObstacleMask as blockers - set that to your walls/terrain layer(s) so
        // other characters standing between us and the target don't count as an obstruction.
        private bool HasLineOfSightToTarget() {
            if (_currentAttackTarget == null) return false;

            Vector3 origin = transform.position + Vector3.up;
            Vector3 targetPos = _currentAttackTarget.transform.position + Vector3.up;
            Vector3 delta = targetPos - origin;

            return !Physics.Raycast(origin, delta.normalized, delta.magnitude, DirectChaseObstacleMask);
        }

        // Recalculates the movement destination on an interval instead of every frame - this
        // is what stops the constant re-pathing that was causing the freezing/stuttering.
        private IEnumerator AttackMovementRoutine() {
            UpdateAttackDestination();
            while (true) {
                yield return new WaitForSeconds(AttackMoveRecalcInterval);
                UpdateAttackDestination();
            }
        }

        private void UpdateAttackDestination() {
            if (_currentAttackTarget == null) return;

            float dist = Vector3.Distance(transform.position, _currentAttackTarget.transform.position);

            if (dist < MinAttackRange) {
                _isRetreating = true;
                Vector3 dest = ComputeRetreatDestination(dist);
                if (dest != Vector3.zero) agent.SetDestination(dest);
            }
            else if (dist > MaxAttackRange) {
                _isRetreating = false;
                Vector3 dest = ComputeApproachDestination(dist);
                if (dest != Vector3.zero) agent.SetDestination(dest);
            }
            else {
                _isRetreating = false;
                if (AllowMovementWhileAttacking)
                    // Keep closing in on where the target is predicted to be instead of freezing
                    // at the computed standoff point (or chasing its stale current position) -
                    // fixes NPCs that sit "in range" per the distance check but are still too far
                    // from the target's actual hitbox/pivot to land hits.
                    agent.SetDestination(GetPredictedTargetPosition());
                else
                    agent.ResetPath();
            }
        }

        // Aims to close the gap - normally to just inside MaxAttackRange, or all the way down to
        // MinAttackRange when ApproachToMinRangeEdge is set. Aims at the target's *predicted*
        // position (current position + estimated velocity * AttackPredictionLeadTime) rather than
        // where it currently is, so the NPC leads a moving target instead of always trailing it.
        // Only weaves to a random side while genuinely out of range (farther than
        // AttackDetectionRadius, e.g. re-engaging after the target ran off) - once already within
        // detection range it just goes straight at the approach point instead of weaving.
        private Vector3 ComputeApproachDestination(float dist) {
            if (_currentAttackTarget == null) return Vector3.zero;

            Vector3 predictedPos = GetPredictedTargetPosition();
            Vector3 toTarget = predictedPos - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return Vector3.zero;

            Vector3 dir = toTarget.normalized;
            float standoff = ApproachToMinRangeEdge ? MinAttackRange : (MaxAttackRange - AttackApproachStopShort);
            float travelDist = Mathf.Max(toTarget.magnitude - standoff, 0f);
            Vector3 candidate = transform.position + dir * travelDist;

            if (dist > AttackDetectionRadius) {
                Vector3 sideways = Vector3.Cross(Vector3.up, dir);
                float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                candidate += sideways * side * AttackApproachSideOffset;
            }

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                return Vector3.zero;

            return hit.position;
        }

        // Backs straight away from the target's *predicted* position (current position + estimated
        // velocity * AttackPredictionLeadTime), so it retreats away from where the target is
        // heading rather than where it currently stands. If RetreatToMinRangeEdge is set, the
        // destination is computed to land exactly on the MinAttackRange edge (the absolute minimum
        // distance); otherwise it aims for the full AttackRetreatDistance, but HandleAttackTick
        // cuts the move short the instant we're back in range rather than letting it run all the
        // way there.
        private Vector3 ComputeRetreatDestination(float dist) {
            if (_currentAttackTarget == null) return Vector3.zero;

            Vector3 predictedPos = GetPredictedTargetPosition();
            Vector3 away = transform.position - predictedPos;
            away.y = 0f;
            away = away.sqrMagnitude < 0.01f ? -transform.forward : away.normalized;

            float retreatDist = RetreatToMinRangeEdge
                ? Mathf.Max(MinAttackRange - dist, 0f)
                : AttackRetreatDistance;

            Vector3 candidate = transform.position + away * retreatDist;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                return Vector3.zero;

            return hit.position;
        }

        private void TryAttack() {
            if (Time.time - _lastAttackTime < AttackCooldown) return;
            _lastAttackTime = Time.time;

            if (AttackFunction != null)
                AttackFunction.Invoke(_currentAttackTarget);
            else
                Debug.LogWarning($"[{name}] In range to attack {_currentAttackTarget.name} but no AttackFunction is assigned.");
        }

        // ─── Flee ────────────────────────────────────────────────────────────

        private IEnumerator FleeRoutine() {
            // Always pick a destination immediately on enter
            PickFleeDestination();

            while (true) {
                yield return new WaitForSeconds(FleeRecalcInterval);

                // Skip recalc if we're still making good progress toward the destination
                if (agent.hasPath
                    && agent.pathStatus == NavMeshPathStatus.PathComplete
                    && agent.remainingDistance > agent.stoppingDistance + 0.5f)
                    continue;

                PickFleeDestination();
            }
        }

        private void PickFleeDestination() {
            List<GameCharacter> threats = GetCharactersInRange(_fleeTeamSet, ThreatClearRadius);
            if (threats.Count == 0) return;

            Vector3 fleeDir = ComputeFleeVector(threats);
            Vector3 best = FindBestFleeDestination(fleeDir, threats);
            if (best != Vector3.zero) {
                _lastFleeDestination = best;
                agent.SetDestination(best);
            }
        }

        // Weighted sum of repulsion vectors from all nearby threats.
        private Vector3 ComputeFleeVector(List<GameCharacter> threats) {
            Vector3 flee = Vector3.zero;
            foreach (GameCharacter threat in threats) {
                Vector3 delta = transform.position - threat.transform.position;
                // Inverse-square weighting so close threats dominate.
                flee += delta.normalized / Mathf.Max(delta.sqrMagnitude, 0.1f);
            }
            return flee.sqrMagnitude > 0f ? flee.normalized : transform.forward;
        }

        // Samples candidate points, rejects unreachable ones, scores the rest.
        private Vector3 FindBestFleeDestination(Vector3 fleeDir, List<GameCharacter> threats) {
            Vector3 bestPos = Vector3.zero;
            float bestScore = float.NegativeInfinity;
            NavMeshPath path = new NavMeshPath();

            int candidateCount = Mathf.Max(FleeCandidateCount, 2); // guard divide-by-zero below
            float angleStep = 180f / (candidateCount - 1);

            for (int i = 0; i < candidateCount; i++) {
                float angle = -90f + angleStep * i;
                Vector3 rotated = Quaternion.Euler(0, angle, 0) * fleeDir;
                Vector3 candidate = transform.position + rotated * FleeDistance;

                Vector3 jitter = UnityEngine.Random.insideUnitSphere * FleeRandomRadius;
                jitter.y = 0f;
                candidate += jitter;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    continue;

                // Reject destinations too close - these are almost always wall hugs or bad edges
                if (Vector3.Distance(transform.position, hit.position) < 2f)
                    continue;

                if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path))
                    continue;

                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float score = ScoreCandidate(hit.position, path, threats);
                if (score > bestScore) {
                    bestScore = score;
                    bestPos = hit.position;
                }
            }

            return bestPos;
        }

        private float ScoreCandidate(Vector3 candidate, NavMeshPath path, List<GameCharacter> threats) {
            float minThreatDist = float.MaxValue;
            foreach (GameCharacter threat in threats) {
                float d = Vector3.Distance(candidate, threat.transform.position);
                if (d < minThreatDist) minThreatDist = d;
            }

            float pathLen = PathLength(path);

            float losPenalty = 0f;
            foreach (GameCharacter threat in threats) {
                Vector3 toCandidate = candidate - threat.transform.position;
                if (!Physics.Raycast(threat.transform.position, toCandidate.normalized, toCandidate.magnitude))
                    losPenalty += 4f;
            }

            // Penalize returning to where we just were - this kills oscillation.
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

        private float PathLength(NavMeshPath path) {
            float len = 0f;
            Vector3[] corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
                len += Vector3.Distance(corners[i - 1], corners[i]);
            return len;
        }
    }
}