using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>A fixed pool of AudioSources ("voices") that plays one-shots through the SFX mixer group.</summary>
    internal sealed class SfxPlayer {
        readonly GameFeelSettings.AudioTuning tuning;
        readonly AudioSource[] voices;
        readonly Transform[] voiceTransforms;
        readonly float[] startedAt;

        public SfxPlayer(Transform parent, GameFeelSettings.AudioTuning tuning) {
            this.tuning = tuning;
            int count = Mathf.Max(1, tuning.voices);
            voices = new AudioSource[count];
            voiceTransforms = new Transform[count];
            startedAt = new float[count];
            for (int i = 0; i < count; i++) {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(parent, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.outputAudioMixerGroup = tuning.mixerGroup;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.dopplerLevel = tuning.doppler;
                voices[i] = source;
                voiceTransforms[i] = go.transform;
                startedAt[i] = float.NegativeInfinity;
            }
        }

        public void Play(SfxSound sound, bool spatial, Vector3 position, float volume, float pitch) {
            if (sound == null || !sound.HasClips)
                return;
            float now = Time.unscaledTime;
            if (now - sound.lastPlayTime < sound.minInterval)
                return;
            AudioClip clip = sound.PickClip();
            if (clip == null)
                return;
            sound.lastPlayTime = now;
            float finalVolume = sound.volume * volume * (1f - Random.value * sound.volumeJitter);
            float semitones = sound.pitchJitterSemitones > 0f ? Random.Range(-sound.pitchJitterSemitones, sound.pitchJitterSemitones) : 0f;
            float finalPitch = sound.pitch * pitch * GameSfx.Semitones(semitones);
            PlayClip(clip, spatial, position, finalVolume, finalPitch, sound.minDistance, sound.maxDistance);
        }

        public void PlayClip(AudioClip clip, bool spatial, Vector3 position, float volume, float pitch, float minDistance = 0f, float maxDistance = 0f) {
            if (clip == null || volume <= 0f)
                return;
            int index = NextVoice();
            AudioSource source = voices[index];
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = minDistance > 0f ? minDistance : tuning.minDistance;
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance > 0f ? maxDistance : tuning.maxDistance);
            if (spatial)
                voiceTransforms[index].position = position;
            startedAt[index] = Time.unscaledTime;
            source.Play();
        }

        /// <summary>A free voice, or the one that started longest ago.</summary>
        int NextVoice() {
            int oldest = 0;
            for (int i = 0; i < voices.Length; i++) {
                if (!voices[i].isPlaying)
                    return i;
                if (startedAt[i] < startedAt[oldest])
                    oldest = i;
            }
            return oldest;
        }
    }
}
