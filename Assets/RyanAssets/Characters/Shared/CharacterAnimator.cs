using FishNet.Object;
using UnityEngine;
using UnityEngine.Audio;

namespace RyanAssets.Characters.Shared
{
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimator: NetworkBehaviour {
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;

        [SerializeField]
        public bool GroundCheck;
        public bool Grounded;
        public static float JumpThreshold = 0.4f;
        public static float SpeedThreshold = 0.125f;

        Animator _animator;
        LayerMask GroundMask;
        Collider _collider;
        AudioSource _footStepSource;

        private Vector3 prevPosition;
        private float jumpStart = float.MinValue;
        public void OnFootstep(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight > 0.5f && FootstepAudioClips.Length > 0) {
                int index = Random.Range(0, FootstepAudioClips.Length);
                var clip = FootstepAudioClips[index];

                //var audioGO = new GameObject("FootstepAudio");
                //audioGO.transform.position = transform.position;
                //var source = audioGO.AddComponent<AudioSource>();
                //source.clip = clip;
                //source.volume = FootstepAudioVolume;
                //source.pitch = 1.0f; //_input.sprint ? (1.25f) : 1.0f;
                //source.Play();
#if AUDIO_ENABLED
                _footStepSource.PlayOneShot(clip);
#endif
                //Destroy(audioGO, clip.length / source.pitch);
            }
        }
        public void OnLand(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight > 0.5f) {
                _footStepSource.PlayOneShot(LandingAudioClip);
            }
        }
        void Start(){
            _animator = GetComponent<Animator>();
            _collider = GetComponent<Collider>();
            _footStepSource = GetComponent<AudioSource>();

            GroundMask = ~LayerMask.GetMask(transform.tag); // Anything but itself is "ground"
            _animator.SetBool("Grounded", true);
            _animator.SetBool("FreeFall", false);
            _animator.SetBool("Jump", false);
            _animator.SetFloat("Speed", 0f);
        }
        void FixedUpdate(){
            if (!IsController)
                return;
            if (GroundCheck) {
                FixedUpdateGround();
                _animator.SetBool("Grounded", Grounded);
                _animator.SetBool("FreeFall", !Grounded);
            }
            Vector3 velocity = GetVelocity();
            float newSpeed = Mathf.Lerp(_animator.GetFloat("Speed"), velocity.magnitude * SpeedThreshold, 1f);
            _animator.SetFloat("Speed", newSpeed);
            _animator.SetFloat("MotionSpeed", newSpeed);
            _animator.SetBool("Jump", (Time.fixedTime - jumpStart) < 0.01f);
            //_animator.SetBool("Jump", false);
        }
        public void Jump() {
            _animator.SetBool("Jump", true);
            jumpStart = Time.fixedTime;
        }
        private Vector3 GetVelocity() {
            Vector3 velocity = (transform.position - prevPosition) / Time.fixedDeltaTime;
            prevPosition = transform.position;
            return velocity;
        }
        private void FixedUpdateGround() {
            Bounds b = _collider.bounds;
            float upOff = 0.03f;
            Grounded = Physics.BoxCast(
                b.center + Vector3.down * (b.extents.y - upOff),
                new Vector3(b.extents.x, 0.01f, b.extents.z),
                Vector3.down,
                out _,
                Quaternion.identity,
                0.085f,
                GroundMask,
                QueryTriggerInteraction.Ignore
            );

#if UNITY_EDITOR
            // Four bottom corners of the box for debug purposes
            Vector3[] origins = new Vector3[]{
                new Vector3(b.min.x, b.min.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z),
            };
            foreach (Vector3 origin in origins) {
                Vector3 targetOrigin = origin + Vector3.up * upOff;
                bool hit = Physics.Raycast(
                    targetOrigin,
                    Vector3.down,
                    out RaycastHit rayHit,
                    0.085f,
                    GroundMask,
                    QueryTriggerInteraction.Ignore
                );

                // DEBUG RAY
                Debug.DrawRay(
                    targetOrigin,
                    Vector3.down * 0.05f,
                    hit ? Color.green : Color.red
                );
            }
#endif
        }
    }
}
