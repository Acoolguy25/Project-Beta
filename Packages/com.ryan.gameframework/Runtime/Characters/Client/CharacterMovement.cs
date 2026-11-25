using System.Collections.Generic;
using FishNet;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientCore;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.DataService;
using RyanAssets.Input;
using UnityEngine;
using UnityEngine.Serialization;

namespace RyanAssets.Characters.Client {
    /// <summary>
    /// A special way of moving, such as a parkour move. Modes get the first look at every physics step;
    /// while one returns true it drives the body for that step instead of normal walking, jumping and
    /// gravity. Register with <see cref="CharacterMovement.AddMode"/>.
    /// </summary>
    public interface IMovementMode {
        bool Step(CharacterMovement movement, float dt);
    }

    /// <summary>
    /// Client-authoritative movement for the local player's character.
    /// <para>
    /// Every vector is measured in the character's gravity frame (<see cref="CharacterGravity"/>):
    /// "up" is wherever gravity is not, "planar" means perpendicular to up. With default gravity this
    /// is ordinary ground movement; inside a gravity volume the same code walks, jumps and steps on
    /// walls and ceilings, and the body turns smoothly to stand on the new surface.
    /// </para>
    /// <para>
    /// Each physics step runs: resolve gravity → orient → movement modes (<see cref="IMovementMode"/>)
    /// → jump → gravity → planar movement.
    /// </para>
    /// </summary>
    public class CharacterMovement : MonoBehaviour {
        // ------------------------------------------------------------------ settings
        [Header("Speed")]
        [Tooltip("Walking speed (m/s). Replaced at runtime by the server's PlayerData.walkSpeed.")]
        [SerializeField, FormerlySerializedAs("MoveSpeed"), Min(0f)] private float walkSpeed = 2f;
        [Tooltip("Sprinting speed (m/s). Replaced at runtime by the server's PlayerData.sprintSpeed.")]
        [SerializeField, FormerlySerializedAs("SprintSpeed"), Min(0f)] private float sprintSpeed = 5.335f;
        [Tooltip("Stamina spent per second while sprinting.")]
        [SerializeField, FormerlySerializedAs("SprintStaminaConsumptionRate"), Min(0f)] private float sprintStaminaPerSecond = 10f;
        [Tooltip("How quickly speed approaches its target, per second. Higher feels snappier.")]
        [SerializeField, FormerlySerializedAs("SpeedChangeRate"), Min(0f)] private float acceleration = 10f;
        [Tooltip("Seconds the body takes to turn toward the movement direction.")]
        [SerializeField, FormerlySerializedAs("RotationSmoothTime"), Range(0f, 0.3f)] private float turnSmoothTime = 0.12f;

