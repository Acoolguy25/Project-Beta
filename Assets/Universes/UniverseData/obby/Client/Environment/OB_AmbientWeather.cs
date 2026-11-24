using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Ambient particles around the camera, one system per tier: pollen sparkles, falling leaves,
    /// drifting sand, snow, embers and ash, void motes. The emitters follow the camera while their
    /// particles stay in the world, and a change of tier fades one system's rate out as the next fades in.
    /// </summary>
    public sealed class OB_AmbientWeather : MonoBehaviour {
        [Tooltip("Weather per difficulty tier (indexed by OB_Tier); empty for none. Rates are authored on the systems.")]
        public ParticleSystem[] tierSystems;
        [Tooltip("Seconds a change of tier takes to fade the weather over.")]
        [Min(0.01f)] public float blendSeconds = 3f;
        [Tooltip("The emitters centre this far in front of the camera (m), where particles are seen.")]
        [Min(0f)] public float ahead = 14f;

        float[] baseRates;
        float[] weights;
        int target = -1;

        void Awake() => Capture();

        void Capture() {
            if (baseRates != null || tierSystems == null) return;
            baseRates = new float[tierSystems.Length];
            weights = new float[tierSystems.Length];
            for (int i = 0; i < tierSystems.Length; i++) {
                if (tierSystems[i] == null) continue;
                baseRates[i] = tierSystems[i].emission.rateOverTimeMultiplier;
                Apply(i, 0f);
            }
        }

        public void SetTier(OB_Tier tier, bool instant) {
            Capture();
            if (tierSystems == null || tierSystems.Length == 0) return;
            target = Mathf.Clamp((int)tier, 0, tierSystems.Length - 1);
            if (!instant) return;
            for (int i = 0; i < weights.Length; i++) {
                weights[i] = i == target ? 1f : 0f;
                Apply(i, weights[i]);
            }
        }

        void LateUpdate() {
            if (target < 0) return;
            Camera view = Camera.main;
            if (view != null) transform.position = view.transform.position + view.transform.forward * ahead;
            float step = Time.unscaledDeltaTime / blendSeconds;
            for (int i = 0; i < weights.Length; i++) {
                float goal = i == target ? 1f : 0f;
                if (weights[i] == goal) continue;
                weights[i] = Mathf.MoveTowards(weights[i], goal, step);
                Apply(i, weights[i]);
            }
        }

        /// <summary>Moves the emitters in front of <paramref name="view"/> and fast-forwards the current tier's weather (preview renders).</summary>
        public void Preview(Camera view, float seconds) {
            if (target < 0 || tierSystems[target] == null || view == null) return;
            transform.position = view.transform.position + view.transform.forward * ahead;
            tierSystems[target].Simulate(seconds, true, true);
        }

        void Apply(int index, float weight) {
            var system = tierSystems[index];
            if (system == null) return;
            var emission = system.emission;
            emission.rateOverTimeMultiplier = baseRates[index] * weight;
            if (weight > 0f && !system.isEmitting) system.Play(true);
            else if (weight <= 0f && system.isEmitting) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
