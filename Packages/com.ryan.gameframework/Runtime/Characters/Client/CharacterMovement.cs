using FishNet;
using FishNet.Managing.Scened;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientCore;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.HID;

namespace RyanAssets.Characters.Client {
    public class CharacterMovement : MonoBehaviour {
        [Header("Player")]
        public float MoveSpeed = 2.0f;
        public float SprintSpeed = 5.335f;
        public float SprintStaminaConsumptionRate = 10.0f;
        [Range(0.0f, 0.3f)] public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 10.0f;

        [Space(10)]
        public float JumpTimeout = 0.35f;
        public float LandJumpTimeout = 0.05f;
        public float FallTimeout = 0.05f;

        [Header("Collision Movement")]
        [Min(0f)] public float MaxStepHeight = 0.6f;
        [Min(0.01f)] public float StepProbeDistance = 0.12f;
        [Range(0.01f, 0.1f)] public float CollisionSkin = 0.03f;
        [Range(0f, 1f)] public float MinimumStepGroundNormal = 0.65f;
        [Min(0f)] public float MaxDepenetrationVelocity = 5f;

        [Header("External Motion")]
        [Tooltip("How quickly launch/wall-jump momentum fades while standing on the ground (per second).")]
        [Min(0f)] public float ExternalGroundDamping = 6f;
        [Tooltip("How quickly launch/wall-jump momentum fades in the air (per second).")]
        [Min(0f)] public float ExternalAirDamping = 0.6f;
        [Tooltip("Seconds after touching a wall-jump surface that a jump press still kicks off it.")]
        [Min(0f)] public float WallContactGrace = 0.15f;
        [Min(0f)] public float WallJumpCooldown = 0.2f;

        public float JumpHeight => PlayerData.localData.jumpHeight.Value;
        public float Gravity => PlayerData.localData.gravity.Value;

        /// <summary>The movement controller driving the local player's character, if any.</summary>
        public static CharacterMovement Local { get; private set; }
        public bool Grounded => groundPhysics != null && groundPhysics.Grounded;
        public Rigidbody Body => _rb;
        /// <summary>Multiplier on gravity from the strongest active gravity source (1 when none).</summary>
        public float GravityScale { get; private set; } = 1f;
        /// <summary>Horizontal momentum from launches, wall jumps and conveyors that fades over time.</summary>
        public Vector3 ExternalVelocity => _externalVelocity;

        private const float LaunchProtection = 0.2f;
        private Vector3 _externalVelocity;
        private float _externalProtectedUntil = float.NegativeInfinity;
        private readonly Dictionary<object, (float scale, float expires)> _gravitySources = new();
        private readonly List<object> _expiredGravitySources = new();
        private Vector3 _wallNormal;
        private IWallJumpSurface _wallSurface;
        private float _wallContactTime = float.NegativeInfinity;
        private float _nextWallJump;
        private bool _jumpHeldLastStep;
        private Collider _surfaceCollider;
        private ISurfaceVelocity _surface;
        private Vector3 _lastSurfaceVelocity;

        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta, _landTimeoutDelta;
        private float _fallTimeoutDelta;


        private Animator _animator;
        private Rigidbody _rb;
        private CharacterControls _input;
        private BoxCollider _boxCollider;
        private CharacterAnimator characterAnimator;
        private GroundPhysics groundPhysics;
        private CharacterCollisionRelay _collisionRelay;
        private bool LastGrounded;
        private bool _jumpInProgress;
        private bool _sceneLoading;
        // private MovementControl _movementControl;


        private bool _hasAnimator;

        //private bool IsCurrentDeviceMouse => _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
        void OnEnable() {
            Local = this;
            LocalCharacter.Teleported += OnTeleported;
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            //PlayerData.localData.walkSpeed.Subscribe(Refresh);
            //if (SharedGlobalEvents.Instance && SharedGlobalEvents.Instance.Players.TryGetValue(InstanceFinder.ClientManager.Connection, out ServerPlayerStats serverPlayerStats))
            //   Refresh(serverPlayerStats);
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
        }
        void OnDisable() {
            if (Local == this)
                Local = null;
            LocalCharacter.Teleported -= OnTeleported;
            ClearExternalMotion();
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
        }
        
