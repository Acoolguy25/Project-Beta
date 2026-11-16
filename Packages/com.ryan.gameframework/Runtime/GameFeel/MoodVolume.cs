using UnityEngine;
using UnityEngine.Rendering;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Gives a scene one of the kit's post-processing looks (<see cref="MoodPreset"/>), as a global
    /// URP Volume. Either drop this component on an object in a client scene or prefab and pick the
    /// preset (it previews in the Editor), or switch looks from code with a crossfade:
    /// <code>
    /// MoodVolume.Apply(MoodPreset.Night, 2f);   // e.g. when the round turns to night
    /// MoodVolume.Clear(1f);                     // back to the scene's own look
    /// </code>
    /// The main camera must have Post Processing enabled (the shared client camera does).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Volume))]
    [DisallowMultipleComponent]
    public sealed class MoodVolume : MonoBehaviour {
        [SerializeField] MoodPreset preset = MoodPreset.ArcadeBright;
        [SerializeField, Range(0f, 1f)] float weight = 1f;
        [Tooltip("Volume priority; higher wins over the scene's other global volumes.")]
        [SerializeField] float priority = 10f;

        Volume volume;

        public MoodPreset Preset {
            get => preset;
            set {
                preset = value;
                Bind();
            }
        }

        /// <summary>Crossfades the kit's global mood to <paramref name="preset"/> over <paramref name="seconds"/>.</summary>
        public static void Apply(MoodPreset preset, float seconds = 1f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner == null)
                return;
            VolumeProfile profile = runner.Settings.Mood(preset);
            if (profile == null) {
                Debug.LogError($"GameFeel: no volume profile for mood {preset}. Run Ryan > Game Feel > Rebuild.");
                return;
            }
            runner.Moods.Apply(preset, profile, seconds);
        }

        /// <summary>Fades the kit's global mood out over <paramref name="seconds"/>.</summary>
        public static void Clear(float seconds = 1f) => GameFeelRunner.Existing?.Moods.Clear(seconds);

        /// <summary>The mood set through <see cref="Apply"/>, if any.</summary>
        public static MoodPreset? Active => GameFeelRunner.Existing?.Moods.Active;

        void OnEnable() => Bind();

        void OnValidate() {
            if (isActiveAndEnabled)
                Bind();
        }

        void Bind() {
            if (volume == null)
                volume = GetComponent<Volume>();
            GameFeelSettings settings = GameFeelSettings.Active;
            if (volume == null || settings == null)
                return;
            volume.isGlobal = true;
            volume.priority = priority;
            volume.weight = weight;
            volume.sharedProfile = settings.Mood(preset);
        }
    }
}
