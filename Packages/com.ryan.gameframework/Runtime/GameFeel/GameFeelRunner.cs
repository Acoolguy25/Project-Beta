using UnityEngine;
using UnityEngine.Rendering;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// The kit's one scene object: created on first use (or at startup for character feel), kept
    /// across scene loads, and owner of every pool. Game code never touches it; it calls the static
    /// entry points (<see cref="GameSfx"/>, <see cref="GameFx"/>, <see cref="CameraShake"/>, ...),
    /// which do nothing outside Play mode.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(1000)] // after gameplay and camera rigs have moved things this frame
    internal sealed class GameFeelRunner : MonoBehaviour {
        static GameFeelRunner instance;
        static bool quitting;

        internal GameFeelSettings Settings { get; private set; }
        internal SfxPlayer Sfx { get; private set; }
        internal CameraShaker Shake { get; private set; }
        internal FxPool Fx { get; private set; }
        internal FloatingTextPool Texts { get; private set; }
        internal PunchAnimator Punches { get; private set; }
        internal MoodBlender Moods { get; private set; }
        internal CharacterFeelSystem Characters { get; private set; }
        /// <summary>The main camera as of this frame's LateUpdate (may be null between camera switches).</summary>
        internal Camera MainCamera { get; private set; }

        ScreenFxOverlay overlay;

        /// <summary>The runner, created on demand; null outside Play mode or without settings.</summary>
        internal static GameFeelRunner Get() {
            if (instance != null)
                return instance;
            if (quitting || !Application.isPlaying)
                return null;
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings == null)
                return null;
            var go = new GameObject("GameFeel");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameFeelRunner>();
            instance.Init(settings);
            return instance;
        }

        /// <summary>The runner if it already exists; never creates one (for queries and stops).</summary>
        internal static GameFeelRunner Existing => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            instance = null;
            quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting() => quitting = true;

        /// <summary>Starts the kit with the first scene so every character gets its feel from the start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings != null && settings.characters.enabled)
                Get();
        }

        void Init(GameFeelSettings settings) {
            Settings = settings;
            Sfx = new SfxPlayer(transform, settings.audio);
            Shake = new CameraShaker(settings.cameraShake);
            Fx = new FxPool(transform, settings.effects);
            Texts = new FloatingTextPool(transform, settings.floatingText);
            Punches = new PunchAnimator();
            Moods = new MoodBlender(transform, settings.moods);
            Characters = new CharacterFeelSystem(settings.characters, this);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        /// <summary>The screen overlay, instantiated from its prefab the first time an effect needs it.</summary>
        internal ScreenFxOverlay Overlay {
            get {
                if (overlay == null && Settings.screenOverlay != null) {
                    overlay = Instantiate(Settings.screenOverlay, transform);
                    overlay.name = Settings.screenOverlay.name;
                }
                return overlay;
            }
        }

        /// <summary>The overlay only if something already created it.</summary>
        internal ScreenFxOverlay ExistingOverlay => overlay;

        void Update() {
            float dt = Time.unscaledDeltaTime;
            Punches.Update(dt);
            Moods.Update(dt);
        }

        void LateUpdate() {
            MainCamera = Camera.main;
            float dt = Time.unscaledDeltaTime;
            Shake.Update(dt);
            Fx.Update();
            Characters.LateUpdate(MainCamera);
            Texts.LateUpdate(MainCamera);
            Moods.LateUpdate(MainCamera);
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera) {
            if (camera == MainCamera)
                Shake.Apply(camera);
        }

        void OnEndCamera(ScriptableRenderContext context, Camera camera) => Shake.Restore(camera);

        void OnDestroy() {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            Characters?.Dispose();
            Punches?.RestoreAll();
            if (instance == this)
                instance = null;
        }
    }
}
