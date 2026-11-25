using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Characters.Client {
    /// <summary>What a <see cref="CharacterDash"/> can do right now.</summary>
    public enum DashState { Off, Ready, Dashing, Recharging }

    /// <summary>
    /// An opt-in, Sonic-style dash for the local player's character that replaces sprinting: one press of the
    /// sprint key starts a short burst (about a second) at a multiple of walking speed, with a deep forward
    /// lean and a fast leg cycle shown to every client through <see cref="Stance.Dash"/>.
    /// <para>
    /// A dash spends most of a small energy pool. The pool refills only slowly, only while standing on solid
    /// ground (not ice, wind or in the air) and only after a short pause, so dashes are a skill tool and cannot
    /// be chained into a free sprint. A dash can only be started on the ground; it carries on through a jump
    /// but cannot be restarted in the air.
    /// </para>
    /// <para>
    /// Opt in by adding the component to the game's client presentation and ticking <see cref="Enabled"/> (or
    /// calling <see cref="SetEnabled"/>); with it off (the default) the sprint key keeps working exactly as before,
    /// so no other game changes. It is a movement mode that never takes over a step: it only boosts the walking
    /// steering of <see cref="CharacterMovement"/> (<see cref="CharacterMovement.SetMoveBoost"/>). The movement is
    /// client-authoritative like everything else here; the speed it reaches is at most
    /// <see cref="TopSpeedFor"/> of the walking speed.
    /// </para>
    /// </summary>
    public sealed class CharacterDash : MonoBehaviour, IMovementMode {
        /// <summary>The dash of the local player's character, if the component is active.</summary>
        public static CharacterDash Local { get; private set; }

        [Header("Mode")]
        [Tooltip("Turns sprint into this dash. Off leaves the sprint key alone.")]
        [SerializeField] private bool dashEnabled;

        [Header("Burst")]
        [Tooltip("Seconds one dash lasts.")]
        [SerializeField, Min(0.1f)] private float burstSeconds = 1f;
        [Tooltip("Top speed of a dash as a multiple of the walking speed.")]
        [SerializeField, Min(1f)] private float speedMultiplier = 1.9f;
        [Tooltip("How much faster than normal the speed builds up when a dash starts, so the burst is felt at once.")]
        [SerializeField, Min(1f)] private float accelerationMultiplier = 4f;

        [Header("Energy")]
        [Tooltip("Size of the energy pool. A full pool is one dash plus a little.")]
        [SerializeField, Min(1f)] private float poolSize = 100f;
        [Tooltip("Energy one full burst costs (it drains evenly over the burst). A dash can only start with at least this much.")]
        [SerializeField, Min(1f)] private float burstCost = 80f;
        [Tooltip("Energy gained per second while recharging.")]
        [SerializeField, Min(0f)] private float refillPerSecond = 14f;
        [Tooltip("Seconds after a dash ends before the pool starts to refill.")]
        [SerializeField, Min(0f)] private float refillDelay = 1.2f;
        [Tooltip("Grip below this (ice) counts as not solid ground: no dash starts and nothing refills there.")]
        [SerializeField, Range(0f, 1f)] private float solidGroundTraction = 0.99f;

        /// <summary>Raised on the physics step a dash starts.</summary>
        public event System.Action Started;
        /// <summary>Raised when a dash ends (it ran its time, ran dry, or was interrupted).</summary>
        public event System.Action Ended;
        /// <summary>Raised when the dash key is pressed but a dash cannot start (no energy, in the air, on ice, in wind).</summary>
        public event System.Action Denied;

        /// <summary>Whether sprint is turned into the dash.</summary>
        public bool Enabled => dashEnabled;
        public bool IsDashing { get; private set; }
        /// <summary>Energy left in the pool.</summary>
        public float Charge { get; private set; }
        public float PoolSize => poolSize;
        /// <summary>Energy a dash needs to start.</summary>
        public float BurstCost => burstCost;
        /// <summary>Energy as a fraction of the pool (0..1).</summary>
        public float Charge01 => poolSize > 0f ? Mathf.Clamp01(Charge / poolSize) : 0f;
        /// <summary>The energy a dash needs, as a fraction of the pool (where a meter can mark "ready").</summary>
        public float StartThreshold01 => poolSize > 0f ? Mathf.Clamp01(burstCost / poolSize) : 0f;
        /// <summary>Fraction of the running burst that is left (1 at the start, 0 at the end; 0 when not dashing).</summary>
        public float BurstRemaining01 => IsDashing ? Mathf.Clamp01(1f - (Time.fixedTime - _startedAt) / burstSeconds) : 0f;
        /// <summary>Seconds until the pool starts refilling (0 once it has).</summary>
        public float RefillDelayRemaining => IsDashing ? refillDelay : Mathf.Max(0f, _refillAt - Time.fixedTime);
        /// <summary>The state a HUD meter shows.</summary>
        public DashState State => !dashEnabled ? DashState.Off : IsDashing ? DashState.Dashing : Charge >= burstCost ? DashState.Ready : DashState.Recharging;

        /// <summary>The fastest ground speed a dash reaches for a character that walks at <paramref name="walkSpeed"/> (m/s).</summary>
        public float TopSpeedFor(float walkSpeed) => walkSpeed * speedMultiplier;

        /// <summary>Turns the dash on or off (a game does this for its local character when it starts or ends).</summary>
        public void SetEnabled(bool value) {
            if (dashEnabled == value)
                return;
            dashEnabled = value;
            if (!value)
                EndDash();
            ApplySprintSetting();
        }

        /// <summary>Refills the pool and cancels a running dash (a respawn or a new round).</summary>
        public void ResetEnergy() {
            EndDash();
            Charge = poolSize;
            _refillAt = float.NegativeInfinity;
        }

        private CharacterMovement _movement;
        private CharacterStance _stance;
        private LocalCharacter _character;
        private float _startedAt, _refillAt = float.NegativeInfinity;
        private bool _sprintHeld, _pressQueued;

        // ------------------------------------------------------------------ lifecycle
        private void OnEnable() {
            Local = this;
            Charge = poolSize;
            LocalCharacter.Teleported += OnTeleported;
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }

        private void OnDisable() {
            if (Local == this)
                Local = null;
            LocalCharacter.Teleported -= OnTeleported;
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            EndDash();
            Bind(null);
        }

        private void OnValidate() => burstCost = Mathf.Min(burstCost, poolSize);

        private void OnCharacterAdded(LocalCharacter character) {
            _character = character;
            _stance = character != null ? character.GetComponent<CharacterStance>() : null;
            ResetEnergy();
        }

        private void OnTeleported(LocalCharacter character) {
            if (character == _character)
                ResetEnergy();
        }

        private void Bind(CharacterMovement movement) {
            if (_movement == movement)
                return;
            if (_movement != null) {
                _movement.SprintEnabled = true;
                _movement.RemoveMode(this);
            }
            _movement = movement;
            if (_movement != null) {
                _movement.AddMode(this);
                ApplySprintSetting();
            }
        }

        private void ApplySprintSetting() {
            if (_movement != null)
                _movement.SprintEnabled = !dashEnabled;
        }

        private void Update() {
            Bind(CharacterMovement.Local);
            if (_movement == null || _movement.Controls == null)
                return;
            bool sprint = _movement.Controls.sprint;
            if (sprint && !_sprintHeld)
                _pressQueued = true;
            _sprintHeld = sprint;
        }

        // ------------------------------------------------------------------ simulation
        private void FixedUpdate() {
            if (!dashEnabled || _movement == null || _movement.Body == null || _stance == null)
                return;
            float dt = Time.fixedDeltaTime;
            bool pressed = _pressQueued;
            _pressQueued = false;
            if (IsDashing) {
                Charge = Mathf.Max(0f, Charge - burstCost / burstSeconds * dt);
                bool interrupted = _stance.Current != Stance.Dash || _movement.Body.isKinematic;
                if (interrupted || Charge <= 0f || Time.fixedTime - _startedAt >= burstSeconds)
                    EndDash();
                return;
            }
            if (pressed && !TryStart())
                Denied?.Invoke();
            if (!IsDashing && _movement.Grounded && _movement.Traction >= solidGroundTraction && Time.fixedTime >= _refillAt)
                Charge = Mathf.Min(poolSize, Charge + refillPerSecond * dt);
        }

        private bool TryStart() {
            if (Charge < burstCost || !_movement.Grounded || _movement.Traction < solidGroundTraction
                || _movement.IsPushed || _movement.Body.isKinematic || _stance.Current != Stance.Normal)
                return false;
            IsDashing = true;
            _startedAt = Time.fixedTime;
            _stance.Set(Stance.Dash);
            Started?.Invoke();
            return true;
        }

        private void EndDash() {
            if (!IsDashing)
                return;
            IsDashing = false;
            _refillAt = Time.fixedTime + refillDelay;
            if (_stance != null && _stance.Current == Stance.Dash)
                _stance.Set(Stance.Normal);
            Ended?.Invoke();
        }

        // ------------------------------------------------------------------ IMovementMode
        /// <summary>Boosts the walking steering while a dash runs. Never takes over the step.</summary>
        public bool Step(CharacterMovement movement, float dt) {
            if (movement != _movement)
                return false;
            movement.SprintEnabled = !dashEnabled;
            if (dashEnabled && IsDashing)
                movement.SetMoveBoost(speedMultiplier, accelerationMultiplier, true);
            return false;
        }
    }
}