        [Header("Jumping")]
        [Tooltip("Stamina spent per jump.")]
        [SerializeField, Min(0f)] private float jumpStaminaCost = 5f;
        [Tooltip("Seconds after a jump or launch during which wall contacts may not cancel the upward velocity.")]
        [SerializeField, FormerlySerializedAs("JumpTimeout"), Min(0f)] private float jumpProtectionTime = 0.35f;
        [Tooltip("Seconds after landing before the next jump can start (0 = immediately).")]
        [SerializeField, FormerlySerializedAs("LandJumpTimeout"), Min(0f)] private float landingJumpDelay = 0f;
        [Tooltip("Seconds after walking off an edge that a jump still works.")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.1f;
        [Tooltip("Fastest the character can fall (m/s along gravity).")]
        [SerializeField, Min(0f)] private float terminalFallSpeed = 53f;

        [Header("Gravity")]
        [Tooltip("Height up the body it turns about when gravity changes direction (0 = feet, 0.5 = middle). " +
                 "Turning about the middle keeps the head from sweeping through the floor when gravity flips upside down. " +
                 "How long the turn takes is set on the character's CharacterGravity.")]
        [SerializeField, Range(0f, 1f)] private float turnPivotHeight = 0.5f;
        [Tooltip("While the body turns to a new gravity direction, how many times per step it is pushed out of anything its turning collider " +
                 "sinks into (0 = leave it to the physics engine, which jitters). Keeps wall and ceiling landings clean.")]
        [SerializeField, Range(0, 6)] private int turnDepenetrationPasses = 3;

        [Header("Steps and Collisions")]
        [Tooltip("Tallest ledge the character walks up without jumping.")]
        [SerializeField, FormerlySerializedAs("MaxStepHeight"), Min(0f)] private float maxStepHeight = 0.6f;
        [Tooltip("How far ahead of the body ledges are looked for.")]
        [SerializeField, FormerlySerializedAs("StepProbeDistance"), Min(0.01f)] private float stepProbeDistance = 0.12f;
        [Tooltip("Gap kept between the collider and geometry during step probes.")]
        [SerializeField, FormerlySerializedAs("CollisionSkin"), Range(0.01f, 0.1f)] private float collisionSkin = 0.03f;
        [Tooltip("Minimum dot(step surface normal, up) for a ledge top to count as a step.")]
        [SerializeField, FormerlySerializedAs("MinimumStepGroundNormal"), Range(0f, 1f)] private float minimumStepUpDot = 0.65f;
        [Tooltip("Caps how fast the physics engine pushes the body out of overlaps.")]
        [SerializeField, FormerlySerializedAs("MaxDepenetrationVelocity"), Min(0f)] private float maxDepenetrationVelocity = 5f;
        [Tooltip("Layers ignored by step probes (characters).")]
        [SerializeField] private string[] stepIgnoredLayers = { "Character", "LocalCharacter" };

        [Header("External Motion")]
        [Tooltip("How quickly launch and wall-jump momentum fades on the ground (per second).")]
        [SerializeField, FormerlySerializedAs("ExternalGroundDamping"), Min(0f)] private float externalGroundDamping = 6f;
        [Tooltip("How quickly launch and wall-jump momentum fades in the air (per second).")]
        [SerializeField, FormerlySerializedAs("ExternalAirDamping"), Min(0f)] private float externalAirDamping = 0.6f;
        [Tooltip("Seconds after a launch before its momentum starts fading.")]
        [SerializeField, Min(0f)] private float launchProtectionTime = 0.2f;
        [Tooltip("Seconds after touching a wall-jump surface that a jump press still kicks off it.")]
        [SerializeField, FormerlySerializedAs("WallContactGrace"), Min(0f)] private float wallContactGrace = 0.15f;
        [Tooltip("Minimum seconds between wall jumps.")]
        [SerializeField, FormerlySerializedAs("WallJumpCooldown"), Min(0f)] private float wallJumpCooldown = 0.2f;
        [Tooltip("After a wall jump the body turns to face the way it is kicked (away from the wall) for this long (s), even while the stick points back at the wall.")]
        [SerializeField, Min(0f)] private float wallJumpTurnSeconds = 0.3f;
        [Tooltip("Turn smoothing (s) for that quick turn away from the wall.")]
        [SerializeField, Range(0.01f, 0.3f)] private float wallJumpTurnSmoothTime = 0.07f;
        [Tooltip("On slippery ground (ISurfaceTraction, such as ice), the fastest planar velocity change (m/s²) at traction 1; " +
                 "a surface's traction scales it. Normal ground keeps instant steering.")]
        [SerializeField, Min(0f)] private float slipAcceleration = 60f;

        /// <summary>Ledges lower than this (metres) are left to the physics engine; lifting for them only jitters.</summary>
        private const float MinimumStepHeight = 0.005f;

        // ------------------------------------------------------------------ public state
        /// <summary>The movement controller driving the local player's character, if any.</summary>
        public static CharacterMovement Local { get; private set; }
        public Rigidbody Body => _body;
        public bool Grounded => _ground != null && _ground.Grounded;
        /// <summary>The character's gravity; volumes change its direction and strength.</summary>
        public CharacterGravity Gravity => _gravity;
        /// <summary>The character's current gravity "up" (world up without a character).</summary>
        public Vector3 Up => _gravity != null ? _gravity.Up : Vector3.up;
        /// <summary>Planar momentum from launches, wall jumps and conveyors that fades over time.</summary>
        public Vector3 ExternalVelocity => _externalVelocity;
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        /// <summary>
        /// Whether the sprint key makes the character sprint. A game that turns sprint into something else
        /// (the obby's Dash) sets this to false; the default keeps sprint exactly as it always was.
        /// </summary>
        public bool SprintEnabled { get; set; } = true;
        /// <summary>True while wind or another push (<see cref="AddPush"/>) acted on the last step.</summary>
        public bool IsPushed => _push != Vector3.zero;
        /// <summary>Raised when a wall jump kicks off, with the wall's normal (planar, pointing away from the wall).</summary>
        public event System.Action<Vector3> WallJumped;
        /// <summary>True while a wall-jump surface was touched recently enough that a jump press still kicks off it.</summary>
        public bool HasWallContact => _wallSurface != null && Time.fixedTime - _wallContactTime <= wallContactGrace;
        /// <summary>Seconds since a wall-jump surface was last touched.</summary>
        public float WallContactAge => Time.fixedTime - _wallContactTime;
        /// <summary>Normal (planar, pointing away from the wall) of the wall-jump surface last touched.</summary>
        public Vector3 WallNormal => _wallNormal;
        /// <summary>The input this controller reads.</summary>
        public CharacterControls Controls => _input;
        /// <summary>True on the physics step jump was pressed (not held from before).</summary>
        public bool JumpPressed { get; private set; }
        /// <summary>Planar direction the body faces.</summary>
        public Vector3 Facing => _facing;
        /// <summary>Current steering speed along the move direction (m/s), excluding external momentum.</summary>
        public float MoveSpeed => _speed;
        /// <summary>Speed a jump leaves the ground with (m/s).</summary>
        public float JumpSpeed => Mathf.Sqrt(2f * JumpHeight * GravityStrength);
        /// <summary>Current gravity acceleration (m/s²), after volumes' strength scale.</summary>
        public float GravityAcceleration => GravityStrength * (_gravity != null ? _gravity.Scale : 1f);
        /// <summary>The character's box collider (null before a character exists).</summary>
        public BoxCollider BodyCollider => _boxCollider;
        /// <summary>Layers that count as world geometry for probes (everything but characters).</summary>
        public int WorldMask => _stepMask;
        /// <summary>Grip of the ground the character stands on: 1 on normal ground and in the air, less on ice (<see cref="ISurfaceTraction"/>).</summary>
        public float Traction { get; private set; } = 1f;
        /// <summary>Raised when the character touches a surface that knocks it back, with the knockback velocity.</summary>
        public event System.Action<Vector3> KnockedBack;

        private float JumpHeight => PlayerData.localData.jumpHeight.Value;
        /// <summary>Gravity strength in m/s² (PlayerData stores it as a negative number).</summary>
        private float GravityStrength => Mathf.Abs(PlayerData.localData.gravity.Value);

        // ------------------------------------------------------------------ components
        private Rigidbody _body;
        private BoxCollider _boxCollider;
        private Animator _animator;
        private CharacterAnimator _characterAnimator;
        private GroundPhysics _ground;
        private CharacterGravity _gravity;
        private CharacterCollisionRelay _collisionRelay;
        private CharacterControls _input;
        private PlayerData _playerData;
        private int _stepMask;
        private readonly List<IMovementMode> _modes = new();
        private readonly List<int> _modePriorities = new();
        private readonly Collider[] _turnOverlaps = new Collider[8];

        // ------------------------------------------------------------------ runtime state
        private Vector3 _facing = Vector3.forward;        // planar, where the body looks
        private Vector3 _moveDirection = Vector3.forward; // planar, where input last pointed
        private Vector3 _bodyUp = Vector3.up;             // the body's up: the smoothed gravity up
        private Vector3 _frameUp = Vector3.up;            // gravity up used for the previous step
        private Vector3 _kickFacing;                      // direction the body turns to after a wall jump
        private float _kickTurnUntil = float.NegativeInfinity;
        private float _boostSpeed = 1f, _boostAcceleration = 1f;
        private bool _boostMoves;
        private float _turnVelocity;
        private float _speed;

        private bool _wasGrounded;
        private bool _jumpInProgress;
        private bool _jumpHeldLastStep;
        private float _jumpProtectedUntil = float.NegativeInfinity;
        private float _landedAt = float.NegativeInfinity;
        private float _leftGroundAt = float.NegativeInfinity;

        private Vector3 _externalVelocity;
        private float _externalProtectedUntil = float.NegativeInfinity;
        private Collider _surfaceCollider;
        private ISurfaceVelocity _surface;
        private ISurfaceTraction _traction;
        private Vector3 _lastSurfaceVelocity;
        private Vector3 _push, _pendingPush;

        private Vector3 _wallNormal;
        private IWallJumpSurface _wallSurface;
        private float _wallContactTime = float.NegativeInfinity;
        private float _nextWallJump;

        /// <summary>Wall contacts must not cancel upward velocity right after a jump or launch.</summary>
        internal bool IsJumpVelocityProtected =>
            _jumpInProgress || Time.fixedTime < _jumpProtectedUntil || Time.fixedTime < _externalProtectedUntil;

        // ------------------------------------------------------------------ lifecycle
        private void Awake() => _stepMask = ~LayerMask.GetMask(stepIgnoredLayers);

        private void OnEnable() {
            Local = this;
            LocalCharacter.Teleported += OnTeleported;
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
        }

        private void OnDisable() {
            if (Local == this)
                Local = null;
            LocalCharacter.Teleported -= OnTeleported;
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
            BindPlayerData(null);
            ClearExternalMotion();
        }

        private void OnMyPlayerAdded(PlayerData data) => BindPlayerData(data);

        private void BindPlayerData(PlayerData data) {
            if (_playerData != null) {
                _playerData.walkSpeed.OnChange -= OnSpeedChanged;
                _playerData.sprintSpeed.OnChange -= OnSpeedChanged;
            }
            _playerData = data;
            if (_playerData == null)
                return;
            _playerData.walkSpeed.OnChange += OnSpeedChanged;
            _playerData.sprintSpeed.OnChange += OnSpeedChanged;
            ApplyPlayerSpeeds();
        }

        private void OnSpeedChanged(float previous, float next, bool asServer) => ApplyPlayerSpeeds();

        private void ApplyPlayerSpeeds() {
            walkSpeed = _playerData.walkSpeed.Value;
            sprintSpeed = _playerData.sprintSpeed.Value;
        }

        private void OnCharacterAdded(LocalCharacter character) {
            _body = character.GetComponent<Rigidbody>();
            _boxCollider = character.GetComponent<BoxCollider>();
            _animator = character.GetComponent<Animator>();
            _characterAnimator = character.GetComponent<CharacterAnimator>();
            _ground = character.GetComponent<GroundPhysics>();
            _gravity = character.GetComponent<CharacterGravity>();
            if (_gravity == null) {
                Debug.LogError($"{character.name} has no {nameof(CharacterGravity)}; add one to the character prefab. Using default gravity.", character);
                _gravity = character.gameObject.AddComponent<CharacterGravity>();
            }
            _collisionRelay = character.GetComponent<CharacterCollisionRelay>();
            if (_collisionRelay == null)
                _collisionRelay = character.gameObject.AddComponent<CharacterCollisionRelay>();
            _collisionRelay.Initialize(_body, this);
            _input = InputService.characterControls;

            // Orientation is driven entirely by this controller (it follows gravity), so the
            // solver must never spin the body.
            _body.constraints = RigidbodyConstraints.FreezeRotation;
            _body.isKinematic = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.maxDepenetrationVelocity = maxDepenetrationVelocity;

            ClearExternalMotion();
            SnapOrientationToGravity();
        }

        private void OnTeleported(LocalCharacter character) {
            if (LocalPlayer.Character != character)
                return;
            ClearExternalMotion();
            SnapOrientationToGravity();
        }

        // ------------------------------------------------------------------ public API
        /// <summary>Stops the steering speed and any launch or wall-jump momentum, keeping gravity and wall contacts (a wall slide starts from a standstill).</summary>
        public void StopMomentum() {
            _externalVelocity = Vector3.zero;
            _externalProtectedUntil = float.NegativeInfinity;
            _speed = 0f;
        }

        /// <summary>Forgets launch momentum, gravity overrides and wall contacts, e.g. after a respawn.</summary>
        public void ClearExternalMotion() {
            _externalVelocity = Vector3.zero;
            _externalProtectedUntil = float.NegativeInfinity;
            _wallSurface = null;
            _wallContactTime = float.NegativeInfinity;
            _lastSurfaceVelocity = Vector3.zero;
            if (_gravity != null)
                _gravity.Clear();
        }

        /// <summary>
        /// Sets the character's velocity along "up" to that of <paramref name="velocity"/>, and adds its
        /// planar part as momentum that fades over time on top of normal steering.
        /// Used by springs, bounce pads, boost panels and wall jumps.
        /// </summary>
        public void Launch(Vector3 velocity) {
            // A kinematic body (ledge pull-up, ragdoll) is placed by script: there is nothing to launch.
            if (_body == null || _body.isKinematic)
                return;
            Vector3 up = Up;
            Vector3 current = _body.linearVelocity;
            _body.linearVelocity = current - up * Vector3.Dot(current, up) + up * Vector3.Dot(velocity, up);
            _externalVelocity = Vector3.ProjectOnPlane(velocity, up);
            _externalProtectedUntil = Time.fixedTime + launchProtectionTime;
            _jumpProtectedUntil = Time.fixedTime + jumpProtectionTime;
            if (_characterAnimator != null)
                _characterAnimator.Jump();
        }

        /// <summary>
        /// Pushes the character with <paramref name="velocity"/> (its planar part) on the next physics step
        /// only, on top of steering. Call it every step for a steady push, such as wind from a trigger's
        /// OnTriggerStay; pushes from several sources add up. The push stops the moment calls stop.
        /// </summary>
        public void AddPush(Vector3 velocity) => _pendingPush += velocity;

        /// <summary>Gives <paramref name="mode"/> the first look at every physics step (see <see cref="IMovementMode"/>).</summary>
        public void AddMode(IMovementMode mode, int priority = 0) {
            if (mode == null || _modes.Contains(mode))
                return;
            // Higher priorities look first; equal ones keep the order they were added in.
            int index = _modes.Count;
            while (index > 0 && _modePriorities[index - 1] < priority)
                index--;
            _modes.Insert(index, mode);
            _modePriorities.Insert(index, priority);
        }

        public void RemoveMode(IMovementMode mode) {
            int index = _modes.IndexOf(mode);
            if (index < 0)
                return;
            _modes.RemoveAt(index);
            _modePriorities.RemoveAt(index);
        }

        /// <summary>
        /// Boosts this physics step's steering: the target speed is multiplied by <paramref name="speedMultiplier"/> and
        /// speed changes <paramref name="accelerationMultiplier"/> times as fast. With <paramref name="moveWithoutInput"/> the
        /// character keeps moving the way it faces with no stick input. Call it every step from a movement mode; the
        /// boost lapses the moment calls stop. Used by the obby's Dash.
        /// </summary>
        public void SetMoveBoost(float speedMultiplier, float accelerationMultiplier = 1f, bool moveWithoutInput = false) {
            _boostSpeed = Mathf.Max(0f, speedMultiplier);
            _boostAcceleration = Mathf.Max(0f, accelerationMultiplier);
            _boostMoves = moveWithoutInput;
        }

        /// <summary>The camera-relative move direction on the gravity plane, if there is move input.</summary>
        public bool TryGetMoveDirection(out Vector3 direction) {
            Vector2 input = GetAdaptedMoveVector(_input);
            if (_body == null || input.sqrMagnitude <= 0f) {
                direction = _facing;
                return false;
            }
            direction = CameraRelativeDirection(input, Up);
            return true;
        }

        /// <summary>Turns the body to face <paramref name="direction"/> (flattened onto the gravity plane) and carries on at <paramref name="speed"/>.</summary>
        public void Face(Vector3 direction, float speed) {
            _facing = _moveDirection = PlanarOrFallback(direction, Up, _facing);
            _turnVelocity = 0f;
            _speed = Mathf.Max(0f, speed);
        }

        /// <summary>Eases the facing toward <paramref name="direction"/> with the normal turn smoothing.</summary>
        public void SteerToward(Vector3 direction) => TurnToward(PlanarOrFallback(direction, Up, _facing), Up, turnSmoothTime);

        /// <summary>Like <see cref="SteerToward"/> with an explicit turn smoothing time (seconds).</summary>
        public void SteerToward(Vector3 direction, float smoothTime) => TurnToward(PlanarOrFallback(direction, Up, _facing), Up, smoothTime);

        /// <summary>Accelerates the body along gravity for one step, scaled by <paramref name="scale"/>.</summary>
        public void ApplyGravityStep(float dt, float scale = 1f) => ApplyGravity(dt * scale);

        /// <summary>Records contact with a knockback surface. Called by the character's collision relay.</summary>
        internal void ReportKnockback(Vector3 velocity) => KnockedBack?.Invoke(velocity);

        /// <summary>Records contact with a wall-jump surface. Called by the character's collision relay.</summary>
        internal void ReportWallContact(Vector3 normal, IWallJumpSurface surface) {
            normal = Vector3.ProjectOnPlane(normal, Up);
            if (surface == null || normal.sqrMagnitude < 0.0001f)
                return;
            _wallNormal = normal.normalized;
            _wallSurface = surface;
            _wallContactTime = Time.fixedTime;
        }

        /// <summary>The move vector after the player's control settings (e.g. inverted controls).</summary>
        public static Vector2 GetAdaptedMoveVector(CharacterControls input) {
            if (input == null)
                return Vector2.zero;
            Vector2 move = input.move;
            if (GameSettingsClient.GetSettingValue<bool>("InvertedMovementControls"))
                move *= -1;
            return move;
        }

        // ------------------------------------------------------------------ simulation
        private bool CanSimulate =>
            _body != null && _animator != null && _animator.enabled && _input
            && InstanceFinder.ClientManager.Connection.LoadedStartScenes(false)
            && !ClientConnector.IsLoadingScenes;

        private void FixedUpdate() {
            if (!CanSimulate)
                return;
            float dt = Time.fixedDeltaTime;
            // Triggers report after the physics step, so pushes gathered then act on this step.
            _push = _pendingPush;
            _pendingPush = Vector3.zero;
            _boostSpeed = _boostAcceleration = 1f;
            _boostMoves = false;
            _gravity.Resolve();
            UpdateGravityFrame();
            UpdateGroundState();
            JumpPressed = _input.jump && !_jumpHeldLastStep;
            for (int i = 0; i < _modes.Count; i++) {
                if (!_modes[i].Step(this, dt))
                    continue;
                ApplyBodyRotation();
                _jumpHeldLastStep = _input.jump;
                return;
            }
            HandleJump();
            ApplyGravity(dt);
            Move(dt);
            ApplyBodyRotation();
            _jumpHeldLastStep = _input.jump;
        }

        /// <summary>
        /// Follows the smoothed gravity up (see <see cref="CharacterGravity"/>). Every step it turns, the planar
        /// directions stay perpendicular to it and the velocity is carried across the turn
        /// (<see cref="GravityBlend.Reproject"/>), so running continues over the corner onto a wall or ceiling
        /// instead of the old speed ploughing into the new surface.
        /// </summary>
        private void UpdateGravityFrame() {
            // Flipping upside down rolls about the facing direction, so the character keeps facing (and
            // the camera keeps looking) the way it was going instead of turning around.
            Vector3 up = Up;
            if (up != _frameUp) {
                Quaternion transport = CharacterGravity.TurnToward(_frameUp, up, _facing);
                Vector3 previousFacing = _facing;
                _facing = PlanarOrFallback(transport * _facing, up, previousFacing);
                _moveDirection = PlanarOrFallback(transport * _moveDirection, up, _facing);
                _kickFacing = PlanarOrFallback(transport * _kickFacing, up, _facing);
                _externalVelocity = transport * _externalVelocity;
                _body.linearVelocity = GravityBlend.Reproject(_body.linearVelocity, _frameUp, up, previousFacing);
                _frameUp = up;
            }
            _bodyUp = up;
        }

        private void SnapOrientationToGravity() {
            if (_body == null)
                return;
            Vector3 up = Up;
            _frameUp = _bodyUp = up;
            _kickTurnUntil = float.NegativeInfinity;
            _facing = PlanarOrFallback(_body.rotation * Vector3.forward, up, Vector3.forward);
            _moveDirection = _facing;
            _turnVelocity = 0f;
            _body.rotation = BodyRotation();
        }

        /// <summary>
        /// Rotates the body toward <see cref="BodyRotation"/>. While it turns over to a new gravity
        /// direction it pivots about <see cref="turnPivotHeight"/> instead of the feet, and is pushed out of
        /// anything the turning collider sinks into; turning about up (facing) leaves the pivot in place, so
        /// ordinary steering is unaffected.
        /// </summary>
        private void ApplyBodyRotation() {
            Quaternion current = _body.rotation;
            Quaternion target = BodyRotation();
            Vector3 pivot = TurnPivotLocal();
            Vector3 position = _body.position + (current * pivot - target * pivot);
            if (turnDepenetrationPasses > 0 && Vector3.Angle(current * Vector3.up, target * Vector3.up) > 0.25f)
                position = ResolveTurnOverlap(position, target);
            if ((position - _body.position).sqrMagnitude > 0.000001f)
                _body.position = position;
            _body.MoveRotation(target);
        }

        /// <summary>
        /// Moves <paramref name="position"/> out of whatever the body's box would overlap if it stood there
        /// with <paramref name="rotation"/>, so a turn never leaves a corner buried in the floor to be
        /// shoved out (and jitter) by the solver.
        /// </summary>
        private Vector3 ResolveTurnOverlap(Vector3 position, Quaternion rotation) {
            if (_boxCollider == null)
                return position;
            Vector3 scale = _boxCollider.transform.lossyScale;
            Vector3 absScale = new(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, absScale);
            PhysicsScene world = _body.gameObject.scene.GetPhysicsScene();
            for (int pass = 0; pass < turnDepenetrationPasses; pass++) {
                Vector3 centre = position + rotation * Vector3.Scale(_boxCollider.center, scale);
                int count = world.OverlapBox(centre, halfExtents, _turnOverlaps, rotation, _stepMask, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++) {
                    Collider other = _turnOverlaps[i];
                    if (other == _boxCollider || other.attachedRigidbody == _body)
                        continue;
                    if (!Physics.ComputePenetration(_boxCollider, position, rotation, other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance) || distance <= 0f)
                        continue;
                    position += direction * distance;
                    moved = true;
                }
                if (!moved)
                    break;
            }
            return position;
        }

        /// <summary>The turn pivot relative to the body origin, in the body's (unscaled) rotation frame.</summary>
        private Vector3 TurnPivotLocal() {
            if (_boxCollider == null || turnPivotHeight <= 0f)
                return Vector3.zero;
            float scale = _body.transform.lossyScale.y;
            float feet = (_boxCollider.center.y - _boxCollider.size.y * 0.5f) * scale;
            return Vector3.up * (feet + _boxCollider.size.y * scale * turnPivotHeight);
        }

        private Quaternion BodyRotation() {
            Vector3 forward = PlanarOrFallback(_facing, _bodyUp, _body.rotation * Vector3.forward);
            return Quaternion.LookRotation(forward, _bodyUp);
        }

        private void UpdateGroundState() {
            bool grounded = _ground.Grounded;
            if (grounded && !_wasGrounded)
                _landedAt = Time.fixedTime;
            else if (!grounded && _wasGrounded)
                _leftGroundAt = Time.fixedTime;
            if (!grounded)
                _jumpInProgress = false;
            _wasGrounded = grounded;
        }

        private void HandleJump() {
            if (!_ground.Grounded) {
                TryWallJump();
                if (Time.fixedTime - _leftGroundAt > coyoteTime || Time.fixedTime < _jumpProtectedUntil)
                    return;
            }
            if (!_input.jump || _jumpInProgress || Time.fixedTime - _landedAt < landingJumpDelay)
                return;
            if (!StaminaController.ConsumeStamina(jumpStaminaCost))
                return;

            _jumpInProgress = true;
            _jumpProtectedUntil = Time.fixedTime + jumpProtectionTime;
            _leftGroundAt = float.NegativeInfinity; // one coyote jump per ledge
            Vector3 up = Up;
            Vector3 velocity = _body.linearVelocity;
            // Jump speed uses unscaled gravity, so low-gravity volumes give higher, floatier jumps.
            float jumpSpeed = Mathf.Sqrt(2f * JumpHeight * GravityStrength);
            _body.linearVelocity = velocity - up * Vector3.Dot(velocity, up) + up * jumpSpeed;
            _characterAnimator.Jump();
        }

        /// <summary>
        /// Kicks off the wall-jump surface touched in the last <see cref="wallContactGrace"/> seconds if jump
        /// was pressed this step. Returns whether it jumped. Normal movement calls it on its own while airborne;
        /// a wall-slide mode calls it while clinging.
        /// </summary>
        public bool TryWallJump() {
            if (!JumpPressed || _wallSurface == null || Time.fixedTime < _nextWallJump
                || Time.fixedTime - _wallContactTime > wallContactGrace)
                return false;
            _nextWallJump = Time.fixedTime + wallJumpCooldown;
            Vector3 normal = _wallNormal;
            Launch(normal * _wallSurface.AwaySpeed + Up * _wallSurface.UpSpeed);
            // Turn away from the wall quickly so the kick reads clearly and steering starts from there.
            _moveDirection = _kickFacing = normal;
            _kickTurnUntil = Time.fixedTime + wallJumpTurnSeconds;
            _turnVelocity = 0f;
            _wallSurface = null;
            WallJumped?.Invoke(normal);
            return true;
        }

        private void ApplyGravity(float dt) {
            Vector3 down = _gravity.Down;
            float fallSpeed = Vector3.Dot(_body.linearVelocity, down);
            if (fallSpeed >= terminalFallSpeed)
                return;
            float delta = Mathf.Min(GravityStrength * _gravity.Scale * dt, terminalFallSpeed - fallSpeed);
            _body.linearVelocity += down * delta;
        }

        private void Move(float dt) {
            Vector3 up = Up;
            Vector2 input = GetAdaptedMoveVector(_input);
            bool hasInput = input.sqrMagnitude > 0f;
            bool kickTurning = Time.fixedTime < _kickTurnUntil;
            bool sprinting = SprintEnabled && _input.sprint && hasInput && sprintSpeed > 0f
                && StaminaController.ConsumeStamina(sprintStaminaPerSecond * dt);
            float targetSpeed = !hasInput && !_boostMoves ? 0f : sprinting ? sprintSpeed : walkSpeed;
            targetSpeed *= _boostSpeed;
            float inputMagnitude = !_input.analogMovement || (_boostMoves && !hasInput) ? 1f : input.magnitude;
            _speed = Mathf.Lerp(_speed, targetSpeed, dt * acceleration * _boostAcceleration);
            if (_speed < 0.01f)
                _speed = 0f;

            if (hasInput) {
                _moveDirection = CameraRelativeDirection(input, up);
                // Just after a wall jump the body turns away from the wall whatever the stick says.
                TurnToward(kickTurning ? _kickFacing : _moveDirection, up, kickTurning ? wallJumpTurnSmoothTime : turnSmoothTime);
            } else if (kickTurning) {
                TurnToward(_kickFacing, up, wallJumpTurnSmoothTime);
            } else if (_boostMoves) {
                _moveDirection = _facing;
            } else if (_ground.GroundRotationDelta != Quaternion.identity) {
                _facing = PlanarOrFallback(_ground.GroundRotationDelta * _facing, up, _facing);
            }

            Vector3 move = _moveDirection * (_speed * inputMagnitude);
            TryStepUp(_moveDirection, move.magnitude, up);
            // Carry the dynamic body through physics so its render pose can interpolate
            // alongside the platform, rather than teleporting it in GroundPhysics.
            move += _ground.GroundVelocity;
            // Conveyors push a standing character; stepping off keeps that push as momentum.
            Vector3 surfaceVelocity = SurfaceVelocity(up);
            if (surfaceVelocity == Vector3.zero && _lastSurfaceVelocity != Vector3.zero && !_ground.Grounded)
                _externalVelocity += _lastSurfaceVelocity;
            _lastSurfaceVelocity = surfaceVelocity;
            DampExternalVelocity(dt);
            move += surfaceVelocity + _externalVelocity + _push;

            // Steer only the planar velocity; gravity and jumps own the part along up.
            Vector3 velocityChange = Vector3.ProjectOnPlane(move - _body.linearVelocity, up);
            // Slippery ground only lets the velocity change slowly, so the character slides.
            if (Traction < 1f)
                velocityChange = Vector3.ClampMagnitude(velocityChange, Traction * slipAcceleration * dt);
            _body.AddForce(velocityChange, ForceMode.VelocityChange);
            _body.angularVelocity = Vector3.zero;
        }

        /// <summary>Input mapped onto the gravity plane as seen from the camera.</summary>
        private Vector3 CameraRelativeDirection(Vector2 input, Vector3 up) {
            Transform view = Camera.main != null ? Camera.main.transform : _body.transform;
            // Looking straight along gravity, the camera's own up is the best "forward".
            Vector3 forward = PlanarOrFallback(view.forward, up, Vector3.ProjectOnPlane(view.up, up));
            // While the camera rolls over to follow flipped gravity its forward swings sideways, but its
            // right stays perpendicular to the way it looks. Heading from the right keeps "forward"
            // straight ahead through the roll; the forward above only decides which way that is.
            Vector3 level = Vector3.ProjectOnPlane(view.right, up);
            if (level.sqrMagnitude > 0.01f) {
                level = Vector3.Cross(level.normalized, up);
                forward = Vector3.Dot(level, forward) >= 0f ? level : -level;
            }
            Vector3 right = Vector3.Cross(up, forward);
            return PlanarOrFallback(right * input.x + forward * input.y, up, _facing);
        }

        private void TurnToward(Vector3 direction, Vector3 up, float smoothTime) {
            float remaining = Vector3.SignedAngle(_facing, direction, up);
            float step = Mathf.SmoothDampAngle(0f, remaining, ref _turnVelocity, smoothTime);
            _facing = PlanarOrFallback(Quaternion.AngleAxis(step, up) * _facing, up, direction);
        }

        private Vector3 SurfaceVelocity(Vector3 up) {
            Collider ground = _ground.Grounded ? _ground.GroundCollider : null;
            if (ground != _surfaceCollider) {
                _surfaceCollider = ground;
                _surface = ground != null ? ground.GetComponentInParent<ISurfaceVelocity>() : null;
                _traction = ground != null ? ground.GetComponentInParent<ISurfaceTraction>() : null;
            }
            Traction = _traction == null || (_traction is Behaviour grip && !grip.isActiveAndEnabled)
                ? 1f : Mathf.Clamp01(_traction.Traction);
            if (_surface == null || (_surface is Behaviour behaviour && !behaviour.isActiveAndEnabled))
                return Vector3.zero;
            return Vector3.ProjectOnPlane(_surface.GetSurfaceVelocity(_body.position), up);
        }

        private void DampExternalVelocity(float dt) {
            if (Time.fixedTime < _externalProtectedUntil || _externalVelocity == Vector3.zero)
                return;
            float damping = _ground.Grounded ? externalGroundDamping : externalAirDamping;
            _externalVelocity *= Mathf.Exp(-damping * dt);
            if (_externalVelocity.sqrMagnitude < 0.0025f)
                _externalVelocity = Vector3.zero;
        }

        /// <summary>Lifts the body onto a low ledge directly ahead, measured along <paramref name="up"/>.</summary>
        private void TryStepUp(Vector3 direction, float speed, Vector3 up) {
            if (_boxCollider == null || !_ground.Grounded || speed <= 0f || maxStepHeight <= 0f)
                return;
            direction = Vector3.ProjectOnPlane(direction, up);
            if (direction.sqrMagnitude < 0.001f)
                return;
            direction.Normalize();

            Transform body = _boxCollider.transform;
            Vector3 scale = body.lossyScale;
            Vector3 fullHalfExtents = Vector3.Scale(_boxCollider.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            // The cast box is the collider shrunk by the skin, so it never starts out touching what it measures.
            Vector3 halfExtents = new Vector3(
                Mathf.Max(0.01f, fullHalfExtents.x - collisionSkin),
                Mathf.Max(0.01f, fullHalfExtents.y - collisionSkin),
                Mathf.Max(0.01f, fullHalfExtents.z - collisionSkin));

            Quaternion orientation = body.rotation;
            Vector3 colliderCenter = body.TransformPoint(_boxCollider.center);
            Vector3 center = colliderCenter + up * collisionSkin;
            float distance = speed * Time.fixedDeltaTime + stepProbeDistance;
            PhysicsScene physics = _boxCollider.gameObject.scene.GetPhysicsScene();

            // Blocked at foot height, but clear one step higher: a ledge worth stepping onto.
            if (!physics.BoxCast(center, halfExtents, direction, out _, orientation, distance, _stepMask, QueryTriggerInteraction.Ignore))
                return;
            Vector3 raisedCenter = center + up * (maxStepHeight + collisionSkin);
            if (physics.BoxCast(raisedCenter, halfExtents, direction, out _, orientation, distance, _stepMask, QueryTriggerInteraction.Ignore))
                return;

            Vector3 right = body.right, forward = body.forward, bodyUp = body.up;
            float forwardExtent = Mathf.Abs(Vector3.Dot(direction, right)) * fullHalfExtents.x
                + Mathf.Abs(Vector3.Dot(direction, bodyUp)) * fullHalfExtents.y
                + Mathf.Abs(Vector3.Dot(direction, forward)) * fullHalfExtents.z;
            float heightExtent = Mathf.Abs(Vector3.Dot(up, right)) * fullHalfExtents.x
                + Mathf.Abs(Vector3.Dot(up, bodyUp)) * fullHalfExtents.y
                + Mathf.Abs(Vector3.Dot(up, forward)) * fullHalfExtents.z;
            // The collider's true underside: the lift is measured from here, so the body clears the ledge
            // instead of ending a skin's width inside it and being shoved back off.
            Vector3 feet = colliderCenter - up * heightExtent;
            Vector3 landingProbe = feet + direction * (forwardExtent + distance) + up * (maxStepHeight + collisionSkin);

            if (!physics.Raycast(landingProbe, -up, out RaycastHit landing, maxStepHeight + collisionSkin * 2f, _stepMask, QueryTriggerInteraction.Ignore)
                || Vector3.Dot(landing.normal, up) < minimumStepUpDot)
                return;

            float stepHeight = Vector3.Dot(landing.point - feet, up);
            if (stepHeight <= MinimumStepHeight || stepHeight > maxStepHeight)
                return;
            _body.MovePosition(_body.position + up * (stepHeight + collisionSkin));
        }

        /// <summary><paramref name="vector"/> flattened onto the plane of <paramref name="up"/> and normalised, or <paramref name="fallback"/> treated the same way when it is (nearly) parallel to up.</summary>
        private static Vector3 PlanarOrFallback(Vector3 vector, Vector3 up, Vector3 fallback) {
            Vector3 planar = Vector3.ProjectOnPlane(vector, up);
            if (planar.sqrMagnitude > 0.0001f)
                return planar.normalized;
            planar = Vector3.ProjectOnPlane(fallback, up);
            if (planar.sqrMagnitude > 0.0001f)
                return planar.normalized;
            // Last resort: any direction perpendicular to up.
            return Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        }
    }
}
