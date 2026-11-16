using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.GameSettings;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Shakes whatever the main camera is showing: first person, third person, top-down or
    /// commander rigs alike. Strengths are 0..1 (1 = the biggest shake the kit allows; tune the
    /// ceiling in GameFeelSettings). The player's "Camera Shake" setting (Camera section of the game
    /// menu) scales everything and 0 turns it off.
    /// <code>
    /// CameraShake.Kick(0.3f, 0.2f);                      // recoil, a hit taken
    /// CameraShake.Explosion(blastPos, radius: 25f);      // stronger the closer the blast
    /// </code>
    /// </summary>
    public static class CameraShake {
        static IntGameSetting setting;
        static float intensity = 1f;
        static bool bound;

        /// <summary>A single shake: <paramref name="strength"/> 0..1, dying away over <paramref name="seconds"/>.</summary>
        public static void Kick(float strength = 0.4f, float seconds = 0.25f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Shake.Add(strength, seconds);
        }

        /// <summary>
        /// A shake from a blast at <paramref name="position"/>: full <paramref name="strength"/> at
        /// the centre, fading to nothing at <paramref name="radius"/>. Distance is measured to the
        /// local player's character when there is one, else to the camera, or to the camera's line of
        /// sight for overhead cameras (a blast in the middle of a commander's screen shakes fully).
        /// </summary>
        public static void Explosion(Vector3 position, float radius, float strength = 1f, float seconds = 0.6f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner == null || radius <= 0f)
                return;
            float distance = ListenerDistance(position, runner.MainCamera != null ? runner.MainCamera : Camera.main);
            float falloff = Mathf.Clamp01(1f - distance / radius);
            if (falloff <= 0f)
                return;
            runner.Shake.Add(strength * Mathf.Pow(falloff, runner.Settings.cameraShake.falloffExponent), seconds);
        }

        /// <summary>Stops every shake at once (cutscenes, menus).</summary>
        public static void Stop() => GameFeelRunner.Existing?.Shake.Clear();

        /// <summary>
        /// The player's shake multiplier, 0..1, saved as the "Camera Shake" game setting
        /// (<see cref="GameSettingsClient.CameraShakeSetting"/>). Setting it updates the menu slider.
        /// </summary>
        public static float Intensity {
            get {
                Bind();
                return intensity;
            }
            set {
                Bind();
                intensity = Mathf.Clamp01(value);
                if (setting != null)
                    setting.TrySet(Mathf.RoundToInt(intensity * setting.max));
            }
        }

        static float ListenerDistance(Vector3 position, Camera camera) {
            LocalCharacter character = LocalPlayer.Character;
            if (character != null && !character.IsDead)
                return Vector3.Distance(position, character.transform.position);
            if (camera == null)
                return float.PositiveInfinity;
            Transform view = camera.transform;
            Vector3 toBlast = position - view.position;
            float along = Vector3.Dot(toBlast, view.forward);
            float direct = toBlast.magnitude;
            if (along <= 0f)
                return direct;
            // Overhead cameras sit far above the action; what matters is how close to the centre of
            // the view the blast is, not how far the lens is.
            return Mathf.Min(direct, (toBlast - view.forward * along).magnitude);
        }

        static void Bind() {
            if (bound)
                return;
            bound = true;
            if (!GameSettingsClient.gameSettingsConfigUI.TryGetValue(GameSettingsClient.CameraShakeSetting, out GameSettingsInstance instance)
                || instance is not IntGameSetting intSetting) {
                Debug.LogWarning($"GameFeel: no '{GameSettingsClient.CameraShakeSetting}' int game setting; camera shake stays at full intensity.");
                return;
            }
            setting = intSetting;
            setting.Load();
            setting.on_update += OnSettingChanged;
            OnSettingChanged(setting.value);
        }

        static void OnSettingChanged(int value) =>
            intensity = setting != null && setting.max > 0 ? Mathf.Clamp01(value / (float)setting.max) : 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            if (setting != null)
                setting.on_update -= OnSettingChanged;
            setting = null;
            intensity = 1f;
            bound = false;
        }
    }
}