        void OnMyPlayerAdded(PlayerData data) {
            data.walkSpeed.OnChange += (_, _, _) => Refresh();
            data.sprintSpeed.OnChange += (_, _, _) => Refresh();
            Refresh();
        }
        private void Refresh() {
            MoveSpeed = PlayerData.localData.walkSpeed.Value;
            SprintSpeed = PlayerData.localData.sprintSpeed.Value;
        }
        private void OnCharacterAdded(LocalCharacter c) {
            _hasAnimator = LocalPlayer.Character.TryGetComponent(out _animator);
            _rb = LocalPlayer.Character.GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.isKinematic = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.maxDepenetrationVelocity = MaxDepenetrationVelocity;
            _input = InputService.characterControls;
            _boxCollider = LocalPlayer.Character.GetComponent<BoxCollider>();
            characterAnimator = LocalPlayer.Character.GetComponent<CharacterAnimator>();
            groundPhysics = LocalPlayer.Character.GetComponent<GroundPhysics>();
            _collisionRelay = LocalPlayer.Character.GetComponent<CharacterCollisionRelay>();
            if (_collisionRelay == null)
                _collisionRelay = LocalPlayer.Character.gameObject.AddComponent<CharacterCollisionRelay>();
            _collisionRelay.Initialize(_rb, this);

            //_playerInput = GetComponent<PlayerInput>();
            // _movementControl = GetComponent<MovementControl>();

            //AssignAnimationIDs();
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
            ClearExternalMotion();
        }

        private void OnTeleported(LocalCharacter character) {
            if (LocalPlayer.Character == character)
                ClearExternalMotion();
        }

        /// <summary>Forgets launch momentum, gravity sources and wall contacts, e.g. after a respawn.</summary>
        public void ClearExternalMotion() {
            _externalVelocity = Vector3.zero;
            _externalProtectedUntil = float.NegativeInfinity;
            _gravitySources.Clear();
            GravityScale = 1f;
            _wallSurface = null;
            _wallContactTime = float.NegativeInfinity;
            _lastSurfaceVelocity = Vector3.zero;
        }

        /// <summary>
        /// Sets the character's velocity to <paramref name="velocity"/>: its vertical part directly,
        /// and its horizontal part as momentum that fades over time on top of normal steering.
        /// Used by springs, bounce pads and boost panels.
        /// </summary>
        public void Launch(Vector3 velocity) {
            if (_rb == null)
                return;
            Vector3 current = _rb.linearVelocity;
            current.y = velocity.y;
            _rb.linearVelocity = current;
            _externalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            _externalProtectedUntil = Time.fixedTime + LaunchProtection;
            _jumpTimeoutDelta = JumpTimeout;
            if (characterAnimator != null)
                characterAnimator.Jump();
        }

        /// <summary>
        /// Applies a gravity multiplier from <paramref name="source"/> for <paramref name="duration"/>
        /// seconds. Volumes refresh it every physics step they still contain the character, so leaving
        /// one (or being teleported out) restores normal gravity without an exit callback.
        /// The source furthest from 1 wins when several overlap.
        /// </summary>
        public void SetGravityScale(object source, float scale, float duration = 0.1f) {
            if (source == null)
                return;
            _gravitySources[source] = (Mathf.Max(0f, scale), Time.fixedTime + Mathf.Max(Time.fixedDeltaTime, duration));
        }

        /// <summary>Records contact with a wall-jump surface. Called by the character's collision relay.</summary>
        internal void ReportWallContact(Vector3 normal, IWallJumpSurface surface) {
            normal.y = 0f;
            if (surface == null || normal.sqrMagnitude < 0.0001f)
                return;
            _wallNormal = normal.normalized;
            _wallSurface = surface;
            _wallContactTime = Time.fixedTime;
        }

        private void UpdateGravityScale() {
            float scale = 1f;
            foreach (var pair in _gravitySources) {
                if (pair.Value.expires < Time.fixedTime) {
                    _expiredGravitySources.Add(pair.Key);
                    continue;
                }
                if (Mathf.Abs(pair.Value.scale - 1f) > Mathf.Abs(scale - 1f))
                    scale = pair.Value.scale;
            }
            foreach (object expired in _expiredGravitySources)
                _gravitySources.Remove(expired);
            _expiredGravitySources.Clear();
            GravityScale = scale;
        }

        private Vector3 SurfaceVelocity() {
            Collider ground = groundPhysics != null && groundPhysics.Grounded ? groundPhysics.GroundCollider : null;
            if (ground != _surfaceCollider) {
                _surfaceCollider = ground;
                _surface = ground != null ? ground.GetComponentInParent<ISurfaceVelocity>() : null;
            }
            if (_surface == null || (_surface is Behaviour behaviour && !behaviour.isActiveAndEnabled))
                return Vector3.zero;
            Vector3 velocity = _surface.GetSurfaceVelocity(_rb.position);
            velocity.y = 0f;
            return velocity;
        }

