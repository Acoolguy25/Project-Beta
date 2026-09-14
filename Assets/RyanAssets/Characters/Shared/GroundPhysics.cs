using FishNet.Object;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class GroundPhysics : NetworkBehaviour {
        private const float ProbeStartAboveFeet = 0.03f;
        private const float ProbeDistance = 0.085f;

        public bool Grounded { get; private set; }
        public Collider GroundCollider { get; private set; }
        // Consumed by character movement as part of its target velocity. Moving a dynamic
        // body with MovePosition teleports it and resets its interpolation every step.
        public Vector3 GroundVelocity => pendingGroundDisplacement / Time.fixedDeltaTime;
        // The displacement that reached the character in the last completed physics step.
        public Vector3 InheritedGroundVelocity => lastSyncTime == Time.fixedTime
            ? appliedGroundDisplacement / Time.fixedDeltaTime
            : Vector3.zero;

        private Collider characterCollider;
        private Rigidbody characterBody;
        private GroundMotionTransfer currentTransfer;
        private Rigidbody currentGroundBody;
        private Vector3 previousGroundPosition;
        private Quaternion previousGroundRotation;
        private float lastCheckTime = float.NegativeInfinity;
        private float lastSyncTime = float.NegativeInfinity;
        private Vector3 pendingGroundDisplacement;
        private Vector3 appliedGroundDisplacement;
        private int groundMask;

        private void Awake() {
            characterCollider = GetComponent<Collider>();
            characterBody = GetComponent<Rigidbody>();
            groundMask = ~LayerMask.GetMask("Character", "LocalCharacter");
        }

        private void FixedUpdate() { 
            if (IsController)
                CheckGround();
        }

        public void SyncWithGround() {
            // Expose the prior step's transfer to animation, which samples the resulting pose now.
            appliedGroundDisplacement = pendingGroundDisplacement;
            pendingGroundDisplacement = Vector3.zero;
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

            // Horizontal movement consumes GroundVelocity; preserve vertical platform
            // motion separately so the movement controller can still apply gravity/jumps.
            float verticalVelocityChange = (pendingGroundDisplacement.y - appliedGroundDisplacement.y) / Time.fixedDeltaTime;
            characterBody.linearVelocity += Vector3.up * verticalVelocityChange;

            if (currentTransfer.transferRotation && groundRotation != previousGroundRotation)
                characterBody.MoveRotation(rotationDelta * characterBody.rotation);

            previousGroundPosition = groundPosition;
            previousGroundRotation = groundRotation;
        }

        public void ResetGround() {
            Grounded = false;
            GroundCollider = null;
            currentTransfer = null;
            currentGroundBody = null;
            lastCheckTime = float.NegativeInfinity;
            lastSyncTime = float.NegativeInfinity;
            pendingGroundDisplacement = Vector3.zero;
            appliedGroundDisplacement = Vector3.zero;
        }

        private Vector3 GroundPosition() => currentGroundBody != null
            ? currentGroundBody.position
            : currentTransfer.transform.position;

        private Quaternion GroundRotation() => currentGroundBody != null
            ? currentGroundBody.rotation
            : currentTransfer.transform.rotation;

        private void CheckGround() {
            if (lastCheckTime == Time.fixedTime)
                return;
            lastCheckTime = Time.fixedTime;

            Bounds bounds = characterCollider.bounds;
            Vector3 origin = bounds.center + Vector3.down * (bounds.extents.y - ProbeStartAboveFeet);
            Vector3 halfExtents = new(bounds.extents.x, 0.01f, bounds.extents.z);
            bool hit = characterCollider.gameObject.scene.GetPhysicsScene().BoxCast(
                origin, halfExtents, Vector3.down, out RaycastHit groundHit,
                Quaternion.identity, ProbeDistance, groundMask, QueryTriggerInteraction.Ignore);

            Grounded = hit && groundHit.normal.y > 0.5f;
            GroundCollider = Grounded ? groundHit.collider : null;
            GroundMotionTransfer transfer = GroundCollider != null
                ? GroundCollider.GetComponentInParent<GroundMotionTransfer>()
                : null;
            if (transfer != null && !transfer.isActiveAndEnabled)
                transfer = null;

            if (transfer != currentTransfer) {
                currentTransfer = transfer;
                currentGroundBody = transfer != null ? transfer.GetComponentInParent<Rigidbody>() : null;
                if (transfer != null) {
                    previousGroundPosition = GroundPosition();
                    previousGroundRotation = GroundRotation();
                }
            }

#if UNITY_EDITOR
            Debug.DrawRay(origin, Vector3.down * ProbeDistance, Grounded ? Color.green : Color.red);
#endif
        }
    }
}
