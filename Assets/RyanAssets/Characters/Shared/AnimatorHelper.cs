using UnityEngine;

namespace RyanAssets.Characters
{
    public class AnimationHelper: MonoBehaviour
    {
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;
        private void OnFootstep(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight > 0.5f && FootstepAudioClips.Length > 0) {
                int index = Random.Range(0, FootstepAudioClips.Length);
                var clip = FootstepAudioClips[index];
                var audioGO = new GameObject("FootstepAudio");
                audioGO.transform.position = transform.position;
                var source = audioGO.AddComponent<AudioSource>();
                source.clip = clip;
                source.volume = FootstepAudioVolume;
                source.pitch = 1.0f; //_input.sprint ? (1.25f) : 1.0f;
                source.Play();
                Destroy(audioGO, clip.length / source.pitch);
            }
        }
        private void OnLand(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight > 0.5f) {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.position, FootstepAudioVolume * 8f);
            }
        }
    }
}