using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// The hider's tension: while seekers hunt, a heartbeat speeds up and the kit's red danger vignette
    /// (<see cref="ScreenFx.SetDanger(float, Color, float, float)"/>: edge only, under every HUD) pulses at
    /// the same rate as the nearest seeker gets closer. It only uses what this client already has:
    /// seekers are replicated characters, and roles come from the round state.
    /// </summary>
    public sealed class HS_Heartbeat : MonoBehaviour {
        [SerializeField] HS_ClientController controller;
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
        [SerializeField] Color vignetteColor = new(0.55f, 0.02f, 0.04f, 1f);
        [Tooltip("Peak alpha of the edge vignette at full danger. Keep it low: the hider has to watch the doorway.")]
        [SerializeField, Range(0f, 1f)] float vignetteMaxAlpha = 0.3f;

        float proximity, target, nextScan, sinceBeat, sentStrength = -1f, sentRate = -1f;

        void OnDisable() {
            proximity = target = 0f;
            sentStrength = sentRate = -1f;
            ScreenFx.ClearDanger();
        }

        void Update() {
            float dt = Time.deltaTime;
            if (Time.unscaledTime >= nextScan) {
                nextScan = Time.unscaledTime + scanInterval;
                target = Hunted() ? HS_Rules.Proximity(NearestSeekerDistance(), near, far) : 0f;
            }
            proximity = Mathf.MoveTowards(proximity, target, dt / smoothing);
            float bpm = Mathf.Lerp(beatsPerMinute.x, beatsPerMinute.y, proximity);
            if (proximity > 0.01f) {
                sinceBeat += dt;
                if (sinceBeat >= 60f / bpm) {
                    sinceBeat = 0f;
                    GameSfx.Play(beat, Mathf.Lerp(beatVolume.x, beatVolume.y, proximity));
                }
            } else sinceBeat = 0f;
            float strength = proximity > 0.01f ? proximity : 0f, rate = bpm / 60f;
            // Only tell the overlay when it changes noticeably.
            if (Mathf.Abs(strength - sentStrength) > 0.02f || (strength == 0f) != (sentStrength == 0f) || Mathf.Abs(rate - sentRate) > 0.05f) {
                sentStrength = strength;
                sentRate = rate;
                ScreenFx.SetDanger(strength, vignetteColor, vignetteMaxAlpha, rate);
            }
        }

        bool Hunted() => controller.HasState && controller.Phase == HS_Phase.Seeking && controller.LocalRole == HS_Role.Hider && LocalPlayer.Character != null;

        float NearestSeekerDistance() {
            Vector3 me = LocalPlayer.Character.transform.position;
            float best = float.MaxValue;
            foreach (var pair in controller.RoleTable) {
                if (!HS_ClientController.Seeks(pair.Value)) continue;
                var seeker = CharacterLookup.ByClientId(pair.Key);
                if (seeker != null) best = Mathf.Min(best, Vector3.Distance(me, seeker.transform.position));
            }
            return best;
        }
    }
}
