using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>A special stance a character can be in. Values are sent over the network: add new ones at the end.</summary>
    public enum Stance : byte { Normal, Hang, Climb, Mantle, Slide, WallRunLeft, WallRunRight, Roll, Ragdoll, WallSlide, WallKick, Dash }

    /// <summary>A stance and, for <see cref="Stance.Ragdoll"/>, how the body is flung and the gravity it falls with.</summary>
    public struct StanceState {
        public Stance stance;
        public Vector3 fling;
        /// <summary>Gravity (m/s²) for the limp body; zero for the world's.</summary>
        public Vector3 gravity;
    }

    /// <summary>
    /// Shares a character's special stance (parkour moves, ragdoll) with every client and presents it.
    /// The owner decides the stance and shows it at once; the server relays it to everyone else.
    /// <para>
    /// Every client presents it the same way: <see cref="Stance.Ragdoll"/> hands the body to the
    /// physics ragdoll (<see cref="RagdollCharacter"/>); the other stances layer a procedural pose over
    /// the animator (hands reaching for a ledge, leaning back into a slide, tilting off a wall, a
    /// forward roll, clinging to a wall, kicking off it, a sprinting dash), so they need no extra animation clips.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterStance : NetworkBehaviour {
        /// <summary>Height of a hanging character's grip above its feet, as a fraction of its height.</summary>
        public const float HangReach = 1.02f;

        [Tooltip("Seconds a pose takes to blend in or out.")]
        [SerializeField, Min(0.01f)] private float poseBlendTime = 0.12f;
        [Tooltip("Seconds a forward roll takes.")]
        [SerializeField, Min(0.1f)] private float rollTime = 0.55f;
        [Tooltip("How far the hips drop in a slide, as a fraction of the character's height.")]
        [SerializeField, Range(0f, 0.5f)] private float slideDrop = 0.27f;
        [Tooltip("How far back (degrees) the body leans in a slide.")]
        [SerializeField, Range(0f, 90f)] private float slideLean = 62f;
        [Header("Wall slide and wall jump")]
        [Tooltip("How high up the body the hands cling to the wall, as a fraction of height.")]
        [SerializeField, Range(0.4f, 1.1f)] private float wallGripHeight = 0.9f;
        [Tooltip("How far the hands scrub up and down the wall while sliding, as a fraction of height.")]
        [SerializeField, Range(0f, 0.15f)] private float wallScrub = 0.04f;
        [Tooltip("Scrub cycles per second.")]
        [SerializeField, Min(0f)] private float wallScrubRate = 1.6f;
        [Tooltip("How far the chest leans into the wall while sliding (degrees).")]
        [SerializeField, Range(0f, 40f)] private float wallLean = 12f;
        [Tooltip("Wall slide: degrees the planted leg's thigh / shin bend (thigh goes back, shin forward).")]
        [SerializeField, Range(0f, 120f)] private float wallPlantedThigh = 42f, wallPlantedShin = 62f;
        [Tooltip("Wall slide: degrees the free leg's thigh / shin bend.")]
        [SerializeField, Range(0f, 120f)] private float wallFreeThigh = 14f, wallFreeShin = 24f;
        [Tooltip("Dash: degrees the forearms bend.")]
        [SerializeField, Range(0f, 90f)] private float dashForearmBend = 12f;
        [Tooltip("Seconds the wall-jump kick pose lasts.")]
        [SerializeField, Min(0.1f)] private float wallKickTime = 0.55f;
        [Tooltip("How far the body somersaults after pushing off a wall (degrees; 0 = a turn with no flip).")]
        [SerializeField, Range(0f, 720f)] private float wallKickFlip = 360f;

        [Header("Dash")]
        [Tooltip("How far the body leans forward while dashing (degrees).")]
        [SerializeField, Range(0f, 70f)] private float dashLean = 38f;
        [Tooltip("How far back the arms sweep while dashing (degrees).")]
        [SerializeField, Range(0f, 110f)] private float dashArmSweep = 78f;
        [Tooltip("How far the hips drop while dashing, as a fraction of height.")]
        [SerializeField, Range(0f, 0.2f)] private float dashDrop = 0.05f;
        [Tooltip("Animator speed multiplier while dashing: the legs cycle this much faster.")]
        [SerializeField, Min(1f)] private float dashAnimatorSpeed = 1.8f;

        [Header("Sync")]
        [Tooltip("Fastest fling (m/s) the server relays, so a client cannot launch others' copies wildly.")]
        [SerializeField, Min(0f)] private float maxFling = 45f;
        [Tooltip("Ragdoll spin (rad/s) per m/s of sideways fling, so hits send the body tumbling.")]
        [SerializeField, Min(0f)] private float tumblePerSpeed = 0.35f;
        [Tooltip("Fastest ragdoll spin (rad/s).")]
        [SerializeField, Min(0f)] private float maxTumble = 9f;
        [Tooltip("Strongest ragdoll gravity (m/s²) the server relays.")]
        [SerializeField, Min(0f)] private float maxGravity = 80f;

        private readonly SyncVar<StanceState> _state = new(new StanceState(),
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.ExcludeOwner, 0f, Channel.Reliable));

        /// <summary>The stance this client is showing.</summary>
        public Stance Current { get; private set; }
        /// <summary>Seconds since <see cref="Current"/> began.</summary>
        public float Elapsed => Time.time - _since;
        /// <summary>Raised on this client when the shown stance changes (previous, next).</summary>
        public event System.Action<Stance, Stance> Changed;

        private Animator _animator;
        private RagdollCharacter _ragdoll;
        private Transform _hips, _spine, _chest;
        private Transform _leftUpperArm, _leftLowerArm, _leftHand, _rightUpperArm, _rightLowerArm, _rightHand;
        private Transform _leftUpperLeg, _leftLowerLeg, _rightUpperLeg, _rightLowerLeg, _neck;
        private float _since;
        private float _weight;
        private Stance _posed;            // the stance the pose weight belongs to (kept while blending out)
        private Vector3 _leftGrip, _rightGrip; // world grips pinned when a climb or mantle starts
        private float _baseAnimatorSpeed = 1f;
        private bool _animatorSpeedScaled;

        private void Awake() {
            _animator = GetComponent<Animator>();
            _ragdoll = GetComponent<RagdollCharacter>();
            if (_animator != null && _animator.isHuman) {
                _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
                _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                _leftUpperLeg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                _leftLowerLeg = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                _rightUpperLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                _rightLowerLeg = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            }
            _state.OnChange += OnStateChanged;
        }

        private void OnDestroy() => _state.OnChange -= OnStateChanged;

        public override void OnStartClient() {
            base.OnStartClient();
            if (!IsOwner && _state.Value.stance != Stance.Normal)
                Show(_state.Value);
        }

        public override void OnStopClient() {
            base.OnStopClient();
            if (Current == Stance.Ragdoll)
                Show(new StanceState());
        }

        // ------------------------------------------------------------------ changing the stance
        /// <summary>
        /// Owner only: shows <paramref name="stance"/> here at once and shares it with everyone else.
        /// For <see cref="Stance.Ragdoll"/>, <paramref name="fling"/> is added to the body's velocity and
        /// it falls with <paramref name="gravity"/> (zero for the world's).
        /// </summary>
        public void Set(Stance stance, Vector3 fling = default, Vector3 gravity = default) {
            if (stance == Current)
                return;
            var state = new StanceState { stance = stance, fling = fling, gravity = gravity };
            Show(state);
            if (IsClientInitialized && IsOwner)
                ShareStance(state);
        }

        [ServerRpc]
        private void ShareStance(StanceState state) {
            state.fling = Vector3.ClampMagnitude(state.fling, maxFling);
            state.gravity = Vector3.ClampMagnitude(state.gravity, maxGravity);
            _state.Value = state;
        }

        private void OnStateChanged(StanceState previous, StanceState next, bool asServer) {
            // The owner already shows its own stance; a dedicated server does not present at all.
            if (asServer || IsOwner)
                return;
            Show(next);
        }

        private void Show(StanceState state) {
            Stance stance = state.stance;
            Stance previous = Current;
            Current = stance;
            _since = Time.time;
            if (_ragdoll != null) {
                if (stance == Stance.Ragdoll) {
                    _ragdoll.Gravity = state.gravity != Vector3.zero ? state.gravity : null;
                    _ragdoll.SetRagdoll(true);
                    _ragdoll.AddVelocity(state.fling, Tumble(state.fling));
                } else if (_ragdoll.IsRagdoll) {
                    _ragdoll.SetRagdoll(false);
                    _ragdoll.Gravity = null;
                }
            }
            if (stance is Stance.Climb or Stance.Mantle && _leftHand != null) {
                // Hands stay where they grab while the body pulls up past them.
                PoseGrips(stance, out _leftGrip, out _rightGrip);
            }
            if (HasPose(stance)) {
                if (_posed != stance && !HasPose(previous)) _weight = 0f;
                _posed = stance;
            }
            Changed?.Invoke(previous, stance);
        }

        /// <summary>Spin (rad/s) for a body flung with <paramref name="fling"/>: head over heels away from the hit.</summary>
        private Vector3 Tumble(Vector3 fling) {
            Vector3 planar = Vector3.ProjectOnPlane(fling, transform.up);
            if (planar.sqrMagnitude < 0.01f)
                return Vector3.zero;
            return Vector3.Cross(transform.up, planar.normalized) * Mathf.Min(planar.magnitude * tumblePerSpeed, maxTumble);
        }

        private static bool HasPose(Stance stance) => stance is not (Stance.Normal or Stance.Ragdoll);

        // ------------------------------------------------------------------ poses
        private void LateUpdate() {
            if (_hips == null || _animator == null || !_animator.enabled)
                return;
            bool posing = HasPose(Current);
            _weight = Mathf.MoveTowards(_weight, posing ? 1f : 0f, Time.deltaTime / poseBlendTime);
            ScaleAnimatorSpeed(_posed == Stance.Dash ? _weight : 0f);
            if (_weight <= 0f)
                return;
            float w = Mathf.SmoothStep(0f, 1f, _weight);
            Vector3 up = transform.up, right = transform.right, forward = transform.forward;
            float height = BodyHeight();
            switch (_posed) {
                case Stance.Hang: {
                    PoseGrips(Stance.Hang, out Vector3 left, out Vector3 right2);
                    Reach(left, right2, forward, w);
                    break;
                }
                case Stance.Climb:
                case Stance.Mantle:
                    Bend(_spine, right, 18f * w);
                    Reach(_leftGrip, _rightGrip, forward, w);
                    break;
                case Stance.Slide:
                    // Lean back into the slide, feet first, one hand trailing.
                    _hips.position -= up * (height * slideDrop * w);
                    Bend(_hips, right, -slideLean * w);
                    Bend(_chest != null ? _chest : _spine, right, 22f * w);
                    break;
                case Stance.WallRunLeft:
                case Stance.WallRunRight: {
                    // Tilt off the wall, the arm on the open side swinging out for balance.
                    float side = _posed == Stance.WallRunLeft ? 1f : -1f;
                    Bend(_hips, forward, -side * 24f * w);
                    Transform hand = side > 0f ? _rightHand : _leftHand;
                    Transform upper = side > 0f ? _rightUpperArm : _leftUpperArm, lower = side > 0f ? _rightLowerArm : _leftLowerArm;
                    Vector3 shoulder = upper.position;
                    TwoBoneIK(upper, lower, hand, shoulder + (right * side * 0.9f + up * 0.35f + forward * 0.2f) * height * 0.28f, -up, w);
                    break;
                }
                case Stance.WallSlide:
                    PoseWallSlide(up, right, forward, height, w);
                    break;
                case Stance.WallKick:
                    PoseWallKick(up, right, forward, height, w);
                    break;
                case Stance.Dash:
                    PoseDash(up, right, height, w);
                    break;
                case Stance.Roll: {
                    // Tuck into a ball (knees to chest, head down), drop it to the floor and tumble once
                    // forward about its centre, then unfold.
                    float t = Mathf.Clamp01(Elapsed / rollTime);
                    float tuck = w * Mathf.SmoothStep(0f, 1f, t / 0.2f) * Mathf.SmoothStep(0f, 1f, (1f - t) / 0.25f);
                    Bend(_spine, right, 40f * tuck);
                    Bend(_chest, right, 30f * tuck);
                    Bend(_neck, right, 35f * tuck);
                    Bend(_leftUpperLeg, right, -105f * tuck);
                    Bend(_rightUpperLeg, right, -95f * tuck);
                    Bend(_leftLowerLeg, right, 150f * tuck);
                    Bend(_rightLowerLeg, right, 140f * tuck);
                    Vector3 feet = transform.position;
                    float hipsHeight = Vector3.Dot(_hips.position - feet, up);
                    float radius = height * 0.2f;
                    _hips.position -= up * (Mathf.Max(0f, hipsHeight - radius) * tuck);
                    Vector3 pivot = _hips.position + up * (height * 0.06f * tuck);
                    Quaternion spin = Quaternion.AngleAxis(360f * Mathf.SmoothStep(0f, 1f, t) * w, right);
                    _hips.SetPositionAndRotation(pivot + spin * (_hips.position - pivot), spin * _hips.rotation);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ wall slide, wall kick, dash
        /// <summary>Hands pressed to the wall in front, knees bent and one foot planted on it: clinging on while sliding down.</summary>
        private void PoseWallSlide(Vector3 up, Vector3 right, Vector3 forward, float height, float w) {
            float scrub = Mathf.Sin(Elapsed * wallScrubRate * Mathf.PI * 2f) * wallScrub * height;
            float shoulder = Vector3.Distance(_leftUpperArm.position, _rightUpperArm.position) * 0.75f;
            Vector3 grip = transform.position + up * (height * wallGripHeight) + forward * (HalfDepth() + 0.08f);
            // The hands trade places on the wall: one rubs up while the other rubs down.
            Reach(grip - right * shoulder + up * scrub, grip + right * shoulder - up * scrub, forward, w);
            Bend(_hips, right, wallLean * w);
            Bend(_neck, right, -wallLean * 0.6f * w);
            Bend(_leftUpperLeg, right, -wallPlantedThigh * w);
            Bend(_leftLowerLeg, right, wallPlantedShin * w);
            Bend(_rightUpperLeg, right, -wallFreeThigh * w);
            Bend(_rightLowerLeg, right, wallFreeShin * w);
        }

        /// <summary>
        /// Push off the wall: crouch into it for the first moments, then spring away and somersault (or just
        /// turn), arms flung out, landing the pose ready to run in the new direction.
        /// </summary>
        private void PoseWallKick(Vector3 up, Vector3 right, Vector3 forward, float height, float w) {
            float t = Mathf.Clamp01(Elapsed / wallKickTime);
            float crouch = Mathf.SmoothStep(0f, 1f, t / 0.18f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.12f));
            float tuck = Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.3f));
            float spread = Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.15f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f));
            Bend(_leftUpperLeg, right, (-70f * crouch - 90f * tuck) * w);
            Bend(_rightUpperLeg, right, (-60f * crouch - 80f * tuck) * w);
            Bend(_leftLowerLeg, right, (95f * crouch + 130f * tuck) * w);
            Bend(_rightLowerLeg, right, (90f * crouch + 120f * tuck) * w);
            Bend(_spine, right, (20f * crouch + 25f * tuck) * w);
            Bend(_leftUpperArm, forward, -70f * spread * w);
            Bend(_rightUpperArm, forward, 70f * spread * w);
            if (wallKickFlip <= 0f)
                return;
            float flip = Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.7f);
            Vector3 pivot = _hips.position + up * (height * 0.05f);
            Quaternion spin = Quaternion.AngleAxis(wallKickFlip * flip * w, right);
            _hips.SetPositionAndRotation(pivot + spin * (_hips.position - pivot), spin * _hips.rotation);
        }

        /// <summary>Sonic-style run: low forward lean, head up, arms swept straight back, legs kept under the body.</summary>
        private void PoseDash(Vector3 up, Vector3 right, float height, float w) {
            _hips.position -= up * (height * dashDrop * w);
            Bend(_hips, right, dashLean * w);
            Bend(_chest != null ? _chest : _spine, right, dashLean * 0.2f * w);
            Bend(_neck, right, -dashLean * 0.75f * w);
            // The hips lean the legs back with them; swing them forward again so the feet still land under the body.
            Bend(_leftUpperLeg, right, -dashLean * 0.7f * w);
            Bend(_rightUpperLeg, right, -dashLean * 0.7f * w);
            Bend(_leftUpperArm, right, dashArmSweep * w);
            Bend(_rightUpperArm, right, dashArmSweep * w);
            Bend(_leftLowerArm, right, dashForearmBend * w);
            Bend(_rightLowerArm, right, dashForearmBend * w);
        }

        /// <summary>Speeds the animator up by up to <see cref="dashAnimatorSpeed"/> (<paramref name="amount"/> 0..1) and gives the original speed back at 0.</summary>
        private void ScaleAnimatorSpeed(float amount) {
            if (_animator == null)
                return;
            if (amount <= 0f) {
                if (_animatorSpeedScaled) {
                    _animator.speed = _baseAnimatorSpeed;
                    _animatorSpeedScaled = false;
                }
                return;
            }
            if (!_animatorSpeedScaled) {
                _baseAnimatorSpeed = _animator.speed;
                _animatorSpeedScaled = true;
            }
            _animator.speed = Mathf.Lerp(_baseAnimatorSpeed, _baseAnimatorSpeed * dashAnimatorSpeed, Mathf.SmoothStep(0f, 1f, amount));
        }

        /// <summary>Character height in metres, from the main collider (or the animator's scale).</summary>
        private float BodyHeight() => TryGetComponent(out BoxCollider box) ? box.size.y * transform.lossyScale.y : _animator.humanScale * 1.8f;

        /// <summary>Distance from the body's centre line to its front, in metres.</summary>
        private float HalfDepth() => TryGetComponent(out BoxCollider box) ? box.size.z * 0.5f * transform.lossyScale.z : 0.3f;

        /// <summary>Where the hands hold on while hanging from (or pulling up over) a ledge just above the head.</summary>
        private void PoseGrips(Stance stance, out Vector3 left, out Vector3 right) {
            Vector3 up = transform.up, side = transform.right, forward = transform.forward;
            float height = BodyHeight();
            float shoulderWidth = Vector3.Distance(_leftUpperArm.position, _rightUpperArm.position);
            Vector3 feet = transform.position;
            float front = HalfDepth() + 0.12f;
            Vector3 grip = stance == Stance.Mantle
                ? feet + up * (height * 0.5f) + forward * (front + 0.3f)
                : feet + up * (height * HangReach) + forward * front;
            left = grip - side * (shoulderWidth * 0.75f);
            right = grip + side * (shoulderWidth * 0.75f);
        }

        private void Reach(Vector3 left, Vector3 right, Vector3 forward, float weight) {
            TwoBoneIK(_leftUpperArm, _leftLowerArm, _leftHand, left, -forward, weight);
            TwoBoneIK(_rightUpperArm, _rightLowerArm, _rightHand, right, -forward, weight);
        }

        private static void Bend(Transform bone, Vector3 axis, float degrees) {
            if (bone != null && degrees != 0f)
                bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// <summary>
        /// Analytic two-bone IK: turns <paramref name="upper"/> and <paramref name="lower"/> so
        /// <paramref name="end"/> reaches <paramref name="target"/>, bending toward <paramref name="hint"/>.
        /// </summary>
        private static void TwoBoneIK(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight) {
            if (upper == null || lower == null || end == null || weight <= 0f)
                return;
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
            Vector3 toTarget = target - a;
            float reach = Mathf.Clamp(toTarget.magnitude, 0.001f, ab + bc - 0.0001f);
            Vector3 direction = toTarget.normalized;
            Vector3 bendAxis = Vector3.Cross(direction, hint);
            if (bendAxis.sqrMagnitude < 0.0001f)
                bendAxis = Vector3.Cross(direction, Vector3.up);
            bendAxis.Normalize();
            float cosAngle = Mathf.Clamp((ab * ab + reach * reach - bc * bc) / (2f * ab * reach), -1f, 1f);
            Vector3 elbow = a + Quaternion.AngleAxis(Mathf.Acos(cosAngle) * Mathf.Rad2Deg, bendAxis) * direction * ab;

            Quaternion upperTurn = Quaternion.FromToRotation(b - a, elbow - a);
            upper.rotation = Quaternion.Slerp(upper.rotation, upperTurn * upper.rotation, weight);
            b = lower.position;
            c = end.position;
            Quaternion lowerTurn = Quaternion.FromToRotation(c - b, a + direction * reach - b);
            lower.rotation = Quaternion.Slerp(lower.rotation, lowerTurn * lower.rotation, weight);
        }
    }
}
