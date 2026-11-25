#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Sticky ground (honey): a solid slab with a second, trigger collider over its top face. While the
    /// local character stands in that trigger its walking pace is cut to <see cref="keep"/> of normal
    /// (a push against its own steering, every physics step), on the ground only: a jump is unaffected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickyZone : MonoBehaviour {
        [Tooltip("Share of normal walking speed kept on this surface (0.5 = half speed).")]
        [Range(0.05f, 1f)] public float keep = 0.55f;
        [Tooltip("How far above the slab's top face the sticky field reaches (metres).")]
        [Min(0.1f)] public float reach = 1.8f;
        [Tooltip("Optional bubbles or drips; played while the local character is stuck in it.")]
        public ParticleSystem bubbles;

        /// <summary>Sets the speed share and fits the trigger to the slab's current scale (call after scaling it).</summary>
        public void Configure(float speedShare) {
            keep = Mathf.Clamp(speedShare, 0.05f, 1f);
            FitTrigger();
        }

        void FitTrigger() {
            foreach (BoxCollider box in GetComponents<BoxCollider>()) {
                if (!box.isTrigger) continue;
                float scale = Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.y));
                box.center = new Vector3(0f, 0.5f + reach * 0.5f / scale, 0f);
                box.size = new Vector3(1f, reach / scale, 1f);
            }
        }

#if !UNITY_SERVER
        /// <summary>Raised on the client when the local character steps into honey (games add the sound).</summary>
        public static event System.Action<StickyZone> Entered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Entered = null;

        void Start() => FitTrigger();

        void OnTriggerEnter(Collider other) {
            if (LocalBody.TryGet(other, out _)) Entered?.Invoke(this);
        }

        void OnTriggerStay(Collider other) {
            if (!LocalBody.TryGet(other, out CharacterMovement movement) || !movement.Grounded) return;
            // Steering brings the walker to MoveSpeed along its facing; push back the share honey takes away.
            movement.AddPush(-movement.Facing * (movement.MoveSpeed * (1f - keep)));
        }
#endif
    }
}
