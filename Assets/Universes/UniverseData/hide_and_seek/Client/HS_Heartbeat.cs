using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Rounds;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// The hider's tension: while seekers hunt, a heartbeat speeds up and a dark red vignette pulses with
    /// it as the nearest seeker gets closer. It only uses what this client already has: seekers are
    /// replicated characters, and roles come from the round state.
    /// </summary>
    public sealed class HS_Heartbeat : MonoBehaviour {
        [SerializeField] HS_ClientController controller;
        [Tooltip("Full-screen vignette image; its colour's alpha is driven here.")]
        [SerializeField] Image vignette;
        [SerializeField] SfxSound beat = new();

        [Header("Distance (world units)")]
        [Tooltip("The heartbeat is at its fastest and loudest with a seeker this close.")]
        [SerializeField, Min(0f)] float near = 10f;
        [Tooltip("The heartbeat starts when a seeker comes this close.")]
        [SerializeField, Min(0f)] float far = 60f;
        [Tooltip("How often the nearest seeker is looked up (seconds).")]
        [SerializeField, Min(0.05f)] float scanInterval = 0.2f;
        [Tooltip("Seconds the felt danger takes to follow the real distance, so it swells and fades instead of jumping.")]
        [SerializeField, Min(0.01f)] float smoothing = 0.5f;

        [Header("Beat")]
        [Tooltip("Beats per minute at the edge of the range and with a seeker right there.")]
        [SerializeField] Vector2 beatsPerMinute = new(62f, 150f);
        [SerializeField] Vector2 beatVolume = new(0.25f, 1f);

        [Header("Vignette")]
        [SerializeField] Color vignetteColor = new(0.35f, 0f, 0.02f, 1f);
        [Tooltip("Vignette alpha between beats and on a beat, at full danger.")]
        [SerializeField] Vector2 vignetteAlpha = new(0.2f, 0.75f);
        [SerializeField, Min(0.05f)] float pulseSeconds = 0.35f;

        float proximity, target, nextScan, sinceBeat, pulse;

        void Awake() => Apply(0f);

        void OnDisable() {
            proximity = target = pulse = 0f;
            Apply(0f);
        }

        void Update() {
            float dt = Time.deltaTime;
            if (Time.unscaledTime >= nextScan) {
                nextScan = Time.unscaledTime + scanInterval;
                target = Hunted() ? HS_Rules.Proximity(NearestSeekerDistance(), near, far) : 0f;
            }
            proximity = Mathf.MoveTowards(proximity, target, dt / smoothing);
            pulse = Mathf.Max(0f, pulse - dt / pulseSeconds);
            if (proximity > 0.01f) {
                sinceBeat += dt;
                if (sinceBeat >= 60f / Mathf.Lerp(beatsPerMinute.x, beatsPerMinute.y, proximity)) {
                    sinceBeat = 0f;
                    pulse = 1f;
                    GameSfx.Play(beat, Mathf.Lerp(beatVolume.x, beatVolume.y, proximity));
                }
            } else sinceBeat = 0f;
            Apply(proximity * Mathf.Lerp(vignetteAlpha.x, vignetteAlpha.y, pulse));
        }

        bool Hunted() => controller.HasState && controller.Phase == HS_Phase.Seeking && controller.LocalRole == HS_Role.Hider && LocalPlayer.Character != null;

        float NearestSeekerDistance() {
            Vector3 me = LocalPlayer.Character.transform.position;
            float best = float.MaxValue;
            foreach (var pair in controller.Roles) {
                if (!HS_ClientController.Seeks(pair.Value)) continue;
                var seeker = CharacterLookup.ByClientId(pair.Key);
                if (seeker != null) best = Mathf.Min(best, Vector3.Distance(me, seeker.transform.position));
            }
            return best;
        }

        void Apply(float alpha) {
            Color c = vignetteColor;
            c.a = alpha;
            vignette.color = c;
            bool visible = alpha > 0.001f;
            if (vignette.enabled != visible) vignette.enabled = visible;
        }
    }
}
