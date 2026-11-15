using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Characters.Client {
    /// <summary>
    /// Parkour and physics for the local player's character, layered on <see cref="CharacterMovement"/>
    /// as a movement mode. Add it to a universe's client presentation to enable it there.
    /// <list type="bullet">
    /// <item><b>Ledge grab:</b> falling past a ledge within reach catches it. Hanging, move sideways to
    /// shimmy, Jump to climb up (or Jump while holding away to kick off), Crouch to let go.</item>
    /// <item><b>Mantle:</b> run into chest-high geometry in the air, or press Jump against it, to pull
    /// up onto it.</item>
    /// <item><b>Slide:</b> Crouch while running slides low and fast under obstacles; Jump out of a slide
    /// for a long jump.</item>
    /// <item><b>Wall run:</b> sprint and jump alongside a wall to run along it; Jump to kick off.</item>
    /// <item><b>Roll and ragdoll:</b> hold Crouch when landing from a big fall to roll out of it.
    /// Otherwise a very hard landing, a hit from a knockback surface or <see cref="Tumble"/> sends the
    /// body limp (<see cref="Stance.Ragdoll"/>), and the character gets back up where it comes to rest.</item>
    /// </list>
    /// Every probe is measured in the character's gravity frame, so it all works on walls and ceilings.
    /// Stances are shared with other players through <see cref="CharacterStance"/>.
    /// </summary>
    public sealed class CharacterParkour : MonoBehaviour, IMovementMode {
        private enum Move { None, Hang, Climb, Mantle, Slide, WallRun, Roll }

        /// <summary>How fast (m/s²) the remembered fall speed fades while airborne.</summary>
        private const float FallSpeedMemory = 120f;

        [Header("Ledge grab")]
        [Tooltip("Ledges are caught when their top is this high above the feet, as fractions of body height.")]
        [SerializeField] private Vector2 grabHeight = new(0.72f, 1.2f);
        [Tooltip("How far in front of the body ledges are looked for (m).")]
        [SerializeField, Min(0.05f)] private float grabReach = 0.8f;
        [SerializeField, Min(0.1f)] private float climbTime = 0.6f;
        [SerializeField, Min(0f)] private float shimmySpeed = 3f;
        [Tooltip("Speed away from the wall when kicking off a ledge (m/s).")]
        [SerializeField, Min(0f)] private float ledgeKickSpeed = 8f;
        [Tooltip("Seconds after letting go before a ledge can be caught again.")]
        [SerializeField, Min(0f)] private float regrabDelay = 0.4f;

        [Header("Mantle")]
        [Tooltip("Obstacles this high above the feet (fractions of body height) can be pulled up onto.")]
        [SerializeField] private Vector2 mantleHeight = new(0.12f, 0.72f);
        [SerializeField, Min(0.05f)] private float mantleTime = 0.38f;

        [Header("Slide")]
        [Tooltip("Slide start speed as a multiple of the running speed.")]
        [SerializeField, Min(1f)] private float slideBoost = 1.3f;
        [Tooltip("How quickly a slide slows down (m/s²).")]
        [SerializeField, Min(0f)] private float slideFriction = 9f;
        [Tooltip("Seconds a tapped slide lasts.")]
        [SerializeField, Min(0.1f)] private float slideTime = 0.7f;
        [Tooltip("Seconds a slide can last while Crouch is held.")]
        [SerializeField, Min(0.1f)] private float slideMaxTime = 1.2f;
        [Tooltip("Collider height while sliding, as a fraction of standing height.")]
        [SerializeField, Range(0.3f, 1f)] private float slideHeight = 0.5f;
        [Tooltip("Extra forward speed a jump out of a slide carries (m/s).")]
        [SerializeField, Min(0f)] private float slideJumpBoost = 6f;
        [SerializeField, Min(0f)] private float slideTurnRate = 70f;

        [Header("Wall run")]
        [SerializeField, Min(0.1f)] private float wallRunTime = 1.3f;
        [Tooltip("Gravity while wall running, as a fraction of normal.")]
        [SerializeField, Range(0f, 1f)] private float wallRunGravity = 0.22f;
        [Tooltip("How far beside the body walls are looked for (m).")]
        [SerializeField, Min(0.05f)] private float wallRunReach = 0.7f;
        [SerializeField, Min(0f)] private float wallKickSpeed = 9f;

        [Header("Landing")]
        [Tooltip("Landing faster than this (m/s along gravity) without rolling knocks the character down.")]
        [SerializeField, Min(0f)] private float hardLandingSpeed = 34f;
        [Tooltip("Landing faster than this with Crouch held rolls.")]
        [SerializeField, Min(0f)] private float rollLandingSpeed = 14f;
        [Tooltip("A Crouch press this long before landing still rolls.")]
        [SerializeField, Min(0f)] private float rollBuffer = 0.3f;
        [SerializeField, Min(0.1f)] private float rollTime = 0.55f;

        [Header("Ragdoll")]
        [Tooltip("Knockback slower than this (m/s) only shoves; faster sends the body limp.")]
        [SerializeField, Min(0f)] private float knockdownSpeed = 6f;
        [SerializeField, Min(0f)] private float minRagdollTime = 1.1f;
        [SerializeField, Min(0.1f)] private float maxRagdollTime = 3f;
        [Tooltip("The body counts as at rest below this speed (m/s).")]
        [SerializeField, Min(0f)] private float restSpeed = 1.5f;
        [Tooltip("Gets up early while falling this fast (m/s), so falls off the world are noticed.")]
        [SerializeField, Min(0f)] private float freeFallGetUpSpeed = 30f;

        /// <summary>True while the local character is a limp ragdoll.</summary>
        public bool Ragdolled { get; private set; }

        private CharacterMovement _movement;
        private LocalCharacter _character;
        private CharacterStance _stance;
        private Animator _animator;
        private Transform _hips;
        private Rigidbody _hipsBody;

        private Move _move;
        private float _moveStart;
        private Vector3 _wallNormal, _ledge, _moveFrom, _moveTo, _direction;
        private float _speed, _wallSide;
        private Collider _lastRunWall;
        private float _noGrabUntil, _crouchPressedAt = float.NegativeInfinity, _fallSpeed;
        private bool _wasGrounded = true, _crouchHeld, _crouchQueued;
        private readonly Collider[] _overlaps = new Collider[4];
        private Vector3 _standSize, _standCenter;
        private bool _shrunk;
        private float _ragdollStart, _restingSince, _fallingSince;

        // ------------------------------------------------------------------ lifecycle
        private void OnEnable() {
            LocalCharacter.Teleported += OnTeleported;
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }

        private void OnDisable() {
            LocalCharacter.Teleported -= OnTeleported;
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            EndRagdoll(false);
            EndMove();
            Bind(null);
        }

        private void OnCharacterAdded(LocalCharacter character) {
            _character = character;
            _stance = character != null ? character.GetComponent<CharacterStance>() : null;
            _animator = character != null ? character.GetComponent<Animator>() : null;
            _hips = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            _hipsBody = _hips != null ? _hips.GetComponent<Rigidbody>() : null;
            _move = Move.None;
            _shrunk = false;
            Ragdolled = false;
            if (character != null && character.TryGetComponent(out BoxCollider box)) {
                _standSize = box.size;
                _standCenter = box.center;
            }
        }

        private void Bind(CharacterMovement movement) {
            if (_movement == movement)
                return;
            if (_movement != null) {
                _movement.RemoveMode(this);
                _movement.KnockedBack -= OnKnockedBack;
            }
            _movement = movement;
            if (_movement != null) {
                _movement.AddMode(this);
                _movement.KnockedBack += OnKnockedBack;
            }
        }

        private void Update() {
            Bind(CharacterMovement.Local);
            if (_movement == null || _movement.Controls == null)
                return;
            bool crouch = _movement.Controls.crouch;
            if (crouch && !_crouchHeld) {
                _crouchPressedAt = Time.time;
                _crouchQueued = true;
            }
            _crouchHeld = crouch;
        }

        private void FixedUpdate() {
            if (Ragdolled)
                StepRagdoll();
        }

        private void OnTeleported(LocalCharacter character) {
            if (character != _character)
                return;
            EndRagdoll(false);
            EndMove();
        }

        // ------------------------------------------------------------------ body frame
        private Rigidbody Body => _movement.Body;
        private Vector3 Up => _movement.Up;
        private float Scale => _movement.BodyCollider.transform.lossyScale.y;
        private float Height => _standSize.y * Scale;
        private float HalfDepth => _standSize.z * 0.5f * Scale;
        private float HalfWidth => _standSize.x * 0.5f * Scale;
        private Vector3 Feet => _movement.BodyCollider.transform.TransformPoint(_standCenter - Vector3.up * (_standSize.y * 0.5f));
        private bool Upright => Vector3.Angle(_movement.BodyCollider.transform.up, Up) < 10f;
        /// <summary>Crouch was pressed since the last physics step.</summary>
        private bool CrouchPressed => _crouchQueued;
        private int Mask => _movement.WorldMask;
        /// <summary>The character's physics scene (universes may simulate their own).</summary>
        private PhysicsScene World => _character.gameObject.scene.GetPhysicsScene();

        private bool Ray(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit) =>
            World.Raycast(origin, direction, out hit, distance, Mask, QueryTriggerInteraction.Ignore) && hit.distance > 0.001f;

        private bool Blocked(Vector3 centre, Vector3 halfExtents, Quaternion rotation) =>
            World.OverlapBox(centre, halfExtents, _overlaps, rotation, Mask, QueryTriggerInteraction.Ignore) > 0;

        /// <summary>Moving geometry (it has a rigidbody) is never grabbed or run along.</summary>
        private static bool IsStatic(Collider collider) => collider != null && collider.attachedRigidbody == null;

        private Vector3 PlanarVelocity => Vector3.ProjectOnPlane(Body.linearVelocity, Up);

        private void SetVelocity(Vector3 planar, float alongUp) => Body.linearVelocity = Vector3.ProjectOnPlane(planar, Up) + Up * alongUp;

        // ------------------------------------------------------------------ IMovementMode
        public bool Step(CharacterMovement movement, float dt) {
            if (movement != _movement || _character == null || _movement.BodyCollider == null || _stance == null)
                return false;
            bool active = _move != Move.None || TryStart();
            _crouchQueued = false;
            if (!active)
                return false;
            switch (_move) {
                case Move.Hang: StepHang(dt); break;
                case Move.Climb:
                case Move.Mantle: StepPullUp(); break;
                case Move.Slide: StepSlide(dt); break;
                case Move.WallRun: StepWallRun(dt); break;
                case Move.Roll: StepRoll(dt); break;
            }
            return true;
        }

        /// <summary>Starts a move if this step calls for one. Also watches landings for rolls and knockdowns.</summary>
        private bool TryStart() {
            bool grounded = _movement.Grounded;
            float alongUp = Vector3.Dot(Body.linearVelocity, Up);
            bool landed = grounded && !_wasGrounded;
            _wasGrounded = grounded;
            if (!grounded) {
                // Remember the fall speed for a few steps: the contact that lands the body stops it a
                // step or two before the ground probe reports grounded.
                _fallSpeed = Mathf.Max(-alongUp, _fallSpeed - FallSpeedMemory * Time.fixedDeltaTime, 0f);
            } else {
                _lastRunWall = null;
            }

            if (landed) {
                float impact = _fallSpeed;
                _fallSpeed = 0f;
                bool rolling = Time.time - _crouchPressedAt < rollBuffer || _crouchHeld;
                if (rolling && impact > rollLandingSpeed) return StartRoll();
                if (impact > hardLandingSpeed) {
                    Tumble(PlanarVelocity * 0.6f);
                    return false;
                }
            }
            if (!Upright) return false;

            bool hasInput = _movement.TryGetMoveDirection(out Vector3 input);
            if (grounded) {
                if (CrouchPressed && _movement.MoveSpeed > _movement.WalkSpeed * 0.8f) return StartSlide(hasInput ? input : _movement.Facing);
                if (_movement.JumpPressed && hasInput && TryMantle(input)) return true;
                return false;
            }
            if (Time.time >= _noGrabUntil && alongUp < 3f && TryHang()) return true;
            if (hasInput && Vector3.Dot(input, _movement.Facing) > 0.5f && TryMantle(input)) return true;
            if (hasInput && _movement.Controls.sprint && alongUp > -8f && TryWallRun()) return true;
            return false;
        }

        private void Begin(Move move, Stance stance) {
            _move = move;
            _moveStart = Time.time;
            _stance.Set(stance);
        }

        private void EndMove() {
            if (_move is Move.Climb or Move.Mantle && Body != null)
                Body.isKinematic = false;
            if (_shrunk)
                Stand(force: true);
            _move = Move.None;
            if (_stance != null && _stance.Current != Stance.Ragdoll)
                _stance.Set(Stance.Normal);
        }

        // ------------------------------------------------------------------ ledges
        /// <summary>
        /// Finds a ledge in front of a body whose feet are at <paramref name="feet"/>: a wall facing it
        /// whose top is between <paramref name="height"/>.x and .y body heights up.
        /// </summary>
        private bool FindLedge(Vector3 feet, Vector3 forward, Vector2 height, out Vector3 edge, out Vector3 normal) {
            edge = normal = Vector3.zero;
            Vector3 up = Up;
            float h = Height;
            Vector3 probe = feet + up * (h * Mathf.Clamp(height.x - 0.05f, 0.08f, 0.6f));
            if (!Ray(probe, forward, HalfDepth + grabReach, out RaycastHit wall) || !IsStatic(wall.collider))
                return false;
            normal = Vector3.ProjectOnPlane(wall.normal, up);
            if (normal.sqrMagnitude < 0.8f || Vector3.Dot(normal.normalized, -forward) < 0.5f)
                return false;
            normal.Normalize();
            Vector3 face = wall.point;
            Vector3 top = face - normal * 0.3f + up * Vector3.Dot(feet + up * (h * height.y) - face, up);
            if (!Ray(top, -up, h * (height.y - height.x), out RaycastHit ledge) || Vector3.Dot(ledge.normal, up) < 0.75f)
                return false;
            edge = face + up * Vector3.Dot(ledge.point - face, up);
            return IsStatic(ledge.collider);
        }

        /// <summary>Whether a standing body fits on top of <paramref name="edge"/>, just past it.</summary>
        private bool FitsOnTop(Vector3 edge, Vector3 normal, out Vector3 standFeet) {
            standFeet = edge - normal * (HalfDepth + 0.35f) + Up * 0.04f;
            Vector3 halfExtents = new Vector3(HalfWidth, Height * 0.5f, HalfDepth) * 0.92f;
            Quaternion rotation = Quaternion.LookRotation(-normal, Up);
            return !Blocked(standFeet + Up * (Height * 0.5f + 0.02f), halfExtents, rotation);
        }

        private Vector3 HangFeet(Vector3 edge, Vector3 normal) => edge + normal * (HalfDepth + 0.04f) - Up * (Height * CharacterStance.HangReach);

        private bool TryHang() {
            if (!FindLedge(Feet, _movement.Facing, grabHeight, out Vector3 edge, out Vector3 normal))
                return false;
            _ledge = edge;
            _wallNormal = normal;
            _movement.Face(-normal, 0f);
            Begin(Move.Hang, Stance.Hang);
            return true;
        }

        private void StepHang(float dt) {
            Vector3 up = Up, tangent = Vector3.Cross(up, _wallNormal);
            bool hasInput = _movement.TryGetMoveDirection(out Vector3 input);

            // Shimmy along the ledge, following it as long as it continues.
            Vector3 feet = HangFeet(_ledge, _wallNormal);
            float sideways = hasInput ? Vector3.Dot(input, tangent) : 0f;
            if (Mathf.Abs(sideways) > 0.35f) {
                Vector3 shifted = feet + tangent * (Mathf.Sign(sideways) * shimmySpeed * dt);
                if (FindLedge(shifted + _wallNormal * 0.05f, -_wallNormal, new Vector2(CharacterStance.HangReach - 0.12f, CharacterStance.HangReach + 0.12f), out Vector3 edge, out Vector3 normal)) {
                    _ledge = edge;
                    _wallNormal = normal;
                    feet = HangFeet(edge, normal);
                }
            } else if (!FindLedge(feet + _wallNormal * 0.05f, -_wallNormal, new Vector2(CharacterStance.HangReach - 0.12f, CharacterStance.HangReach + 0.12f), out _, out _)) {
                Release(Vector3.zero); // the ledge crumbled or vanished
                return;
            }

            // Ease into the hang, then hold still.
            Vector3 position = Vector3.Lerp(Body.position, feet, Time.time - _moveStart < 0.12f ? 0.45f : 1f);
            Body.linearVelocity = Vector3.zero;
            Body.MovePosition(position);
            _movement.Face(-_wallNormal, 0f);

            if (_movement.JumpPressed) {
                if (hasInput && Vector3.Dot(input, _wallNormal) > 0.5f) {
                    Release(_wallNormal * ledgeKickSpeed + up * _movement.JumpSpeed);
                    _movement.Face(_wallNormal, _movement.WalkSpeed);
                } else if (FitsOnTop(_ledge, _wallNormal, out Vector3 standFeet)) {
                    StartPullUp(Move.Climb, Stance.Climb, standFeet);
                }
            } else if (CrouchPressed) {
                Release(_wallNormal * 1.5f);
            }
        }

        private void Release(Vector3 launch) {
            _noGrabUntil = Time.time + regrabDelay;
            EndMove();
            if (launch != Vector3.zero)
                _movement.Launch(launch);
        }

        // ------------------------------------------------------------------ climbing and mantling
        private bool TryMantle(Vector3 input) {
            if (!FindLedge(Feet, input, mantleHeight, out Vector3 edge, out Vector3 normal) || !FitsOnTop(edge, normal, out Vector3 standFeet))
                return false;
            _wallNormal = normal;
            _ledge = edge;
            _movement.Face(-normal, 0f);
            StartPullUp(Move.Mantle, Stance.Mantle, standFeet);
            return true;
        }

        private void StartPullUp(Move move, Stance stance, Vector3 standFeet) {
            _moveFrom = Body.position;
            _moveTo = standFeet;
            Body.linearVelocity = Vector3.zero;
            Body.isKinematic = true;
            Begin(move, stance);
        }

        /// <summary>Climb and mantle: rise until the feet clear the edge, then step forward onto it.</summary>
        private void StepPullUp() {
            float duration = _move == Move.Climb ? climbTime : mantleTime;
            float t = Mathf.Clamp01((Time.time - _moveStart) / duration);
            Vector3 up = Up;
            float rise = Vector3.Dot(_moveTo - _moveFrom, up);
            Vector3 lifted = _moveFrom + up * rise;
            const float split = 0.62f;
            Vector3 target = t < split
                ? Vector3.Lerp(_moveFrom, lifted, Mathf.SmoothStep(0f, 1f, t / split))
                : Vector3.Lerp(lifted, _moveTo, Mathf.SmoothStep(0f, 1f, (t - split) / (1f - split)));
            Body.MovePosition(target);
            _movement.Face(-_wallNormal, 0f);
            if (t >= 1f) {
                EndMove();
                Body.linearVelocity = Vector3.zero;
                _movement.Face(-_wallNormal, _movement.WalkSpeed * 0.5f);
            }
        }

        // ------------------------------------------------------------------ sliding
        private bool StartSlide(Vector3 direction) {
            _direction = Vector3.ProjectOnPlane(direction, Up).normalized;
            _speed = Mathf.Max(PlanarVelocity.magnitude, _movement.MoveSpeed) * slideBoost;
            Shrink();
            _movement.Face(_direction, 0f);
            Begin(Move.Slide, Stance.Slide);
            return true;
        }

        private void StepSlide(float dt) {
            float elapsed = Time.time - _moveStart;
            if (_movement.TryGetMoveDirection(out Vector3 input))
                _direction = Vector3.RotateTowards(_direction, input, slideTurnRate * Mathf.Deg2Rad * dt, 0f);
            _speed = Mathf.Max(_movement.WalkSpeed * 0.5f, _speed - slideFriction * dt);
            float alongUp = Vector3.Dot(Body.linearVelocity, Up);
            SetVelocity(_direction * _speed, alongUp);
            _movement.ApplyGravityStep(dt);
            _movement.Face(_direction, 0f);

            if (_movement.JumpPressed && Stand(force: false)) {
                float carried = Mathf.Max(0f, _speed - _movement.SprintSpeed) + slideJumpBoost;
                EndMove();
                _movement.Face(_direction, _movement.SprintSpeed);
                _movement.Launch(Up * _movement.JumpSpeed + _direction * carried);
                return;
            }
            if (!_movement.Grounded && elapsed > 0.1f) {
                // Slid off an edge: keep the momentum in the air.
                EndMove();
                _movement.Face(_direction, _movement.SprintSpeed);
                _movement.Launch(Up * alongUp + _direction * Mathf.Max(0f, _speed - _movement.SprintSpeed));
                return;
            }
            bool done = elapsed > slideMaxTime || _speed <= _movement.WalkSpeed * 0.6f || (!_crouchHeld && elapsed > slideTime);
            if (done && Stand(force: false)) {
                EndMove();
                _movement.Face(_direction, _speed);
            }
        }

        private void Shrink() {
            if (_shrunk || !_character.TryGetComponent(out BoxCollider box))
                return;
            float bottom = _standCenter.y - _standSize.y * 0.5f;
            box.size = new Vector3(_standSize.x, _standSize.y * slideHeight, _standSize.z);
            box.center = new Vector3(_standCenter.x, bottom + box.size.y * 0.5f, _standCenter.z);
            _shrunk = true;
        }

        /// <summary>Restores standing height if there is headroom (or <paramref name="force"/>). Returns whether it stood.</summary>
        private bool Stand(bool force) {
            if (!_shrunk)
                return true;
            if (!force) {
                Vector3 halfExtents = new Vector3(HalfWidth, Height * 0.5f, HalfDepth) * 0.9f;
                Vector3 centre = Feet + Up * (Height * 0.5f + 0.05f);
                if (Blocked(centre, halfExtents, Body.rotation))
                    return false;
            }
            if (_character != null && _character.TryGetComponent(out BoxCollider box)) {
                box.size = _standSize;
                box.center = _standCenter;
            }
            _shrunk = false;
            return true;
        }

        // ------------------------------------------------------------------ wall running
        private bool TryWallRun() {
            Vector3 planar = PlanarVelocity;
            if (planar.magnitude < _movement.WalkSpeed * 0.8f)
                return false;
            Vector3 up = Up, right = Vector3.Cross(up, _movement.Facing);
            Vector3 chest = Feet + up * (Height * 0.55f);
            for (int s = -1; s <= 1; s += 2) {
                if (!Ray(chest, right * s, HalfWidth + wallRunReach, out RaycastHit wall) || !IsStatic(wall.collider) || wall.collider == _lastRunWall)
                    continue;
                if (wall.collider.GetComponentInParent<IWallJumpSurface>() != null)
                    continue;
                Vector3 normal = Vector3.ProjectOnPlane(wall.normal, up);
                if (normal.sqrMagnitude < 0.9f || Mathf.Abs(Vector3.Dot(_movement.Facing, normal.normalized)) > 0.55f)
                    continue;
                _wallNormal = normal.normalized;
                _wallSide = s;
                _lastRunWall = wall.collider;
                _direction = Vector3.ProjectOnPlane(_movement.Facing, _wallNormal).normalized;
                _speed = Mathf.Max(planar.magnitude, _movement.SprintSpeed);
                SetVelocity(_direction * _speed, Mathf.Max(Vector3.Dot(Body.linearVelocity, up), 3f));
                Begin(Move.WallRun, s < 0 ? Stance.WallRunLeft : Stance.WallRunRight);
                return true;
            }
            return false;
        }

        private void StepWallRun(float dt) {
            Vector3 up = Up;
            Vector3 chest = Feet + up * (Height * 0.55f);
            bool still = Ray(chest, -_wallNormal, HalfWidth + wallRunReach + 0.2f, out RaycastHit wall) && IsStatic(wall.collider);
            bool holding = _movement.Controls.sprint && _movement.TryGetMoveDirection(out _);
            if (!still || !holding || _movement.Grounded || Time.time - _moveStart > wallRunTime) {
                EndMove();
                _movement.Face(_direction, _speed);
                return;
            }
            if (_movement.JumpPressed) {
                EndMove();
                _movement.Face(_direction + _wallNormal * 0.6f, _movement.SprintSpeed);
                _movement.Launch(_wallNormal * wallKickSpeed + up * _movement.JumpSpeed + _direction * 2f);
                return;
            }
            float alongUp = Mathf.Max(Vector3.Dot(Body.linearVelocity, up), -4f);
            float gap = wall.distance - HalfWidth;
            SetVelocity(_direction * _speed - _wallNormal * Mathf.Clamp(gap * 8f, 0f, 4f), alongUp);
            _movement.ApplyGravityStep(dt, wallRunGravity);
            _movement.Face(_direction, 0f);
        }

        // ------------------------------------------------------------------ rolling
        private bool StartRoll() {
            Vector3 planar = PlanarVelocity;
            _direction = planar.sqrMagnitude > 1f ? planar.normalized : _movement.Facing;
            _speed = Mathf.Max(planar.magnitude, _movement.WalkSpeed);
            _movement.Face(_direction, 0f);
            Begin(Move.Roll, Stance.Roll);
            return true;
        }

        private void StepRoll(float dt) {
            SetVelocity(_direction * _speed, Vector3.Dot(Body.linearVelocity, Up));
            _movement.ApplyGravityStep(dt);
            _movement.Face(_direction, 0f);
            if (Time.time - _moveStart >= rollTime) {
                EndMove();
                _movement.Face(_direction, _speed);
            }
        }

        // ------------------------------------------------------------------ ragdoll
        private void OnKnockedBack(Vector3 velocity) {
            if (velocity.magnitude >= knockdownSpeed)
                Tumble(velocity);
            else if (!Ragdolled && Body != null)
                _movement.Launch(velocity);
        }

        /// <summary>
        /// Sends the local character limp, flung with <paramref name="velocity"/> on top of its own
        /// motion. It gets back up where the body comes to rest (or wherever a teleport puts it).
        /// </summary>
        public void Tumble(Vector3 velocity) {
            if (Ragdolled || _stance == null || _movement == null || Body == null)
                return;
            Vector3 carried = Body.linearVelocity;
            EndMove();
            Ragdolled = true;
            _ragdollStart = _restingSince = Time.time;
            _fallingSince = float.PositiveInfinity;
            // The root stays put (and keeps its network position) while the limbs fly.
            Body.linearVelocity = Vector3.zero;
            Body.isKinematic = true;
            _stance.Set(Stance.Ragdoll, carried + velocity, -Up * _movement.GravityAcceleration);
        }

        private void StepRagdoll() {
            if (_hipsBody == null) {
                EndRagdoll(true);
                return;
            }
            float now = Time.time, elapsed = now - _ragdollStart;
            Vector3 velocity = _hipsBody.linearVelocity;
            if (velocity.magnitude > restSpeed) _restingSince = now;
            float falling = -Vector3.Dot(velocity, Up);
            if (falling < freeFallGetUpSpeed) _fallingSince = float.PositiveInfinity;
            else if (float.IsPositiveInfinity(_fallingSince)) _fallingSince = now;

            bool rested = elapsed > minRagdollTime && now - _restingSince > 0.4f;
            if (rested || elapsed > maxRagdollTime || now - _fallingSince > 0.5f)
                EndRagdoll(true);
        }

        /// <summary>Ends the ragdoll; with <paramref name="getUp"/> the character stands up where the body lies.</summary>
        private void EndRagdoll(bool getUp) {
            if (!Ragdolled)
                return;
            Ragdolled = false;
            if (Body == null)
                return;
            if (getUp && _hips != null) {
                Vector3 up = Up;
                Vector3 hips = _hips.position;
                Vector3 feet = Ray(hips + up * 0.3f, -up, Height, out RaycastHit ground)
                    ? ground.point + up * 0.05f
                    : hips - up * (Height * 0.5f);
                // Wedged under something (or inside it)? Stand back up where the fall began instead.
                Vector3 halfExtents = new Vector3(HalfWidth, Height * 0.5f, HalfDepth) * 0.9f;
                if (Blocked(feet + up * (Height * 0.5f + 0.05f), halfExtents, Body.rotation))
                    feet = Body.position;
                Vector3 facing = Vector3.ProjectOnPlane(_hips.forward, up);
                Body.position = feet;
                Body.transform.position = feet;
                if (facing.sqrMagnitude > 0.01f) _movement.Face(facing, 0f);
            }
            _stance.Set(Stance.Normal);
            Body.isKinematic = false;
            Body.linearVelocity = Vector3.zero;
        }
    }
}
