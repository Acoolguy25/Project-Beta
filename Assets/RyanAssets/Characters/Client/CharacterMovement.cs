using FishNet;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Core;
using RyanAssets.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.HID;

namespace RyanAssets.Characters.Client {
    public class CharacterMovement : MonoBehaviour {
        [Header("Player")]
        public float MoveSpeed = 2.0f;
        public float SprintSpeed = 5.335f;
        [Range(0.0f, 0.3f)] public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 10.0f;

        [Space(10)]
        public float JumpHeight = 1.2f;
        public float Gravity = -15.0f;

        [Space(10)]
        public float JumpTimeout = 0.35f;
        public float LandJumpTimeout = 0.15f;
        public float FallTimeout = 0.20f;

        [Header("Player Grounded")]
        public bool LastGrounded, Grounded;
        static private LayerMask GroundMask;

        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta, _landTimeoutDelta;
        private float _fallTimeoutDelta;

        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

        private Animator _animator;
        private Rigidbody _rb;
        private CharacterControls _input;
        private BoxCollider boxCollider;
        // private MovementControl _movementControl;


        private bool _hasAnimator;

        //private bool IsCurrentDeviceMouse => _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
        void OnEnable() {
            LocalPlayer.Instance.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }
        void OnDisable() {
            LocalPlayer.Instance.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
        }
        private void OnCharacterAdded(Transform c) {
            _hasAnimator = LocalPlayer.Character.TryGetComponent(out _animator);
            _rb = LocalPlayer.Character.GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotation & ~RigidbodyConstraints.FreezeRotationY;
            _input = InputService.characterControls;
            boxCollider = LocalPlayer.Character.GetComponent<BoxCollider>();

            //_playerInput = GetComponent<PlayerInput>();
            // _movementControl = GetComponent<MovementControl>();
            GroundMask = ~LayerMask.GetMask("Character");

            AssignAnimationIDs();
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }
        private void FixedUpdate() {
            if (_animator == null || !_animator.enabled) return;
            if (!_input) return;

            GroundedCheck();
            JumpAndGravity();
            Move();
        }

        private void AssignAnimationIDs() {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }
        private void GroundedCheck() {
            Bounds b = boxCollider.bounds;
            float upOff = 0.03f;

            Grounded = Physics.BoxCast(
                b.center + Vector3.down * (b.extents.y - upOff),   // same origin shift you used
                new Vector3(b.extents.x, 0.0f, b.extents.z), // thin "bottom slice"
                Vector3.down,
                out RaycastHit _,
                boxCollider.transform.rotation,
                0.085f,
                GroundMask,
                QueryTriggerInteraction.Ignore
            );

#if UNITY_EDITOR
            // Four bottom corners of the box for debug purposes
            Vector3[] origins = new Vector3[]{
                new Vector3(b.min.x, b.min.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z),
            };
            foreach (Vector3 origin in origins) {
                Vector3 targetOrigin = origin + Vector3.up * upOff;
                bool hit = Physics.Raycast(
                    targetOrigin,
                    Vector3.down,
                    out RaycastHit rayHit,
                    0.085f,
                    GroundMask,
                    QueryTriggerInteraction.Ignore
                );

                // DEBUG RAY
                Debug.DrawRay(
                    targetOrigin,
                    Vector3.down * 0.05f,
                    hit ? Color.green : Color.red
                );
            }
#endif

            if (_hasAnimator)
                _animator.SetBool(_animIDGrounded, Grounded);
        }
        private Vector2 GetAdaptedMoveVector(){
            Vector2 move = _input.move;
            if (GameSettingsClient.GetSettingValue<bool>("InvertedMovementControls"))
                move *= -1;
            return move;
        }
        private void Move() {
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;
            Vector2 moveVec = GetAdaptedMoveVector();

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
                float rotation = Mathf.SmoothDampAngle(LocalPlayer.Character.transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                LocalPlayer.Character.transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            } else
                _rb.angularVelocity = Vector3.zero;

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            Vector3 move = targetDirection.normalized * (_animationBlend * inputMagnitude);
            //if (!Grounded)
            move.y = _rb.linearVelocity.y;
            _rb.linearVelocity = move;

            // Animation updates
            if (_hasAnimator) {
                _animator.SetFloat(_animIDSpeed, _animationBlend); // smoothed
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }
        //private float wasJumping = 0f;
        private void JumpAndGravity() {
            _jumpTimeoutDelta -= Time.fixedDeltaTime;
            if (Grounded) {
                if (LastGrounded)
                    _landTimeoutDelta -= Time.fixedDeltaTime;
                else
                    _landTimeoutDelta = LandJumpTimeout;
                _fallTimeoutDelta = FallTimeout;

                if (_hasAnimator) {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                if (_input.jump && _jumpTimeoutDelta <= 0.0f && _landTimeoutDelta <= 0.0f) {
                    _jumpTimeoutDelta = JumpTimeout;

                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    Vector3 velocity = _rb.linearVelocity;
                    velocity.y = _verticalVelocity;
                    _rb.linearVelocity = velocity;

                    if (_hasAnimator)
                        _animator.SetBool(_animIDJump, true);
                    //wasJumping = Time.fixedTime;
                }
            } else {
                if (_fallTimeoutDelta >= 0.0f) {
                    _fallTimeoutDelta -= Time.fixedDeltaTime;
                } else {
                    if (_hasAnimator) {
                        _animator.SetBool(_animIDFreeFall, true);
                        _animator.SetBool(_animIDJump, false);
                    }
                }
            }

            if (_rb.linearVelocity.y < _terminalVelocity) {
                _rb.linearVelocity += new Vector3(0f, Gravity * Time.fixedDeltaTime, 0f);
            }
            LastGrounded = Grounded;
        }
    }
}