using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// A character as a client-only puppet: a model and its walk, posed by cutscenes (the priest, Mara) and by the
    /// apparitions that are not really there. It never collides and is never networked.
    /// </summary>
    public sealed class CH_Puppet : MonoBehaviour {
        public Animator animator;
        [Tooltip("Its tread, played at its feet by the walk animation's footstep events.")]
        public SfxSound footstep = new();
        [Tooltip("A dim light on the face, so it reads in the dark; on only while the puppet is visible.")]
        public Light faceLight;
        [Tooltip("How far below its mark it starts when it rises out of the water, and sinks when it is dragged under.")]
        [SerializeField, Min(0f)] float riseDepth = 2.2f;
        [SerializeField, Min(0.1f)] float walkAnimationSpeed = 2.2f;
        [SerializeField, Min(0.1f)] float lungeAnimationSpeed = 7f;
        [Tooltip("Fraction of a Sink shot it stands before it is pulled toward its target and under.")]
        [SerializeField, Range(0f, 1f)] float sinkStart = 0.15f;
        [Tooltip("Fraction of a Rise shot by which it is fully out of the water (it drifts toward its target for the rest).")]
        [SerializeField, Range(0.1f, 1f)] float riseEnd = 0.625f;
        [Tooltip("Fraction of a Lunge shot it holds still, gathering, before it springs.")]
        [SerializeField, Range(0f, 1f)] float lungeStart = 0.35f;
        [Tooltip("Fraction of a Lunge shot the spring itself takes.")]
        [SerializeField, Range(0.05f, 1f)] float lungeLength = 0.4f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        Renderer[] renderers;

        public bool Visible { get; private set; }

        void Awake() {
            renderers = GetComponentsInChildren<Renderer>(true);
            Hide();
        }

        public void Hide() {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer part in renderers) part.enabled = false;
            if (faceLight != null) faceLight.enabled = false;
            Visible = false;
        }

        /// <summary>Stands it at <paramref name="position"/>, facing <paramref name="facing"/>.</summary>
        public void Place(Vector3 position, Vector3 facing, float animationSpeed = 0f) {
            if (!Visible) {
                foreach (Renderer part in renderers) part.enabled = true;
                if (faceLight != null) faceLight.enabled = true;
                Visible = true;
            }
            facing.y = 0f;
            transform.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : transform.rotation);
            if (animator == null) return;
            animator.SetBool(GroundedId, true);
            animator.SetFloat(SpeedId, animationSpeed);
            animator.SetFloat(MotionSpeedId, animationSpeed > 0f ? 1f : 0f);
        }

        // Animation events from the shared walk clips (a networked character's CharacterAnimator handles them there).
        public void OnFootstep(AnimationEvent animationEvent) {
            if (Visible && animationEvent.animatorClipInfo.weight > 0.5f && footstep.HasClips) GameSfx.PlayAt(footstep, transform.position);
        }

        public void OnLand(AnimationEvent animationEvent) { }

        /// <summary>Poses it on <paramref name="mark"/> (moving toward <paramref name="target"/>) <paramref name="progress"/> (0 to 1) through a shot.</summary>
        public void Animate(Transform mark, Transform target, CH_CutsceneSet.ActorPose pose, float progress) {
            if (mark == null || pose == CH_CutsceneSet.ActorPose.Hidden) { if (Visible) Hide(); return; }
            Vector3 start = mark.position;
            Vector3 end = target != null ? target.position : start;
            Vector3 facing = target != null && (end - start).sqrMagnitude > 0.01f ? end - start : mark.forward;
            switch (pose) {
                case CH_CutsceneSet.ActorPose.Stand:
                    Place(start, mark.forward);
                    break;
                case CH_CutsceneSet.ActorPose.Walk:
                    Place(Vector3.Lerp(start, end, progress), facing, walkAnimationSpeed);
                    break;
                case CH_CutsceneSet.ActorPose.Rise:
                    float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / riseEnd));
                    Place(Vector3.Lerp(start, end, progress) + Vector3.down * (riseDepth * (1f - rise)), facing);
                    break;
                case CH_CutsceneSet.ActorPose.Lunge:
                    float lunge = Mathf.Clamp01((progress - lungeStart) / lungeLength);
                    Place(Vector3.Lerp(start, end, lunge * lunge), facing, lunge > 0f && lunge < 1f ? lungeAnimationSpeed : 0f);
                    break;
                case CH_CutsceneSet.ActorPose.Sink:
                    // Facing what pulls it, dragged backwards off its feet and under.
                    float pulled = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sinkStart, 1f, progress));
                    Place(Vector3.Lerp(start, end, pulled) + Vector3.down * (riseDepth * pulled * pulled), mark.forward, pulled > 0f && pulled < 1f ? walkAnimationSpeed : 0f);
                    break;
            }
        }
    }
}
