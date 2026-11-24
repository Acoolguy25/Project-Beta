using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// The Blackout map event on this client: while the server says it runs, every lamp, the low sun and
    /// the ambient light sink to a sliver, with a power-down thud on the way out and a hum when the power
    /// comes back. The candles keep flickering at their own (dimmed) level through <see cref="HS_LightFlicker.Dim"/>.
    /// </summary>
    public sealed class HS_Blackout : MonoBehaviour {
        [SerializeField] HS_ClientController controller;
        [SerializeField] HS_LightFlicker flicker;

        [Header("Levels while the power is out (fraction of normal)")]
        [SerializeField, Range(0f, 1f)] float lampLevel = 0.08f;
        [SerializeField, Range(0f, 1f)] float sunLevel = 0.3f;
        [SerializeField, Range(0f, 1f)] float ambientLevel = 0.35f;
        [Tooltip("Candles and lanterns keep a little more light, so rooms are not pitch black.")]
        [SerializeField, Range(0f, 1f)] float candleLevel = 0.35f;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] float fadeOutSeconds = 0.5f;
        [SerializeField, Min(0.05f)] float fadeInSeconds = 2.5f;

        [Header("Sounds")]
        [SerializeField] SfxSound powerDown = new();
        [SerializeField] SfxSound powerUp = new();

        Light[] lights;
        float[] baseIntensity, outLevel;
        Color ambientSky, ambientEquator, ambientGround;
        float level = 1f, applied = -1f;
        bool dark, captured;

        void Start() {
            // The map lists the lights a blackout dims (HS_Authoring fills it: the sun and the lamps, not the
            // candles the flicker animates), so a light spawned later or one from another scene is never caught.
            HS_Map map = GetComponentInParent<HS_Map>();
            if (map == null || map.blackoutLights == null) {
                Debug.LogWarning("[HideAndSeek] No HS_Map with blackout lights above the HUD; the Blackout event only dims the ambient light.");
                lights = System.Array.Empty<Light>();
            } else lights = System.Array.FindAll(map.blackoutLights, light => light != null);
            baseIntensity = new float[lights.Length];
            outLevel = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) {
                baseIntensity[i] = lights[i].intensity;
                outLevel[i] = lights[i].type == LightType.Directional ? sunLevel : lampLevel;
            }
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            captured = true;
        }

        void Update() {
            if (!captured) return;
            bool wantDark = controller.HasState && controller.ActiveEvent == HS_RoundEvent.Blackout;
            if (wantDark != dark) {
                dark = wantDark;
                GameSfx.Play(dark ? powerDown : powerUp);
            }
            level = Mathf.MoveTowards(level, dark ? 0f : 1f, Time.deltaTime / (dark ? fadeOutSeconds : fadeInSeconds));
            if (Mathf.Approximately(level, applied)) return;
            Apply(level);
        }

        void Apply(float t) {
            applied = t;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = baseIntensity[i] * Mathf.Lerp(outLevel[i], 1f, t);
            float ambient = Mathf.Lerp(ambientLevel, 1f, t);
            RenderSettings.ambientSkyColor = ambientSky * ambient;
            RenderSettings.ambientEquatorColor = ambientEquator * ambient;
            RenderSettings.ambientGroundColor = ambientGround * ambient;
            flicker.Dim = Mathf.Lerp(candleLevel, 1f, t);
        }

        void OnDestroy() {
            if (captured) Apply(1f);
        }
    }
}
