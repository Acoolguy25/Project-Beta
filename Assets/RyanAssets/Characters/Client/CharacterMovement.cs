using FishNet;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;
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
        public float JumpHeight = 1.2f;
        public float Gravity = -15.0f;

        [Space(10)]
        public float JumpTimeout = 0.35f;
        public float LandJumpTimeout = 0.05f;
        public float FallTimeout = 0.05f;


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
        private BoxCollider boxCollider;
        private CharacterAnimator characterAnimator;
        private bool LastGrounded;
        // private MovementControl _movementControl;


        private bool _hasAnimator;

        //private bool IsCurrentDeviceMouse => _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
        void OnEnable() {
            LocalPlayer.Instance.OnCharacterAdded.Subscribe(OnCharacterAdded);
            //PlayerData.localData.walkSpeed.Subscribe(Refresh);
            //if (SharedGlobalEvents.Instance && SharedGlobalEvents.Instance.Players.TryGetValue(InstanceFinder.ClientManager.Connection, out ServerPlayerStats serverPlayerStats))
            //   Refresh(serverPlayerStats);
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
        }
        void OnDisable() {
            LocalPlayer.Instance.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
            //SharedGlobalEvents.OnMyPlayerUpdated -= Refresh;
            //PlayerData.localData.walkSpeed.Unsubscribe(Refresh);
        }
        void OnMyPlayerAdded(PlayerData data) {
            data.walkSpeed.OnChange += (_, _, _) => Refresh();
            data.sprintSpeed.OnChange += (_, _, _) => Refresh();
        }
        private void Refresh() {
            MoveSpeed = PlayerData.localData.walkSpeed.Value;
            SprintSpeed = PlayerData.localData.sprintSpeed.Value;
        }
        private void OnCharacterAdded(Transform c) {
            _hasAnimator = LocalPlayer.Character.TryGetComponent(out _animator);
            _rb = LocalPlayer.Character.GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotation & ~RigidbodyConstraints.FreezeRotationY;
            _input = InputService.characterControls;
            boxCollider = LocalPlayer.Character.GetComponent<BoxCollider>();
            characterAnimator = LocalPlayer.Character.GetComponent<CharacterAnimator>();

            //_playerInput = GetComponent<PlayerInput>();
            // _movementControl = GetComponent<MovementControl>();

            //AssignAnimationIDs();
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }
        private void FixedUpdate() {
            if (_animator == null || !_animator.enabled) return;
            if (!_input) return;

            JumpAndGravity();
            Move();
        }

        //private void AssignAnimationIDs() {
        //    _animIDSpeed = Animator.StringToHash("Speed");
        //    _animIDGrounded = Animator.StringToHash("Grounded");
        //    _animIDJump = Animator.StringToHash("Jump");
        //    _animIDFreeFall = Animator.StringToHash("FreeFall");
        //    _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        //}
        private Vector2 GetAdaptedMoveVector(){
            Vector2 move = _input.move;
            if (GameSettingsClient.GetSettingValue<bool>("InvertedMovementControls"))
                move *= -1;
            return move;
        }
        private void Move() {
            //if (LocalPlayer.Character.ConsumeStamina(previousSpeed.magnitude > 3f && _input.sprint)) {

            //}
            Vector2 moveVec = GetAdaptedMoveVector();
            Vector2 lastMoveVec = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z);

            float targetSpeed = (_input.sprint && moveVec.magnitude > 0f && lastMoveVec.magnitude > SprintSpeed/4f && LocalPlayer.Character.ConsumeStamina(SprintStaminaConsumptionRate * Time.fixedDeltaTime)) ? SprintSpeed : MoveSpeed;
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
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            Vector3 move = targetDirection.normalized * (_animationBlend * inputMagnitude);
            //if (!Grounded)
            move.y = _rb.linearVelocity.y;
            _rb.linearVelocity = move;
            _rb.angularVelocity = Vector3.zero;

            // Animation updates
            //if (_hasAnimator) {
            //    _animator.SetFloat(_animIDSpeed, _animationBlend); // smoothed
            //    _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            //}
        }
        //private float wasJumping = 0f;
        private void JumpAndGravity() {
            _jumpTimeoutDelta -= Time.fixedDeltaTime;
            if (characterAnimator.Grounded) {
                if (LastGrounded)
                    _landTimeoutDelta -= Time.fixedDeltaTime;
                else
                    _landTimeoutDelta = LandJumpTimeout;
                _fallTimeoutDelta = FallTimeout;

                //if (_hasAnimator) {
                //    _animator.SetBool(_animIDJump, false);
                //    _animator.SetBool(_animIDFreeFall, false);
                //}

                if (_input.jump && _jumpTimeoutDelta <= 0.0f && _landTimeoutDelta <= 0.0f && LocalPlayer.Character.ConsumeStamina(5f)) {
                    _jumpTimeoutDelta = JumpTimeout;

                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    //Vector3 velocity = _rb.linearVelocity;
                    //velocity.y = _verticalVelocity;
                    //_rb.linearVelocity = velocity;
                    _rb.AddRelativeForce(Vector3.up * _verticalVelocity, ForceMode.VelocityChange);
                    characterAnimator.Jump();
                    //if (_hasAnimator)
                    //    _animator.SetBool(_animIDJump, true);
                    //wasJumping = Time.fixedTime;
                }
            } else {
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
                _rb.linearVelocity += new Vector3(0f, Gravity * Time.fixedDeltaTime, 0f);
            }
            LastGrounded = characterAnimator.Grounded;
        }
    }
}
