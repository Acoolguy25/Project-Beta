using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace RyanAssets.Characters.Shared {
    public class IRagdoll: MonoBehaviour { public virtual bool disableOnRagdoll => false; }
    public class RagdollCharacter : MonoBehaviour {
        [SerializeField]
        private bool RagdollEnabled;
        [SerializeField]
        private bool RagdollOnDeath = true;
        [SerializeField]
        private LayerMask ExcludeColliderMask;
        private readonly static System.Type[] disableObjects = {  typeof(Animator), typeof(Collider), typeof(CharacterAnimator) };
        private GameCharacter GameCharacter;
        // Ragdoll internals
        private Collider mainCollider;
        private Rigidbody[] rigidBodies;
        private Collider[] colliders;
        private Joint[] joints;
        void Start() {
            GameCharacter = GetComponent<GameCharacter>();
            mainCollider = GetComponent<Collider>();

            RagdollInit();
            if (RagdollOnDeath) {
                GameCharacter.OnDied += (_, _) => {
                    if (!gameObject)
                        return;
                    SetRagdoll(true);
                };
                GameCharacter.OnRevive += () => {
                    if (!gameObject)
                        return;
                    SetRagdoll(false);
                };
                if (GameCharacter.IsDead) // If already died
                    RagdollEnabled = true;
            }
            SetRagdoll(RagdollEnabled);
        }
#if UNITY_EDITOR
        void OnValidate() {
            if (!didStart)
                return;
            SetRagdoll(RagdollEnabled);
        }
#endif
        void RagdollInit() {
            mainCollider ??= GetComponent<Collider>();
            rigidBodies = transform.GetComponentsInChildren<Rigidbody>(true).Where(rb => rb.transform != transform).ToArray();
            colliders = transform.GetChild(1).GetComponentsInChildren<Collider>(true);
            joints = GetComponentsInChildren<Joint>(true);
            foreach (Collider collider in colliders) {
                collider.excludeLayers = ExcludeColliderMask;
            }
            foreach (Rigidbody rb in rigidBodies) {
                rb.excludeLayers = ExcludeColliderMask;
            }
        }
        void DisableExternalObjects(bool disabled) {
            foreach (System.Type type in disableObjects) {
                var component = GetComponent(type);

                if (component is Animator animator) {
                    if (disabled) {
                        animator.Rebind();
                        animator.Update(0f);
                    }
                }

                if (component is Behaviour behaviour) {
                    behaviour.enabled = !disabled;
                }

                if (component is Rigidbody rb) {
                    rb.isKinematic = disabled;
                }

            }
            foreach (var ragdoll in GetComponentsInChildren<MonoBehaviour>(true).OfType<IRagdoll>()) {
                if (ragdoll.disableOnRagdoll)
                    ragdoll.enabled = !disabled;
                else
                    ragdoll.enabled = disabled;
            }
        }
        public bool IsRagdoll => RagdollEnabled;

        /// <summary>
        /// Gravity (m/s², direction included) the limp body falls with, instead of the world's
        /// <see cref="Physics.gravity"/>: a character on a wall, or with heavier gravity, stays in its own frame.
        /// </summary>
        public Vector3? Gravity { get; set; }

        void FixedUpdate() {
            if (!RagdollEnabled || Gravity is not Vector3 gravity || rigidBodies == null)
                return;
            Vector3 extra = gravity - Physics.gravity;
            foreach (Rigidbody body in rigidBodies)
                if (!body.isKinematic)
                    body.AddForce(extra, ForceMode.Acceleration);
        }

        /// <summary>
        /// Adds <paramref name="velocity"/> to every ragdoll body, for a shove or a fling, and sets the
        /// whole body spinning at <paramref name="angularVelocity"/> (rad/s) about its centre of mass.
        /// </summary>
        public void AddVelocity(Vector3 velocity, Vector3 angularVelocity = default) {
            if (rigidBodies == null)
                return;
            Vector3 centre = Vector3.zero;
            float mass = 0f;
            foreach (Rigidbody body in rigidBodies) {
                centre += body.worldCenterOfMass * body.mass;
                mass += body.mass;
            }
            if (mass > 0f) centre /= mass;
            foreach (Rigidbody body in rigidBodies) {
                if (body.isKinematic)
                    continue;
                body.linearVelocity += velocity + Vector3.Cross(angularVelocity, body.worldCenterOfMass - centre);
                body.angularVelocity += angularVelocity;
            }
        }

        public void SetRagdoll(bool enabled) {
            if (rigidBodies == null)
                RagdollInit();
            RagdollEnabled = enabled;
            mainCollider.enabled = !enabled;

            foreach (Joint joint in joints) {
                joint.enableCollision = enabled;
                //joint.breakForce = enabled ? Mathf.Infinity : 0;
                //joint.breakTorque = enabled ? Mathf.Infinity : 0;
            }
            foreach (Collider collider in colliders) {
                collider.enabled = enabled;
            }
            foreach (Rigidbody rigidbody in rigidBodies) {
                rigidbody.isKinematic = !enabled;
                if (!rigidbody.isKinematic) {
                    rigidbody.linearVelocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                }
                rigidbody.detectCollisions = enabled;
                rigidbody.useGravity = enabled;
            }
            DisableExternalObjects(enabled);
        }
    }
}
