using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
        [SerializeField] public float WalkSpeed = 18.0f;
        [SerializeField] public float FleeSpeed = 28.0f;
        [SerializeField] public float AttackSpeed = 20.0f;
        public static float WalkSpeedMultiplier = 1.0f;
        public static float FleeSpeedMultiplier = 1.0f;
        public static float AttackSpeedMultiplier = 1.0f;

        [Header("Targeting")]
        public NPCTargetingType TargetingType = NPCTargetingType.Random;
        private NPCTargetingType _previousTargetingType = NPCTargetingType.Random;
        [NonSerialized] public DamageType AttackDamageType; // set in runtime by game specific script
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
        // Do not index TeamEnemies directly. NPCs are created before their game-specific team
        // setup has necessarily finished, and a missing entry used to throw from Update before
        // the random-wander code could run.
        private static readonly HashSet<TeamColor> EmptyTeamSet = new();
        private HashSet<TeamColor> EnemyTeams {
            get {
                if (gameCharacter == null || SharedGlobalEvents.TeamEnemies == null)
                    return EmptyTeamSet;

                return SharedGlobalEvents.TeamEnemies.TryGetValue(gameCharacter.GetTeam().team, out HashSet<TeamColor> teams)
                    ? teams
                    : EmptyTeamSet;
            }
        }
        [SerializeField] public float AttackDetectionRadius = 10f; // enemy this close -> start attacking
        [SerializeField] private float MinAttackRange = 0f;         // closer than this -> back off (0 = melee, never backs off)
        [SerializeField] private float MaxAttackRange = 2f;         // farther than this -> close the distance (melee reach)
        [SerializeField] private float AttackRetargetInterval = 1f;
        [SerializeField] private float AttackClearRadius = 15f;     // no enemies this close -> stop attacking
        [SerializeField] private float AttackCooldown = 1f;         // min seconds between AttackFunction invocations
        [SerializeField] private float RetargetSwitchThreshold = 0.7f; // on each retarget check, only switch off a still-valid target onto a different enemy if the alternative is at least this much closer (as a fraction of the current target's distance) - stops the NPC fixating on whichever enemy it happened to see first while a much closer attacker goes ignored, while still avoiding thrash between two similarly-distant targets every retarget tick
        // MaxAttackRange is also a valid engagement distance: a ranged NPC should enter combat
        // and fire at an enemy that is already in its configured attack band, even if the
        // inspector's detection radius was left at its smaller melee-era default.
        private float AttackEngagementRadius => Mathf.Max(AttackDetectionRadius, MaxAttackRange);
        private float AttackTrackingRadius => Mathf.Max(AttackEngagementRadius, AttackClearRadius);

        [Header("Attack Movement")]
        [SerializeField] private float DestinationUpdateThreshold = 0.5f; // only re-path when the desired destination has moved at least this far since the last time we committed to one - see UpdateAttackMovement for why
        [SerializeField] private float AttackApproachSideOffset = 0f;   // lateral offset so the approach weaves instead of beelining - 0 disables weaving entirely
        [SerializeField] private float AttackApproachStopShort = 0.5f;  // stop this far inside MaxAttackRange when ApproachToMinRangeEdge is false
        [SerializeField] private bool ApproachToMinRangeEdge = true;    // true: close the gap all the way down to MinAttackRange (as close as possible). false: stop just inside MaxAttackRange.
        [SerializeField] private bool AllowMovementWhileAttacking = true; // true: keeps walking straight at the target while inside the [Min, Max] band instead of freezing at the standoff point - use this for melee, where the computed standoff can land just short of actual hit range. Set false to keep the NPC planted in place while attacking (e.g. for ranged units that want to hold a fixed distance).
        [SerializeField] private bool RetreatToMinRangeEdge = true;    // true: always run all the way out to the absolute MinAttackRange edge. false: retreat using AttackRetreatDistance but stop as soon as we're back in range.
        [SerializeField] private float AttackRetreatDistance = 3f;      // only used when RetreatToMinRangeEdge is false
        [SerializeField] private float AttackPredictionLeadTime = 0f;   // seconds to lead the target's estimated velocity by. 0 disables prediction entirely (chases actual current position).
        [SerializeField] private float AttackRangeBuffer = 0.5f;        // margin kept beyond MinAttackRange when computing the standoff point the NPC actually walks to. Without this, the point the NPC approaches toward was the exact same point that triggers retreat, so ordinary movement overshoot flipped it between closing in and backing off every frame - the main cause of the walking-back-and-forth bug. Also stops melee NPCs (MinAttackRange = 0) from aiming at the target's exact pivot.
        [SerializeField] private float DestinationArrivalTolerance = 0.15f; // avoids repeatedly asking the agent to reach an effectively identical point

        // Assign this from elsewhere (e.g. another script's Awake/Start) to define what
        // "attacking" actually does for this NPC, e.g.:
        //   npc.AttackFunction = target => target.TakeDamage(10);
        // Not serialized - Unity can't show a delegate in the Inspector, so it must be
        // wired up in code.
        public Action<GameCharacter> AttackFunction;

        // Optional game-specific hooks. A multiplier greater than one lets a target be
        // acquired and retained from farther away; a higher priority is selected before a
        // lower-priority target, with distance breaking ties. LocalNPC deliberately has no
        // knowledge of roles, weapons, or individual game modes.
        public Func<GameCharacter, float> AttackTargetRangeMultiplier;
        public Func<GameCharacter, float> AttackTargetPriority;

        public NavMeshAgent agent;

        private Coroutine _fleeCoroutine;
        private Coroutine _attackCoroutine;

        private Vector3 _lastFleeDestination = Vector3.zero;

        private GameCharacter _currentAttackTarget;
        public GameCharacter CurrentAttackTarget => _currentAttackTarget;
        private GameCharacter _forcedAttackTarget;
        private float _lastAttackTime = -Mathf.Infinity;

        // Estimated target velocity, used to predict where it's going instead of chasing/fleeing
        // where it currently is. We have to estimate this ourselves by sampling its position over
        // time - there's no velocity we can read directly off an arbitrary GameCharacter.
        private GameCharacter _velocityTrackedTarget;
        private Vector3 _lastTargetPosition;
        private float _lastTargetSampleTime = -1f;
        private Vector3 _targetVelocity;

        // Tracks the last movement point we actually committed a path to, so we can tell whether
        // the desired destination has moved far enough to be worth re-pathing over.
        private Vector3 _lastAttackMoveCandidate;
        private bool _hasAttackMoveCandidate;
        private float _attackApproachSide = 1f;
        private NavMeshPath _destinationPath;

        void Awake() {
            gameCharacter = GetComponent<GameCharacter>();
            agent = GetComponent<NavMeshAgent>();
            _destinationPath = new NavMeshPath();
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
            agent.isStopped = TargetingType == NPCTargetingType.None;
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
            if (CanNavigate())
                agent.isStopped = newType == NPCTargetingType.None;
            UpdateSpeed();

            if (newType == NPCTargetingType.Flee)
                _fleeCoroutine = StartCoroutine(FleeRoutine());
            else if (newType == NPCTargetingType.Attack)
                _attackCoroutine = StartCoroutine(AttackRoutine());
        }

        private void StopStateCoroutines() {
            if (_fleeCoroutine != null) {
                StopCoroutine(_fleeCoroutine);
                _fleeCoroutine = null;
            }
            if (_attackCoroutine != null) {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
                SetAttackTarget(null);
                _forcedAttackTarget = null;
                _velocityTrackedTarget = null;
                _targetVelocity = Vector3.zero;
                _lastTargetSampleTime = -1f;
                _hasAttackMoveCandidate = false;
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
                && AnyEnemyInAttackRange(AttackEngagementRadius)) {
                SetTargetingType(NPCTargetingType.Attack);
            }

            switch (TargetingType) {
                case NPCTargetingType.Random:
                    HandleRandom();
                    break;
                case NPCTargetingType.Character:
                    agent.isStopped = false;
                    ServerPathfinding.UpdateTarget(agent, ref PreviousTarget, "Player", 0f);
                    break;
                case NPCTargetingType.Flee:
                    // Destination is driven by FleeRoutine; only check threat-clear here.
                    if (_fleeTeamSet.Count == 0 || !AnyCharacterInRange(_fleeTeamSet, ThreatClearRadius))
                        RevertToPrevious();
                    break;
                case NPCTargetingType.Attack:
                    if ((EnemyTeams == null || EnemyTeams.Count == 0 || !AnyEnemyInAttackRange(AttackTrackingRadius))
                        && !HasValidForcedAttackTarget())
                        RevertToPrevious();
                    else
                        HandleAttackTick();
                    break;
                case NPCTargetingType.None:
                    StopMovement();
                    break;
            }
        }

        // --- Rotation --------------------------------------------------------

        private void HandleRotation() {
            // A melee weapon's hit collider is attached to the animated hand, so movement
            // direction is not a reliable aim direction once an NPC reaches its target (or
            // takes a curved NavMesh path). Keep an attacking NPC facing its actual target;
            // this also continues to turn while the agent is stopped during the attack swing.
            Vector3 direction;
            if (TargetingType == NPCTargetingType.Attack && _currentAttackTarget != null) {
                direction = _currentAttackTarget.transform.position - transform.position;
            } else {
                if (agent.pathPending || agent.velocity.sqrMagnitude <= 0.001f)
                    return;
                direction = agent.velocity;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
                return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction),
                agent.angularSpeed * Time.deltaTime
            );
        }

        // --- Random wandering ------------------------------------------------

        private void HandleRandom() {
            if (!CanNavigate()) return;

            // A failed SetDestination leaves hasPath false and remainingDistance at zero. The
            // old code only cleared a target after arriving, so one bad random point made the
            // NPC stand still forever until combat took over.
            if (!agent.pathPending
                && (!agent.hasPath
                    || agent.pathStatus != NavMeshPathStatus.PathComplete
                    || agent.remainingDistance <= agent.stoppingDistance)) {
                PreviousTarget = null;
                PreviousTargetVec = null;
            }

            if (PreviousTargetVec == null) {
                // The sampled NavMesh triangle is usually reachable, but it may be on a
                // disconnected island. Keep trying a small bounded number of points and only
                // retain a point after a complete path has been accepted.
                for (int i = 0; i < 8; i++) {
                    Vector3 candidate = ServerPathfinding.GetRandomPosition();
                    if (TrySetDestination(candidate)) {
                        PreviousTargetVec = candidate;
                        break;
                    }
                }
            }
        }

        // --- Shared team-based detection (used by both Flee and Attack) --------
        // Takes ICollection<TeamColor> so both FleeTeams' cached HashSet and EnemyTeams'
        // native HashSet can be passed straight in - no more ToList() copies per call.

        // Cheap early-out check: is any matching character within radius? Safe to call every frame.
        private bool AnyCharacterInRange(ICollection<TeamColor> teams, float radius) {
            if (teams == null || gameCharacter == null) return false;
            foreach (TeamColor team in teams) {
                if (!GameCharacter.TeamToCharacter.ContainsKey(team)) continue;
                foreach (GameCharacter character in GameCharacter.TeamToCharacter[team]) {
                    if (character == null || character.IsDead || character.IsProtected(gameCharacter, AttackDamageType)) continue;
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
                    if (character == null || character.IsDead || character.IsProtected(gameCharacter, AttackDamageType)) continue;
                    if (Vector3.Distance(transform.position, character.transform.position) < radius)
                        result.Add(character);
                }
            }
            return result;
        }

        // Nearest matching character within maxRadius, or null if none found. Skips dead
        // characters so a corpse never gets (re-)selected as an attack target.
        private GameCharacter GetNearestCharacter(ICollection<TeamColor> teams, float maxRadius) {
            if (teams == null || gameCharacter == null) return null;

            GameCharacter nearest = null;
            float nearestDist = float.MaxValue;
            float highestPriority = float.MinValue;

            foreach (TeamColor team in teams) {
                if (!GameCharacter.TeamToCharacter.ContainsKey(team)) continue;
                foreach (GameCharacter character in GameCharacter.TeamToCharacter[team]) {
                    if (character == null || character.IsDead || character.IsProtected(gameCharacter, AttackDamageType)) continue;
                    float dist = Vector3.Distance(transform.position, character.transform.position);
                    if (dist >= GetAttackTargetRange(character, maxRadius)) continue;

                    float priority = GetAttackTargetPriority(character);
                    if (priority > highestPriority
                        || (Mathf.Approximately(priority, highestPriority) && dist < nearestDist)) {
                        highestPriority = priority;
                        nearestDist = dist;
                        nearest = character;
                    }
                }
            }
            return nearest;
        }

        private float GetAttackTargetRange(GameCharacter target, float baseRange) {
            float multiplier = AttackTargetRangeMultiplier?.Invoke(target) ?? 1f;
            return baseRange * Mathf.Max(0f, multiplier);
        }

        private float GetAttackTargetPriority(GameCharacter target) {
            return AttackTargetPriority?.Invoke(target) ?? 0f;
        }

        private bool AnyEnemyInAttackRange(float baseRange) {
            if (gameCharacter == null) return false;

            foreach (TeamColor team in EnemyTeams) {
                if (!GameCharacter.TeamToCharacter.TryGetValue(team, out HashSet<GameCharacter> characters)) continue;
                foreach (GameCharacter character in characters) {
                    if (character == null || character.IsDead || character.IsProtected(gameCharacter, AttackDamageType)) continue;
                    if (Vector3.Distance(transform.position, character.transform.position) < GetAttackTargetRange(character, baseRange))
                        return true;
                }
            }
            return false;
        }

        private bool IsValidAttackTarget(GameCharacter target) {
            return target != null
                && !target.IsDead
                && EnemyTeams.Contains(target.GetTeam().team)
                && !target.IsProtected(gameCharacter, AttackDamageType);
        }

        private bool HasValidForcedAttackTarget() {
            return IsValidAttackTarget(_forcedAttackTarget);
        }

        /// <summary>
        /// Immediately enters attack mode and holds this valid enemy as the attack target until
        /// it is no longer valid. Game-specific behaviours can use this for reactions such as
        /// retaliating against an attacker outside the NPC's normal acquisition radius.
        /// </summary>
        public bool TargetCharacter(GameCharacter target) {
            if (!IsValidAttackTarget(target)) return false;

            SetTargetingType(NPCTargetingType.Attack);
            _forcedAttackTarget = target;
            SetAttackTarget(target);
            return true;
        }

        // --- Attack ----------------------------------------------------------

        // Centralizes changing _currentAttackTarget so the OnDied subscription always stays in
        // sync - subscribe to the new target, unsubscribe from the old one, and clear any stale
        // movement/velocity tracking that referred to the previous target.
        private void SetAttackTarget(GameCharacter target) {
            if (_currentAttackTarget == target) return;

            if (_currentAttackTarget != null)
                _currentAttackTarget.OnDied -= HandleAttackTargetDied;

            _currentAttackTarget = target;
            _hasAttackMoveCandidate = false; // force a fresh destination for the new target (or stop cleanly if null)
            _attackApproachSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;

            if (_currentAttackTarget != null)
                _currentAttackTarget.OnDied += HandleAttackTargetDied;
        }

        // Fires the instant our current target dies, so we drop it immediately instead of
        // continuing to chase/hold at a corpse until the periodic check in AttackRoutine (or the
        // dead-target check at the top of HandleAttackTick) eventually notices. Those periodic
        // checks stay in place as a safety net in case this event is ever missed.
        private void HandleAttackTargetDied(DamageType source, IEntity killer) {
            if (_forcedAttackTarget == _currentAttackTarget)
                _forcedAttackTarget = null;
            SetAttackTarget(null);
        }

        private IEnumerator AttackRoutine() {
            SetAttackTarget(GetNearestCharacter(EnemyTeams, AttackTrackingRadius));

            while (true) {
                yield return new WaitForSeconds(AttackRetargetInterval);

                if (_forcedAttackTarget != null) {
                    if (IsValidAttackTarget(_forcedAttackTarget)) {
                        SetAttackTarget(_forcedAttackTarget);
                        continue;
                    }
                    _forcedAttackTarget = null;
                }

                // Re-acquire target if it's gone, died, or has wandered out of range. The death
                // check here is just a safety net - HandleAttackTargetDied normally clears the
                // target immediately via the OnDied event well before this runs.
                if (_currentAttackTarget == null
                    || !IsValidAttackTarget(_currentAttackTarget)
                    || Vector3.Distance(transform.position, _currentAttackTarget.transform.position)
                        > GetAttackTargetRange(_currentAttackTarget, AttackTrackingRadius)) {
                    SetAttackTarget(GetNearestCharacter(EnemyTeams, AttackTrackingRadius));
                    continue;
                }

                // Current target is still valid, but check whether someone closer has shown up.
                // Without this, the NPC fixates on whichever enemy it targeted first for as long
                // as that enemy stays inside AttackClearRadius - which is generous, well beyond
                // AttackTrackingRadius - so it would keep chasing one target across the map while
                // ignoring other enemies right next to it. Only switches when the alternative is
                // meaningfully closer (RetargetSwitchThreshold) so it doesn't thrash between two
                // similarly-distant targets every retarget tick.
                GameCharacter closest = GetNearestCharacter(EnemyTeams, AttackTrackingRadius);
                if (closest != null && closest != _currentAttackTarget) {
                    float closestDist = Vector3.Distance(transform.position, closest.transform.position);
                    float currentDist = Vector3.Distance(transform.position, _currentAttackTarget.transform.position);
                    float closestPriority = GetAttackTargetPriority(closest);
                    float currentPriority = GetAttackTargetPriority(_currentAttackTarget);

                    // A game-specific priority is an explicit override, not merely a distance
                    // tiebreaker. Keep the distance threshold only for equally-ranked targets.
                    if (closestPriority > currentPriority
                        || (Mathf.Approximately(closestPriority, currentPriority)
                            && closestDist < currentDist * RetargetSwitchThreshold))
                        SetAttackTarget(closest);
                }
            }
        }

        // Runs every frame from Update(). Handles retargeting, velocity tracking, movement, and
        // attacking - all driven straight off distance checks each frame rather than a mix of
        // frame-rate and interval-rate logic, which is what was causing the stutter.
        private void HandleAttackTick() {
            // Extra safety net - see HandleAttackTargetDied for the primary (immediate) path.
            if (_currentAttackTarget != null && _currentAttackTarget.IsDead)
                SetAttackTarget(null);

            if (_currentAttackTarget == null) {
                SetAttackTarget(GetNearestCharacter(EnemyTeams, AttackTrackingRadius));
                if (_currentAttackTarget == null) return;
            }

            TrackTargetVelocity();

            float dist = Vector3.Distance(transform.position, _currentAttackTarget.transform.position);

            UpdateAttackMovement(dist);

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
        // our own estimated velocity for it. With AttackPredictionLeadTime at 0 (the default) this
        // is just the target's actual current position.
        private Vector3 GetPredictedTargetPosition() {
            if (_currentAttackTarget == null) return transform.position;
            return _currentAttackTarget.transform.position + _targetVelocity * AttackPredictionLeadTime;
        }

        // Decides how the NPC should move this frame. Destinations are gated by
        // DestinationUpdateThreshold, but movement always stays under NavMeshAgent control so
        // walls, links, and agent avoidance are handled consistently.
        private void UpdateAttackMovement(float dist) {
            if (_currentAttackTarget == null) return;

            if (dist < MinAttackRange) {
                CommitPathDestination(ComputeRetreatCandidate(dist));
                return;
            }

            bool wantsToMove = dist > MaxAttackRange || AllowMovementWhileAttacking;
            if (!wantsToMove) {
                StopMovement();
                _hasAttackMoveCandidate = false;
                return;
            }

            // Let NavMeshAgent follow the route; manually assigning velocity bypasses obstacle
            // avoidance and causes repeated wall collisions.
            CommitPathDestination(ComputeApproachCandidate(dist));
        }

        // Shared by the retreat and far/obstructed-approach cases: only actually re-paths once the
        // desired point has moved farther than DestinationUpdateThreshold from the last one we
        // committed to. If NavMesh.SamplePosition fails to resolve a candidate, the "last committed"
        // point is deliberately left unchanged so we retry again next frame instead of getting stuck
        // waiting for the raw candidate to drift another full threshold away.
        private void CommitPathDestination(Vector3 candidate) {
            if (!CanNavigate()) return;

            Vector3 offset = candidate - transform.position;
            offset.y = 0f;
            float arrivalTolerance = Mathf.Max(agent.stoppingDistance, DestinationArrivalTolerance);
            if (offset.sqrMagnitude <= arrivalTolerance * arrivalTolerance) {
                StopMovement();
                _hasAttackMoveCandidate = false;
                return;
            }

            if (_hasAttackMoveCandidate
                && (candidate - _lastAttackMoveCandidate).sqrMagnitude < DestinationUpdateThreshold * DestinationUpdateThreshold
                && (agent.pathPending
                    || (agent.hasPath
                        && agent.pathStatus == NavMeshPathStatus.PathComplete
                        && !agent.isPathStale)))
                return;

            if (TrySetDestination(candidate)) {
                _lastAttackMoveCandidate = candidate;
                _hasAttackMoveCandidate = true;
            }
        }

        private bool CanNavigate() {
            return agent != null && agent.enabled && agent.isOnNavMesh;
        }

        // Sampling alone is not enough: it can resolve a point on the other side of a wall or
        // on a disconnected NavMesh island. Only commit destinations that have a complete route
        // from the NPC's current position.
        private bool TrySetDestination(Vector3 candidate) {
            if (!CanNavigate() || _destinationPath == null) return false;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, agent.areaMask)) return false;
            if (!NavMesh.CalculatePath(transform.position, hit.position, agent.areaMask, _destinationPath)) return false;
            if (_destinationPath.status != NavMeshPathStatus.PathComplete) return false;

            agent.isStopped = false;
            return agent.SetDestination(hit.position);
        }

        private void StopMovement() {
            if (!CanNavigate()) return;
            if (agent.hasPath || agent.pathPending)
                agent.ResetPath();
            agent.isStopped = true;
        }

        // Point offset back from the target's predicted position by the standoff distance.
        // AttackRangeBuffer keeps this resting point outside the retreat trigger rather than
        // exactly on it (see ComputeRetreatCandidate).
        private Vector3 GetStandoffPoint() {
            if (_currentAttackTarget == null) return transform.position;

            Vector3 predictedPos = GetPredictedTargetPosition();
            Vector3 toTarget = predictedPos - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return transform.position;

            float standoff = ApproachToMinRangeEdge ? (MinAttackRange + AttackRangeBuffer) : (MaxAttackRange - AttackApproachStopShort);
            float travelDist = Mathf.Max(toTarget.magnitude - standoff, 0f);
            return transform.position + toTarget.normalized * travelDist;
        }

        // Same standoff point, with an optional stable side offset while re-engaging from far
        // away. Choosing a random side every frame would continually invalidate the active path.
        private Vector3 ComputeApproachCandidate(float dist) {
            if (_currentAttackTarget == null) return transform.position;

            Vector3 candidate = GetStandoffPoint();

            if (dist > AttackDetectionRadius) {
                Vector3 toTarget = GetPredictedTargetPosition() - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude >= 0.01f) {
                    Vector3 sideways = Vector3.Cross(Vector3.up, toTarget.normalized);
                    candidate += sideways * _attackApproachSide * AttackApproachSideOffset;
                }
            }

            return candidate;
        }

        // Raw (unsampled) retreat point, straight away from the target's predicted position. If
        // RetreatToMinRangeEdge is set, lands on MinAttackRange + AttackRangeBuffer - the same
        // resting point GetStandoffPoint approaches toward, not the bare MinAttackRange edge.
        // Landing on the exact trigger threshold used to mean ordinary movement overshoot could
        // immediately re-trigger retreat (or get pulled straight back in by approach), producing
        // a fast back-and-forth flicker; the buffer gives it somewhere to actually settle.
        // Otherwise aims for the full AttackRetreatDistance - once dist crosses back above
        // MinAttackRange, UpdateAttackMovement's branch switches away from retreat on the very
        // next frame regardless, so there's no separate "cut the retreat short" logic needed here.
        private Vector3 ComputeRetreatCandidate(float dist) {
            if (_currentAttackTarget == null) return transform.position;

            Vector3 predictedPos = GetPredictedTargetPosition();
            Vector3 away = transform.position - predictedPos;
            away.y = 0f;
            away = away.sqrMagnitude < 0.01f ? -transform.forward : away.normalized;

            float retreatDist = RetreatToMinRangeEdge
                ? Mathf.Max(MinAttackRange + AttackRangeBuffer - dist, 0f)
                : AttackRetreatDistance;

            return transform.position + away * retreatDist;
        }

        private void TryAttack() {
            if (Time.time - _lastAttackTime < AttackCooldown) return;
            _lastAttackTime = Time.time;

            if (AttackFunction != null)
                AttackFunction.Invoke(_currentAttackTarget);
            else
                Debug.LogWarning($"[{name}] In range to attack {_currentAttackTarget.name} but no AttackFunction is assigned.");
        }

        // --- Flee ------------------------------------------------------------

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
            if (best != Vector3.zero && TrySetDestination(best)) {
                _lastFleeDestination = best;
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
