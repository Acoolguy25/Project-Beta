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
        public void SetRagdoll(bool enabled) {
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