        private void DampExternalVelocity() {
            if (Time.fixedTime < _externalProtectedUntil || _externalVelocity == Vector3.zero)
                return;
            float damping = groundPhysics.Grounded ? ExternalGroundDamping : ExternalAirDamping;
            _externalVelocity *= Mathf.Exp(-damping * Time.fixedDeltaTime);
            if (_externalVelocity.sqrMagnitude < 0.0025f)
                _externalVelocity = Vector3.zero;
        }

        private void FixedUpdate() {
            if (_animator == null || !_animator.enabled) return;
            if (!_input) return;
            if (!InstanceFinder.ClientManager.Connection.LoadedStartScenes(false) || ClientConnector.IsLoadingScenes) return;

            UpdateGravityScale();
            JumpAndGravity();
            Move();
            _jumpHeldLastStep = _input.jump;
        }

        //private void AssignAnimationIDs() {
        //    _animIDSpeed = Animator.StringToHash("Speed");
        //    _animIDGrounded = Animator.StringToHash("Grounded");
        //    _animIDJump = Animator.StringToHash("Jump");
        //    _animIDFreeFall = Animator.StringToHash("FreeFall");
        //    _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        //}
        public static Vector2 GetAdaptedMoveVector(CharacterControls input) {
            if (input == null)
                return Vector2.zero;

            Vector2 move = input.move;
            if (GameSettingsClient.GetSettingValue<bool>("InvertedMovementControls"))
                move *= -1;
            return move;
        }
        private void Move() {
            Vector2 moveVec = GetAdaptedMoveVector(_input);
            bool canSprint = _input.sprint
                && moveVec.sqrMagnitude > 0f
                && SprintSpeed > 0f
                && StaminaController.ConsumeStamina(SprintStaminaConsumptionRate * Time.fixedDeltaTime);
            float targetSpeed = canSprint ? SprintSpeed : MoveSpeed;
            if (moveVec == Vector2.zero) targetSpeed = 0.0f;

            float inputMagnitude = _input.analogMovement ? moveVec.magnitude : 1f;

            // Smooth animation blend
            float currentSpeed = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z).magnitude;
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.fixedDeltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // Movement direction
            Vector3 inputDirection = new Vector3(moveVec.x, 0.0f, moveVec.y).normalized;

            // Rotate player to face movement direction
            if (moveVec != Vector2.zero) {
                float CamEulerAngleY = Camera.main.transform.eulerAngles.y;
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + CamEulerAngleY;
                float rotation = Mathf.SmoothDampAngle(_rb.rotation.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                _rb.MoveRotation(Quaternion.Euler(0.0f, rotation, 0.0f));
            } else if (groundPhysics.GroundRotationDelta != Quaternion.identity) {
                _rb.MoveRotation(groundPhysics.GroundRotationDelta * _rb.rotation);
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            Vector3 move = targetDirection.normalized * (_animationBlend * inputMagnitude);
            TryStepUp(targetDirection, move.magnitude, groundPhysics.Grounded);
            // Carry the dynamic body through physics so its render pose can interpolate
            // alongside the platform, rather than teleporting it in GroundPhysics.
            move += groundPhysics.GroundVelocity;
            // Conveyors push a standing character; stepping off keeps that push as momentum.
            Vector3 surfaceVelocity = SurfaceVelocity();
            if (surfaceVelocity == Vector3.zero && _lastSurfaceVelocity != Vector3.zero && !groundPhysics.Grounded)
                _externalVelocity += _lastSurfaceVelocity;
            _lastSurfaceVelocity = surfaceVelocity;
            DampExternalVelocity();
            move += surfaceVelocity + _externalVelocity;
            //if (!Grounded)
            Vector3 velocityChange = move - _rb.linearVelocity;
            velocityChange.y = 0f;

            _rb.AddForce(velocityChange, ForceMode.VelocityChange);
            _rb.angularVelocity = Vector3.zero;

            // Animation updates
            //if (_hasAnimator) {
            //    _animator.SetFloat(_animIDSpeed, _animationBlend); // smoothed
            //    _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            //}
        }

        private void TryStepUp(Vector3 direction, float speed, bool grounded) {
            if (_boxCollider == null || !grounded || speed <= 0f || MaxStepHeight <= 0f)
                return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return;
            direction.Normalize();

            Transform characterTransform = _boxCollider.transform;
            Vector3 scale = characterTransform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size * 0.5f, scale);
            halfExtents = new Vector3(
                Mathf.Max(0.01f, halfExtents.x - CollisionSkin),
                Mathf.Max(0.01f, halfExtents.y - CollisionSkin),
                Mathf.Max(0.01f, halfExtents.z - CollisionSkin)
            );

            Vector3 center = characterTransform.TransformPoint(_boxCollider.center) + Vector3.up * CollisionSkin;
            Quaternion orientation = characterTransform.rotation;
            float distance = speed * Time.fixedDeltaTime + StepProbeDistance;
            int collisionMask = ~LayerMask.GetMask("Character", "LocalCharacter");
            PhysicsScene physicsScene = _boxCollider.gameObject.scene.GetPhysicsScene();

            if (!physicsScene.BoxCast(center, halfExtents, direction, out _, orientation, distance, collisionMask, QueryTriggerInteraction.Ignore))
                return;

            Vector3 raisedCenter = center + Vector3.up * (MaxStepHeight + CollisionSkin);
            if (physicsScene.BoxCast(raisedCenter, halfExtents, direction, out _, orientation, distance, collisionMask, QueryTriggerInteraction.Ignore))
                return;

            float forwardExtent = Mathf.Abs(direction.x) * halfExtents.x + Mathf.Abs(direction.z) * halfExtents.z;
            Vector3 feet = center - Vector3.up * halfExtents.y;
            Vector3 landingProbe = feet
                + direction * (forwardExtent + distance)
                + Vector3.up * (MaxStepHeight + CollisionSkin);

            if (!physicsScene.Raycast(
                    landingProbe,
                    Vector3.down,
                    out RaycastHit landing,
                    MaxStepHeight + CollisionSkin * 2f,
                    collisionMask,
                    QueryTriggerInteraction.Ignore)
                || landing.normal.y < MinimumStepGroundNormal)
                return;

            float stepHeight = landing.point.y - feet.y;
            if (stepHeight <= CollisionSkin || stepHeight > MaxStepHeight)
                return;

            _rb.MovePosition(_rb.position + Vector3.up * (stepHeight + CollisionSkin));
        }

