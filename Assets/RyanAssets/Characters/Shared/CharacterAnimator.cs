using FishNet.Object;
using UnityEngine;
using UnityEngine.Audio;

namespace RyanAssets.Characters.Shared
{
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class CharacterAnimator: NetworkBehaviour {
        public event System.Action LethalAttackStarted, LethalAttackEnded;
        public bool LethalAttackEnabled { get; set; } = false;
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;

        [Header("Locomotion Audio")]
        [SerializeField, Min(1f)] private float footstepPaceMultiplier = 1f;
        [SerializeField, Min(0f)] private float duplicateSoundWindow = 0.08f;

        public float FootstepPaceMultiplier {
            get => footstepPaceMultiplier;
            set => footstepPaceMultiplier = Mathf.Max(1f, value);
        }

        public static float JumpThreshold = 0.4f;
        public static float SpeedThreshold = 0.125f;

        Animator _animator;
        GroundPhysics _groundPhysics;
        AudioSource _footStepSource;

        private Vector3 prevPosition;
        private bool hasPreviousPosition;
        private float jumpStart = float.MinValue;
        private float _lastFootstepTime = float.NegativeInfinity;
        private float _lastLandingTime = float.NegativeInfinity;
        public void OnFootstep(AnimationEvent animationEvent) {
            if (FootstepAudioClips == null || FootstepAudioClips.Length == 0)
                return;

            // Walk/run clips can both contribute less than half of the blend while still
            // producing a valid foot plant. Use time-based de-duplication instead of clip
            // weight so blended locomotion never suppresses every footstep event.
            if (Time.time - _lastFootstepTime < duplicateSoundWindow)
                return;

            _lastFootstepTime = Time.time;
            PlayOneShot(FootstepAudioClips[Random.Range(0, FootstepAudioClips.Length)]);
        }
        public void OnLand(AnimationEvent animationEvent) {
            if (Time.time - _lastLandingTime < duplicateSoundWindow)
                return;

            _lastLandingTime = Time.time;
            PlayOneShot(LandingAudioClip);
        }
        private void PlayOneShot(AudioClip clip) {
            if (clip == null)
                return;

            if (_footStepSource == null)
                _footStepSource = GetComponent<AudioSource>();

            if (_footStepSource != null && _footStepSource.isActiveAndEnabled)
                _footStepSource.PlayOneShot(clip);
        }
        public void OnLethalAttackStart(AnimationEvent animationEvent) {
            LethalAttackEnabled = true;
            LethalAttackStarted?.Invoke();
        }
        public void OnLethalAttackEnd(AnimationEvent animationEvent) {
            LethalAttackEnabled = false;
            LethalAttackEnded?.Invoke();
        }
        void Start(){
            _animator = GetComponent<Animator>();
            _groundPhysics = GetComponent<GroundPhysics>();
            _footStepSource = GetComponent<AudioSource>();

            // Animation events drive locomotion audio. First-person games such as
            // classic_horror may keep the local mesh outside every camera, so renderer-
            // based culling must not stop animation updates or their audio events.
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _animator.SetBool("Grounded", true);
            _animator.SetBool("FreeFall", false);
            _animator.SetBool("Jump", false);
            _animator.SetFloat("Speed", 0f);
        }
        void FixedUpdate(){
            if (!IsController)
                return;
            if (_groundPhysics != null) {
                _animator.SetBool("Grounded", _groundPhysics.Grounded);
                _animator.SetBool("FreeFall", !_groundPhysics.Grounded);
            }
            Vector3 velocity = GetVelocity();
            float newSpeed = Mathf.Lerp(_animator.GetFloat("Speed"), velocity.magnitude * SpeedThreshold, 1f);
            _animator.SetFloat("Speed", newSpeed);
            _animator.SetFloat("MotionSpeed", newSpeed * footstepPaceMultiplier);
            _animator.SetBool("Jump", (Time.fixedTime - jumpStart) < JumpThreshold);
            //_animator.SetBool("Jump", false);
        }
        public void Jump() {
            _animator.SetBool("Jump", true);
            _animator.CrossFadeInFixedTime("Lowerbody.JumpStart", 0.05f, 0, 0f);
            jumpStart = Time.fixedTime;
        }
        private Vector3 GetVelocity() {
            Vector3 position = transform.position;
            Vector3 velocity = hasPreviousPosition
                ? (position - prevPosition) / Time.fixedDeltaTime
                : Vector3.zero;
            prevPosition = position;
            hasPreviousPosition = true;
            if (_groundPhysics != null)
                velocity -= _groundPhysics.InheritedGroundVelocity;
            velocity.y = 0f; // Vertical movement does not drive locomotion animation.
            return velocity;
        }
    }
}
