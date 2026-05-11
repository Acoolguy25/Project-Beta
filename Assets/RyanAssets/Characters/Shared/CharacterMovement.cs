using UnityEngine;
using UnityEngine.InputSystem;
using FishNet;

namespace RyanAssets.Characters
{
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
        public float JumpTimeout = 0.50f;
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        public bool Grounded = true;

        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        
        private Animator _animator;
        private Rigidbody _rb;
        private SharedInputController _input;
        // private MovementControl _movementControl;


        private bool _hasAnimator;

        //private bool IsCurrentDeviceMouse => _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
        public void OnEnable() {
            _hasAnimator = LocalPlayer.Character.TryGetComponent(out _animator);
            _rb = LocalPlayer.Character.GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotation & ~RigidbodyConstraints.FreezeRotationY;
            _input = SharedInputController.Instance;
            //_playerInput = GetComponent<PlayerInput>();
            // _movementControl = GetComponent<MovementControl>();

            AssignAnimationIDs();
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }
        private void FixedUpdate() {
            if (_animator != null && !_animator.enabled) return;
            if (!_input) return;

            JumpAndGravity();
            GroundedCheck();
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
            Grounded = true; //_movementControl.IsGrounded;
            if (_hasAnimator) {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void Move() {
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // Smooth animation blend
            float currentSpeed = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z).magnitude;
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.fixedDeltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // Movement direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // Rotate player to face movement direction
            if (_input.move != Vector2.zero) {
                float CamEulerAngleY = CameraController.Instance.activeCamera.transform.eulerAngles.y;
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + CamEulerAngleY;
                float rotation = Mathf.SmoothDampAngle(LocalPlayer.Character.transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                LocalPlayer.Character.transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            Vector3 move = targetDirection.normalized * (_animationBlend * inputMagnitude);
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
            if (Grounded) {
                _fallTimeoutDelta = FallTimeout;

                if (_hasAnimator) {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                if (_verticalVelocity < 0.0f)
                    _verticalVelocity = -2f;

                if (_input.jump && _jumpTimeoutDelta <= 0.0f) {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    Vector3 velocity = _rb.linearVelocity;
                    velocity.y = _verticalVelocity;
                    _rb.linearVelocity = velocity;

                    if (_hasAnimator)
                        _animator.SetBool(_animIDJump, true);
                    //wasJumping = Time.fixedTime;
                }

                if (_jumpTimeoutDelta >= 0.0f)
                    _jumpTimeoutDelta -= Time.fixedDeltaTime;
            }
            else {
                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0.0f) {
                    _fallTimeoutDelta -= Time.fixedDeltaTime;
                }
                else {
                    if (_hasAnimator) {
                        _animator.SetBool(_animIDFreeFall, true);
                        _animator.SetBool(_animIDJump, false);
                    }
                }
            }

            if (_rb.linearVelocity.y < _terminalVelocity) {
                _rb.linearVelocity += new Vector3(0f, Gravity * Time.fixedDeltaTime, 0f);
            }
        }
    }
}