        internal bool IsJumpVelocityProtected => _jumpInProgress || _jumpTimeoutDelta > 0f || Time.fixedTime < _externalProtectedUntil;
        private void TryWallJump() {
            bool jumpPressed = _input.jump && !_jumpHeldLastStep;
            if (!jumpPressed || _wallSurface == null || Time.fixedTime < _nextWallJump
                || Time.fixedTime - _wallContactTime > WallContactGrace)
                return;
            _nextWallJump = Time.fixedTime + WallJumpCooldown;
            Vector3 away = _wallNormal * _wallSurface.AwaySpeed;
            Launch(new Vector3(away.x, _wallSurface.UpSpeed, away.z));
            // Face away from the wall so the kick reads clearly and steering starts from there.
            _targetRotation = Mathf.Atan2(_wallNormal.x, _wallNormal.z) * Mathf.Rad2Deg;
            _rb.MoveRotation(Quaternion.Euler(0f, _targetRotation, 0f));
            _wallSurface = null;
        }
        //private float wasJumping = 0f;
        private void JumpAndGravity() {
            _jumpTimeoutDelta -= Time.fixedDeltaTime;
            if (groundPhysics.Grounded) {
                if (LastGrounded)
                    _landTimeoutDelta -= Time.fixedDeltaTime;
                else
                    _landTimeoutDelta = LandJumpTimeout;
                _fallTimeoutDelta = FallTimeout;

                //if (_hasAnimator) {
                //    _animator.SetBool(_animIDJump, false);
                //    _animator.SetBool(_animIDFreeFall, false);
                //}

                if (_input.jump && !_jumpInProgress && StaminaController.ConsumeStamina(5f)) {
                    _jumpInProgress = true;
                    _jumpTimeoutDelta = JumpTimeout;

                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    Vector3 velocity = _rb.linearVelocity;
                    velocity.y = _verticalVelocity;
                    _rb.linearVelocity = velocity;
                    characterAnimator.Jump();
                    //if (_hasAnimator)
                    //    _animator.SetBool(_animIDJump, true);
                    //wasJumping = Time.fixedTime;
                }
            } else {
                _jumpInProgress = false;
                TryWallJump();
                if (_fallTimeoutDelta >= 0.0f) {
                    _fallTimeoutDelta -= Time.fixedDeltaTime;
                } else {
                    //if (_hasAnimator) {
                    //    _animator.SetBool(_animIDFreeFall, true);
                    //    _animator.SetBool(_animIDJump, false);
                    //}
                }
            }

            if (_rb.linearVelocity.y < _terminalVelocity) {
                _rb.linearVelocity += new Vector3(0f, Gravity * GravityScale * Time.fixedDeltaTime, 0f);
            }
            LastGrounded = groundPhysics.Grounded;
        }
    }
}
