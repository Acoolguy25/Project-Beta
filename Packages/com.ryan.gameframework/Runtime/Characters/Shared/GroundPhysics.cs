using FishNet.Object;
using RyanAssets.Core;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>
    /// Detects what the character stands on and carries it with moving ground.
    /// <para>
    /// "Down" is the character's gravity direction (<see cref="CharacterGravity"/>), so the same probe
    /// finds a floor, a wall or a ceiling. Only the controlling peer probes; movement reads
    /// <see cref="Grounded"/>, <see cref="GroundCollider"/> and <see cref="GroundVelocity"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class GroundPhysics : NetworkBehaviour {
        [Header("Ground Probe")]
        [Tooltip("How far above the feet the probe starts, so it still finds ground the collider rests slightly inside.")]
        [SerializeField, Min(0f)] private float probeStartAboveFeet = 0.03f;
        [Tooltip("How far the probe travels from its start; ground within (distance - start) below the feet counts as touched.")]
        [SerializeField, Min(0.001f)] private float probeDistance = 0.085f;
        [Tooltip("Steepest surface, relative to the character's up, that counts as ground rather than a wall.")]
        [SerializeField, Range(0f, 89f)] private float maxGroundAngle = 60f;
        [Tooltip("Layers ignored by the probe (the character itself and other characters).")]
        [SerializeField] private string[] ignoredLayers = { "Character", "LocalCharacter" };

        public bool Grounded { get; private set; }
        public Collider GroundCollider { get; private set; }
        /// <summary>Surface normal of the ground, or the character's up while airborne.</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        // Consumed by character movement as part of its target velocity. Moving a dynamic
        // body with MovePosition teleports it and resets its interpolation every step.
        public Vector3 GroundVelocity => pendingGroundDisplacement / Time.fixedDeltaTime;
        // The displacement that reached the character in the last completed physics step.
        public Vector3 InheritedGroundVelocity => lastSyncTime == Time.fixedTime
            ? appliedGroundDisplacement / Time.fixedDeltaTime
            : Vector3.zero;
        public Quaternion GroundRotationDelta { get; private set; } = Quaternion.identity;

        private Collider characterCollider;
        private Rigidbody characterBody;
        private CharacterGravity gravity;
        private GroundMotionTransfer currentTransfer;
        private Rigidbody currentGroundBody;
        private IPhysicsMotionSource currentMotionSource;
        private RigidbodyReposition groundReposition;
        private Vector3 previousGroundPosition;
        private Quaternion previousGroundRotation;
        private float lastCheckTime = float.NegativeInfinity;
        private float lastSyncTime = float.NegativeInfinity;
        private Vector3 pendingGroundDisplacement;
        private Vector3 appliedGroundDisplacement;
        private int groundMask;

        private Vector3 Up => gravity != null ? gravity.Up : Vector3.up;

        private void Awake() {
            characterCollider = GetComponent<Collider>();
            characterBody = GetComponent<Rigidbody>();
            gravity = GetComponent<CharacterGravity>();
            groundMask = ~LayerMask.GetMask(ignoredLayers);
        }

        private void FixedUpdate() {
            if (IsController)
                CheckGround();
        }

        private void OnDisable() => ResetGround();

        private void FollowGroundReposition(Vector3 position, Quaternion rotation) {
            if (!IsController || characterBody == null || characterBody.isKinematic)
                return;

            // Check the OLD platform pose first. A player who jumped or walked off
            // must not be pulled back by the last cached grounded state.
            var support = currentTransfer;
            lastCheckTime = float.NegativeInfinity;
            CheckGround();
            if (!Grounded || support == null || currentTransfer != support)
                return;

            Quaternion rotationDelta = rotation * Quaternion.Inverse(previousGroundRotation);
            var interpolation = characterBody.interpolation;
            characterBody.interpolation = RigidbodyInterpolation.None;
            if (support.transferPosition)
                characterBody.position = position + rotationDelta * (characterBody.position - previousGroundPosition);
            if (support.transferRotation)
                characterBody.rotation = rotationDelta * characterBody.rotation;
            characterBody.interpolation = interpolation;

            // The snap is not velocity. Forget old carry motion so it cannot launch
            // the rider or be applied again by SyncWithGround on this physics step.
            characterBody.linearVelocity -= GroundVelocity;
            pendingGroundDisplacement = appliedGroundDisplacement = Vector3.zero;
            previousGroundPosition = position;
            previousGroundRotation = rotation;
        }

        public void SyncWithGround() {
            if (!isActiveAndEnabled)
                return;
            // Expose the prior step's transfer to animation, which samples the resulting pose now.
            appliedGroundDisplacement = pendingGroundDisplacement;
            pendingGroundDisplacement = Vector3.zero;
            GroundRotationDelta = Quaternion.identity;
            lastSyncTime = Time.fixedTime;
            CheckGround();
            if (!Grounded || currentTransfer == null || characterBody == null || characterBody.isKinematic)
                return;

            Vector3 groundPosition = GroundPosition();
            Quaternion groundRotation = GroundRotation();
            Quaternion rotationDelta = groundRotation * Quaternion.Inverse(previousGroundRotation);

            if (currentTransfer.transferPosition &&
                (groundPosition != previousGroundPosition || groundRotation != previousGroundRotation)) {
                Vector3 position = groundPosition + rotationDelta * (characterBody.position - previousGroundPosition);
                pendingGroundDisplacement = position - characterBody.position;
            }

            // Movement consumes the planar part of GroundVelocity; apply the part along "up"
            // here so the movement controller can still own gravity and jumps on that axis.
            Vector3 up = Up;
            float alongUpVelocityChange = Vector3.Dot(pendingGroundDisplacement - appliedGroundDisplacement, up) / Time.fixedDeltaTime;
            characterBody.linearVelocity += up * alongUpVelocityChange;

            if (currentTransfer.transferRotation && groundRotation != previousGroundRotation)
                GroundRotationDelta = rotationDelta;

            previousGroundPosition = groundPosition;
            previousGroundRotation = groundRotation;
        }

        public void ResetGround() {
            if (groundReposition != null)
                groundReposition.Repositioning -= FollowGroundReposition;
            groundReposition = null;
            Grounded = false;
            GroundCollider = null;
            GroundNormal = Up;
            currentTransfer = null;
            currentGroundBody = null;
            currentMotionSource = null;
            lastCheckTime = float.NegativeInfinity;
            lastSyncTime = float.NegativeInfinity;
            pendingGroundDisplacement = Vector3.zero;
            appliedGroundDisplacement = Vector3.zero;
            GroundRotationDelta = Quaternion.identity;
        }

        private Vector3 GroundPosition() {
            if (currentMotionSource != null && currentMotionSource.HasScheduledPose)
                return currentMotionSource.ScheduledPosition;
            return currentGroundBody != null ? currentGroundBody.position : currentTransfer.transform.position;
        }

        private Quaternion GroundRotation() {
            if (currentMotionSource != null && currentMotionSource.HasScheduledPose)
                return currentMotionSource.ScheduledRotation;
            return currentGroundBody != null ? currentGroundBody.rotation : currentTransfer.transform.rotation;
        }

        private void CheckGround() {
            if (lastCheckTime == Time.fixedTime)
                return;
            lastCheckTime = Time.fixedTime;

            Vector3 up = Up;
            GetFootprint(up, out Vector3 feet, out Vector3 halfExtents, out Quaternion orientation);
            Vector3 origin = feet + up * probeStartAboveFeet;
            float distance = probeDistance;
            bool hit = characterCollider.gameObject.scene.GetPhysicsScene().BoxCast(
                origin, halfExtents, -up, out RaycastHit groundHit,
                orientation, distance, groundMask, QueryTriggerInteraction.Ignore);

            Grounded = hit && Vector3.Angle(groundHit.normal, up) <= maxGroundAngle;
            GroundCollider = Grounded ? groundHit.collider : null;
            GroundNormal = Grounded ? groundHit.normal : up;
            UpdateGroundTransfer();

#if UNITY_EDITOR
            Debug.DrawRay(origin, -up * distance, Grounded ? Color.green : Color.red);
#endif
        }

        /// <summary>
        /// A thin box at the bottom of the character, measured along <paramref name="up"/>. Uses the box
        /// collider's own dimensions so the footprint stays correct while the body is tilted mid-turn.
        /// </summary>
        private void GetFootprint(Vector3 up, out Vector3 feet, out Vector3 halfExtents, out Quaternion orientation) {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(transform.up, up);
            orientation = Quaternion.LookRotation(forward, up);

            Vector3 center;
            Vector3 size;
            if (characterCollider is BoxCollider box) {
                center = transform.TransformPoint(box.center);
                size = Vector3.Scale(box.size, transform.lossyScale);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            } else {
                Bounds bounds = characterCollider.bounds;
                center = bounds.center;
                size = bounds.size;
            }
            // Distance from the centre to the lowest point of the (possibly tilted) box along -up.
            Vector3 localDown = Quaternion.Inverse(transform.rotation) * -up;
            float support = 0.5f * (Mathf.Abs(localDown.x) * size.x + Mathf.Abs(localDown.y) * size.y + Mathf.Abs(localDown.z) * size.z);
            feet = center - up * support;
            halfExtents = new Vector3(size.x * 0.5f, 0.01f, size.z * 0.5f);
        }

        private void UpdateGroundTransfer() {
            GroundMotionTransfer transfer = GroundCollider != null
                ? GroundCollider.GetComponentInParent<GroundMotionTransfer>()
                : null;
            if (transfer != null && !transfer.isActiveAndEnabled)
                transfer = null;
            if (transfer == currentTransfer)
                return;

            if (groundReposition != null)
                groundReposition.Repositioning -= FollowGroundReposition;
            currentTransfer = transfer;
            currentGroundBody = transfer != null ? transfer.GetComponentInParent<Rigidbody>() : null;
            currentMotionSource = currentGroundBody != null ? currentGroundBody.GetComponent<IPhysicsMotionSource>() : null;
            groundReposition = currentGroundBody != null ? currentGroundBody.GetComponent<RigidbodyReposition>() : null;
            if (groundReposition != null)
                groundReposition.Repositioning += FollowGroundReposition;
            if (transfer != null) {
                previousGroundPosition = GroundPosition();
                previousGroundRotation = GroundRotation();
            }
        }
    }
}
