using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// The shared edge-only danger vignette (the centre of the view stays clear) has one setting; two
    /// things in Flag Wars want it: an enemy carrying your flag, and one hit left. Each sets its own
    /// source here and the stronger one shows.
    /// </summary>
    public static class CTF_EdgeVignette {
        public enum Source { FlagTaken, LowHealth }

        struct Want {
            public float strength, maxAlpha, pulses;
            public Color color;
        }

        static readonly Want[] wants = new Want[2];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            for (int i = 0; i < wants.Length; i++) wants[i] = default;
        }

        public static void Set(Source source, float strength, Color color, float maxAlpha, float pulsesPerSecond) {
            wants[(int)source] = new Want { strength = Mathf.Clamp01(strength), color = color, maxAlpha = maxAlpha, pulses = pulsesPerSecond };
            Apply();
        }

        public static void Clear(Source source) => Set(source, 0f, Color.clear, 0f, 0f);

        static void Apply() {
            int best = -1;
            float bestAlpha = 0f;
            for (int i = 0; i < wants.Length; i++) {
                float alpha = wants[i].strength * wants[i].maxAlpha;
                if (alpha <= bestAlpha) continue;
                bestAlpha = alpha;
                best = i;
            }
            if (best < 0) {
                ScreenFx.SetDanger(0f);
                return;
            }
            var w = wants[best];
            ScreenFx.SetDanger(w.strength, w.color, w.maxAlpha, w.pulses);
        }
    }
}
