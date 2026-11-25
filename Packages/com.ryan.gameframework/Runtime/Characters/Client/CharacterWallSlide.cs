using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Characters.Client {
    /// <summary>
    /// Wall slide and wall-jump presentation for the local player's character, layered on
    /// <see cref="CharacterMovement"/> as a movement mode. Add it to a universe's client presentation to enable it there.
    /// <list type="bullet">
    /// <item><b>Slide:</b> falling against a wall-jump surface (<see cref="IWallJumpSurface"/>) the character clings to it:
    /// it turns to face the wall, its fall slows to a controlled slide, and the shared <see cref="Stance.WallSlide"/>
    /// pose (hands on the wall, knees bent) shows on every client. Walking away from the wall, landing or losing
    /// contact lets go.</item>
    /// <item><b>Jump:</b> pressing jump while clinging (or just after touching the wall) kicks off it. The body turns
    /// away from the wall to face the way it travels (<see cref="CharacterMovement"/>) and the shared
    /// <see cref="Stance.WallKick"/> pose plays: a crouch into the wall, a spring and a somersault.</item>
    /// </list>
    /// Everything is measured in the character's gravity frame, so it also works on walls of a flipped world.
    /// Other games are unaffected: without a wall-jump surface nothing here ever starts.
    /// </summary>
    public sealed class CharacterWallSlide : MonoBehaviour, IMovementMode {
        /// <summary>Raised when the character starts clinging to a wall (the wall's normal, pointing away from it).</summary>
        public event System.Action<Vector3> SlideStarted;
        /// <summary>Raised when the character lets go of the wall (by any means).</summary>
        public event System.Action SlideEnded;

        [Header("Slide")]
        [Tooltip("Fastest the character slides down a wall (m/s).")]
        [SerializeField, Min(0f)] private float slideSpeed = 4f;
        [Tooltip("How quickly a faster fall is slowed to the slide speed (m/s²).")]
        [SerializeField, Min(0f)] private float slideBraking = 45f;
        [Tooltip("Speed pressed into the wall to stay in contact (m/s).")]
        [SerializeField, Min(0f)] private float stickSpeed = 1.5f;
        [Tooltip("A fall starts a slide only while rising slower than this (m/s), so the climb of a wall jump is not caught.")]
        [SerializeField, Min(0f)] private float maxRiseToStart = 1.5f;
        [Tooltip("Seconds the body takes to turn to face the wall.")]
        [SerializeField, Range(0.01f, 0.3f)] private float faceSmoothTime = 0.06f;
        [Tooltip("Moving this directly away from the wall (dot of the input and the wall normal) lets go.")]
        [SerializeField, Range(0.1f, 1f)] private float releaseInput = 0.6f;

        [Header("Wall jump")]
        [Tooltip("Seconds after a wall jump before the character can cling again, so it kicks clear of the wall it left.")]
        [SerializeField, Min(0f)] private float reattachDelay = 0.3f;
        [Tooltip("Seconds the kick pose lasts (matches the pose length on CharacterStance).")]
        [SerializeField, Min(0.1f)] private float kickPoseSeconds = 0.55f;

        /// <summary>True while the local character clings to a wall.</summary>
        public bool Sliding { get; private set; }

        private CharacterMovement _movement;
        private CharacterStance _stance;
        private LocalCharacter _character;
        private float _reattachAt = float.NegativeInfinity;
        private float _kickUntil = float.NegativeInfinity;

        // ------------------------------------------------------------------ lifecycle
        private void OnEnable() {
            LocalCharacter.Teleported += OnTeleported;
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }

        private void OnDisable() {
            LocalCharacter.Teleported -= OnTeleported;
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            EndSlide();
            EndKick();
            Bind(null);
        }

        private void OnCharacterAdded(LocalCharacter character) {
            EndSlide();
            _character = character;
            _stance = character != null ? character.GetComponent<CharacterStance>() : null;
        }

        private void OnTeleported(LocalCharacter character) {
            if (character != _character)
                return;
            EndSlide();
            EndKick();
        }

        private void Bind(CharacterMovement movement) {
            if (_movement == movement)
                return;
            if (_movement != null) {
                _movement.RemoveMode(this);
                _movement.WallJumped -= OnWallJumped;
            }
            _movement = movement;
            if (_movement != null) {
                // After the parkour moves: a ledge to grab or a wall to mantle wins over clinging.
                _movement.AddMode(this, -10);
                _movement.WallJumped += OnWallJumped;
            }
        }

        private void Update() {
            Bind(CharacterMovement.Local);
            if (_kickUntil > float.NegativeInfinity && (Time.time >= _kickUntil || (_movement != null && _movement.Grounded)))
                EndKick();
        }

        // ------------------------------------------------------------------ IMovementMode
        public bool Step(CharacterMovement movement, float dt) {
            if (movement != _movement || _stance == null || movement.Body == null)
                return false;
            if (Sliding) {
                // Something else (a ledge grab, a ragdoll) changed the stance: the cling is over.
                if (_stance.Current != Stance.WallSlide || movement.Grounded || !movement.HasWallContact || WantsToLetGo(movement)) {
                    EndSlide();
                    return false;
                }
                if (movement.JumpPressed && movement.TryWallJump()) {
                    // WallJumped has already ended the slide and started the kick pose.
                    return false;
                }
                Cling(movement, dt);
                return true;
            }
            return TryStart(movement);
        }

        private bool TryStart(CharacterMovement movement) {
            if (movement.Grounded || Time.fixedTime < _reattachAt || !movement.HasWallContact)
                return false;
            if (_stance.Current != Stance.Normal && _stance.Current != Stance.WallKick)
                return false;
            Vector3 up = movement.Up;
            if (Vector3.Dot(movement.Body.linearVelocity, up) > maxRiseToStart || WantsToLetGo(movement))
                return false;
            if (_stance.Current == Stance.WallKick)
                EndKick();
            Sliding = true;
            movement.StopMomentum();
            _stance.Set(Stance.WallSlide);
            SlideStarted?.Invoke(movement.WallNormal);
            Cling(movement, Time.fixedDeltaTime);
            return true;
        }

        /// <summary>True while the stick points (nearly) straight away from the wall.</summary>
        private bool WantsToLetGo(CharacterMovement movement) =>
            movement.TryGetMoveDirection(out Vector3 input) && Vector3.Dot(input, movement.WallNormal) > releaseInput;

        /// <summary>Faces the wall, presses lightly into it and lets the fall settle at the slide speed.</summary>
        private void Cling(CharacterMovement movement, float dt) {
            Vector3 up = movement.Up, normal = movement.WallNormal;
            movement.SteerToward(-normal, faceSmoothTime);
            float along = Vector3.Dot(movement.Body.linearVelocity, up) - movement.GravityAcceleration * dt;
            if (along < -slideSpeed)
                along = Mathf.MoveTowards(along, -slideSpeed, slideBraking * dt);
            movement.Body.linearVelocity = -normal * stickSpeed + up * along;
        }

        private void EndSlide() {
            if (!Sliding)
                return;
            Sliding = false;
            if (_stance != null && _stance.Current == Stance.WallSlide)
                _stance.Set(Stance.Normal);
            SlideEnded?.Invoke();
        }

        private void OnWallJumped(Vector3 normal) {
            bool wasSliding = Sliding;
            Sliding = false;
            _reattachAt = Time.fixedTime + reattachDelay;
            if (_stance == null)
                return;
            _stance.Set(Stance.WallKick);
            _kickUntil = Time.time + kickPoseSeconds;
            if (wasSliding)
                SlideEnded?.Invoke();
        }

        private void EndKick() {
            _kickUntil = float.NegativeInfinity;
            if (_stance != null && _stance.Current == Stance.WallKick)
                _stance.Set(Stance.Normal);
        }
    }
}
