using UnityEngine;

namespace RyanAssets.UI.Feel {
    /// <summary>
    /// Plays the shared UI sounds (button hover and click from <see cref="UITheme"/>) through one 2D
    /// AudioSource on the theme's mixer group. Created on first use; does nothing outside Play mode
    /// or on a dedicated server.
    /// </summary>
    public static class UISound {
        static AudioSource source;
        static float lastHover = float.NegativeInfinity;

        /// <summary>The theme's hover sound (rate-limited so sweeping across buttons does not rattle).</summary>
        public static void Hover() {
            UITheme theme = UITheme.Active;
            float now = Time.unscaledTime;
            if (now - lastHover < theme.hoverSoundInterval)
                return;
            lastHover = now;
            Play(theme.buttonHoverSound, theme.buttonHoverVolume);
        }

        /// <summary>The theme's click sound.</summary>
        public static void Click() {
            UITheme theme = UITheme.Active;
            Play(theme.buttonClickSound, theme.buttonClickVolume);
        }

        /// <summary>Any clip as a UI sound.</summary>
        public static void Play(AudioClip clip, float volume = 1f) {
#if !UNITY_SERVER
            if (clip == null || volume <= 0f || !Application.isPlaying)
                return;
            if (source == null) {
                var go = new GameObject("UI Sound");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.outputAudioMixerGroup = UITheme.Active.soundGroup;
            }
            source.PlayOneShot(clip, volume);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            source = null;
            lastHover = float.NegativeInfinity;
        }
    }
}
