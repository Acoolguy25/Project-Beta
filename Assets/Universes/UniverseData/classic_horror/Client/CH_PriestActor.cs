using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The priest as a client-only puppet: the same model and walk as the real one, posed by cutscenes and by the
    /// apparitions that are not really there. It never collides and is never networked.
    /// </summary>
    public sealed class CH_PriestActor : MonoBehaviour {
        public Animator animator;
        [Tooltip("His heavy tread, played at his feet by the walk animation's footstep events.")]
        public SfxSound footstep = new();
        [Tooltip("How far below his mark he starts when he rises out of the water.")]
        [SerializeField, Min(0f)] float riseDepth = 2.2f;
        [SerializeField, Min(0.1f)] float walkAnimationSpeed = 2.2f;
        [SerializeField, Min(0.1f)] float lungeAnimationSpeed = 7f;

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
            Visible = false;
        }

        /// <summary>Stands him at <paramref name="position"/>, facing <paramref name="facing"/>.</summary>
        public void Place(Vector3 position, Vector3 facing, float animationSpeed = 0f) {
            if (!Visible) {
                foreach (Renderer part in renderers) part.enabled = true;
                Visible = true;
            }
            facing.y = 0f;
            transform.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : transform.rotation);
            if (animator == null) return;
            animator.SetBool(GroundedId, true);
            animator.SetFloat(SpeedId, animationSpeed);
            animator.SetFloat(MotionSpeedId, animationSpeed > 0f ? 1f : 0f);
        }

        // Animation events from the shared walk clips (the real monster's CharacterAnimator handles them there).
        public void OnFootstep(AnimationEvent animationEvent) {
            if (Visible && animationEvent.animatorClipInfo.weight > 0.5f && footstep.HasClips) GameSfx.PlayAt(footstep, transform.position);
        }

        public void OnLand(AnimationEvent animationEvent) { }

        /// <summary>Poses him for a cutscene shot <paramref name="progress"/> (0 to 1) of the way through.</summary>
        public void Animate(CH_CutsceneSet.Shot shot, float progress) {
            if (shot.actorMark == null || shot.actorPose == CH_CutsceneSet.ActorPose.Hidden) { if (Visible) Hide(); return; }
            Vector3 start = shot.actorMark.position;
            Vector3 end = shot.actorTarget != null ? shot.actorTarget.position : start;
            Vector3 facing = shot.actorTarget != null && (end - start).sqrMagnitude > 0.01f ? end - start : shot.actorMark.forward;
            switch (shot.actorPose) {
                case CH_CutsceneSet.ActorPose.Stand:
                    Place(start, facing);
                    break;
                case CH_CutsceneSet.ActorPose.Walk:
                    Place(Vector3.Lerp(start, end, progress), facing, walkAnimationSpeed);
                    break;
                case CH_CutsceneSet.ActorPose.Rise:
                    float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 1.6f));
                    Place(Vector3.Lerp(start, end, progress) + Vector3.down * (riseDepth * (1f - rise)), facing);
                    break;
                case CH_CutsceneSet.ActorPose.Lunge:
                    float lunge = Mathf.Clamp01((progress - 0.35f) / 0.4f);
                    Place(Vector3.Lerp(start, end, lunge * lunge), facing, lunge > 0f && lunge < 1f ? lungeAnimationSpeed : 0f);
                    break;
            }
        }
    }
}
