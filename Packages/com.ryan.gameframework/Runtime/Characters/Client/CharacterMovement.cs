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
    /// Client-authoritative movement for the local player's character.
    /// <para>
    /// Every vector is measured in the character's gravity frame (<see cref="CharacterGravity"/>):
    /// "up" is wherever gravity is not, "planar" means perpendicular to up. With default gravity this
    /// is ordinary ground movement; inside a gravity volume the same code walks, jumps and steps on
    /// walls and ceilings, and the body turns smoothly to stand on the new surface.
    /// </para>
    /// <para>Each physics step runs: resolve gravity → orient → jump → gravity → planar movement.</para>
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
        [Tooltip("How fast the body turns to stand on a new gravity direction (degrees per second).")]
        [SerializeField, Min(1f)] private float upAlignSpeed = 540f;
        [Tooltip("Height up the body it turns about when gravity changes direction (0 = feet, 0.5 = middle). " +
                 "Turning about the middle keeps the head from sweeping through the floor when gravity flips upside down.")]
        [SerializeField, Range(0f, 1f)] private float turnPivotHeight = 0.5f;

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

        // ------------------------------------------------------------------ runtime state
        private Vector3 _facing = Vector3.forward;        // planar, where the body looks
        private Vector3 _moveDirection = Vector3.forward; // planar, where input last pointed
        private Vector3 _bodyUp = Vector3.up;             // the body's up, easing toward gravity up
        private Vector3 _frameUp = Vector3.up;            // gravity up used for the previous step
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
        private Vector3 _lastSurfaceVelocity;

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
            if (_body == null)
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
            _gravity.Resolve();
            UpdateGravityFrame(dt);
            UpdateGroundState();
            HandleJump();
            ApplyGravity(dt);
            Move(dt);
            ApplyBodyRotation();
            _jumpHeldLastStep = _input.jump;
        }

        /// <summary>
        /// Keeps the planar directions perpendicular to gravity when it changes, and eases the body's
        /// up toward gravity's up so a switch to a wall or ceiling reads as the character turning over.
        /// </summary>
        private void UpdateGravityFrame(float dt) {
            // Flipping upside down rolls about the facing direction, so the character keeps facing (and
            // the camera keeps looking) the way it was going instead of turning around.
            Vector3 up = Up;
            if (up != _frameUp) {
                Quaternion transport = CharacterGravity.TurnToward(_frameUp, up, _facing);
                Vector3 previousFacing = _facing;
                _facing = PlanarOrFallback(transport * _facing, up, previousFacing);
                _moveDirection = PlanarOrFallback(transport * _moveDirection, up, _facing);
                _frameUp = up;
            }

            if (Vector3.Angle(_bodyUp, up) < 0.01f) {
                _bodyUp = up;
                return;
            }
            _bodyUp = CharacterGravity.TurnToward(_bodyUp, up, _facing, upAlignSpeed * dt) * _bodyUp;
        }

        private void SnapOrientationToGravity() {
            if (_body == null)
                return;
            Vector3 up = Up;
            _frameUp = _bodyUp = up;
            _facing = PlanarOrFallback(_body.rotation * Vector3.forward, up, Vector3.forward);
            _moveDirection = _facing;
            _turnVelocity = 0f;
            _body.rotation = BodyRotation();
        }

        /// <summary>
        /// Rotates the body toward <see cref="BodyRotation"/>. While it turns over to a new gravity
        /// direction it pivots about <see cref="turnPivotHeight"/> instead of the feet; turning about
        /// up (facing) leaves the pivot in place, so ordinary steering is unaffected.
        /// </summary>
        private void ApplyBodyRotation() {
            Quaternion current = _body.rotation;
            Quaternion target = BodyRotation();
            Vector3 pivot = TurnPivotLocal();
            Vector3 shift = current * pivot - target * pivot;
            if (shift.sqrMagnitude > 0.000001f)
                _body.position += shift;
            _body.MoveRotation(target);
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

        private void TryWallJump() {
            bool jumpPressed = _input.jump && !_jumpHeldLastStep;
            if (!jumpPressed || _wallSurface == null || Time.fixedTime < _nextWallJump
                || Time.fixedTime - _wallContactTime > wallContactGrace)
                return;
            _nextWallJump = Time.fixedTime + wallJumpCooldown;
            Launch(_wallNormal * _wallSurface.AwaySpeed + Up * _wallSurface.UpSpeed);
            // Face away from the wall so the kick reads clearly and steering starts from there.
            _facing = _moveDirection = _wallNormal;
            _turnVelocity = 0f;
            _wallSurface = null;
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
            bool sprinting = _input.sprint && hasInput && sprintSpeed > 0f
                && StaminaController.ConsumeStamina(sprintStaminaPerSecond * dt);
            float targetSpeed = !hasInput ? 0f : sprinting ? sprintSpeed : walkSpeed;
            float inputMagnitude = _input.analogMovement ? input.magnitude : 1f;
            _speed = Mathf.Lerp(_speed, targetSpeed, dt * acceleration);
            if (_speed < 0.01f)
                _speed = 0f;

            if (hasInput) {
                _moveDirection = CameraRelativeDirection(input, up);
                TurnToward(_moveDirection, up);
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
            move += surfaceVelocity + _externalVelocity;

            // Steer only the planar velocity; gravity and jumps own the part along up.
            Vector3 velocityChange = Vector3.ProjectOnPlane(move - _body.linearVelocity, up);
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

        private void TurnToward(Vector3 direction, Vector3 up) {
            float remaining = Vector3.SignedAngle(_facing, direction, up);
            float step = Mathf.SmoothDampAngle(0f, remaining, ref _turnVelocity, turnSmoothTime);
            _facing = PlanarOrFallback(Quaternion.AngleAxis(step, up) * _facing, up, direction);
        }

        private Vector3 SurfaceVelocity(Vector3 up) {
            Collider ground = _ground.Grounded ? _ground.GroundCollider : null;
            if (ground != _surfaceCollider) {
                _surfaceCollider = ground;
                _surface = ground != null ? ground.GetComponentInParent<ISurfaceVelocity>() : null;
            }
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
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            halfExtents = new Vector3(
                Mathf.Max(0.01f, halfExtents.x - collisionSkin),
                Mathf.Max(0.01f, halfExtents.y - collisionSkin),
                Mathf.Max(0.01f, halfExtents.z - collisionSkin));

            Quaternion orientation = body.rotation;
            Vector3 center = body.TransformPoint(_boxCollider.center) + up * collisionSkin;
            float distance = speed * Time.fixedDeltaTime + stepProbeDistance;
            PhysicsScene physics = _boxCollider.gameObject.scene.GetPhysicsScene();

            // Blocked at foot height, but clear one step higher: a ledge worth stepping onto.
            if (!physics.BoxCast(center, halfExtents, direction, out _, orientation, distance, _stepMask, QueryTriggerInteraction.Ignore))
                return;
            Vector3 raisedCenter = center + up * (maxStepHeight + collisionSkin);
            if (physics.BoxCast(raisedCenter, halfExtents, direction, out _, orientation, distance, _stepMask, QueryTriggerInteraction.Ignore))
                return;

            Vector3 right = body.right, forward = body.forward, bodyUp = body.up;
            float forwardExtent = Mathf.Abs(Vector3.Dot(direction, right)) * halfExtents.x
                + Mathf.Abs(Vector3.Dot(direction, bodyUp)) * halfExtents.y
                + Mathf.Abs(Vector3.Dot(direction, forward)) * halfExtents.z;
            float heightExtent = Mathf.Abs(Vector3.Dot(up, right)) * halfExtents.x
                + Mathf.Abs(Vector3.Dot(up, bodyUp)) * halfExtents.y
                + Mathf.Abs(Vector3.Dot(up, forward)) * halfExtents.z;
            Vector3 feet = center - up * heightExtent;
            Vector3 landingProbe = feet + direction * (forwardExtent + distance) + up * (maxStepHeight + collisionSkin);

            if (!physics.Raycast(landingProbe, -up, out RaycastHit landing, maxStepHeight + collisionSkin * 2f, _stepMask, QueryTriggerInteraction.Ignore)
                || Vector3.Dot(landing.normal, up) < minimumStepUpDot)
                return;

            float stepHeight = Vector3.Dot(landing.point - feet, up);
            if (stepHeight <= collisionSkin || stepHeight > maxStepHeight)
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
