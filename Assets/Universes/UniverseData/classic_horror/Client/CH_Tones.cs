using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The minigames' sounds, synthesised once on first use: the choir's four notes (a D minor seventh, struck like small
    /// bells), the engine cord, a click, the engine catching, the lock dial's hum. No audio assets to import.
    /// </summary>
    public static class CH_Tones {
        const int Rate = 44100;
        /// <summary>The four notes of the choir, low to high: D, F, A, C.</summary>
        public static readonly float[] NoteHertz = { 293.66f, 349.23f, 440f, 523.25f };
        /// <summary>Seconds a hymn note rings.</summary>
        const float NoteSeconds = 1.4f;

        static readonly Dictionary<int, AudioClip> notes = new();
        static AudioClip cord, click, ignition, hum, chain;

        public static AudioClip Note(int index) {
            index = Mathf.Clamp(index, 0, NoteHertz.Length - 1);
            if (notes.TryGetValue(index, out AudioClip clip) && clip != null) return clip;
            float hertz = NoteHertz[index];
            clip = Make($"Choir note {index}", NoteSeconds, t => {
                float decay = Mathf.Exp(-t * 3.2f);
                // A bell's partials are not whole multiples: that is what makes it ring rather than hum.
                return decay * (Mathf.Sin(2f * Mathf.PI * hertz * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * hertz * 2.76f * t) * Mathf.Exp(-t * 4f)
                    + 0.25f * Mathf.Sin(2f * Mathf.PI * hertz * 5.4f * t) * Mathf.Exp(-t * 7f)) * 0.35f * Mathf.Min(1f, t * 400f);
            });
            notes[index] = clip;
            return clip;
        }

        /// <summary>The cord yanked: a short low thud with grit.</summary>
        public static AudioClip Cord() => cord != null ? cord : cord = Make("Cord", 0.3f, t => {
            float body = Mathf.Sin(2f * Mathf.PI * (70f - 40f * t) * t) * Mathf.Exp(-t * 14f);
            float grit = (Mathf.PerlinNoise(t * 4000f, 3.1f) - 0.5f) * Mathf.Exp(-t * 22f);
            return (body + grit * 0.8f) * 0.7f;
        });

        public static AudioClip Click() => click != null ? click : click = Make("Click", 0.06f, t =>
            (Mathf.PerlinNoise(t * 9000f, 1.3f) - 0.5f) * 2f * Mathf.Exp(-t * 90f) * 0.5f);

        /// <summary>The engine catching: a rising rattle that settles into a low idle.</summary>
        public static AudioClip Ignition() => ignition != null ? ignition : ignition = Make("Ignition", 1.6f, t => {
            float rpm = Mathf.Lerp(18f, 42f, 1f - Mathf.Exp(-t * 3f));
            float phase = rpm * t;
            float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * phase)), 6f);
            return (pulse * Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.8f + (Mathf.PerlinNoise(t * 600f, 7.7f) - 0.5f) * 0.35f * pulse)
                * Mathf.Min(1f, t * 20f) * Mathf.Exp(-Mathf.Max(0f, t - 1.1f) * 3f) * 0.6f;
        });

        /// <summary>A second of an even tone, built to loop: the dial's hum, louder and higher as it warms.</summary>
        public static AudioClip Hum() => hum != null ? hum : hum = Make("Dial hum", 1f, t =>
            (Mathf.Sin(2f * Mathf.PI * 196f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 294f * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * 98f * t)) * 0.28f);

        /// <summary>A heavy link of chain dropping: a metallic clank.</summary>
        public static AudioClip Chain() => chain != null ? chain : chain = Make("Chain", 0.7f, t =>
            (Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1330f * t) * 0.35f + (Mathf.PerlinNoise(t * 7000f, 5.5f) - 0.5f) * 0.6f)
            * Mathf.Exp(-t * 9f) * 0.5f);

        static AudioClip Make(string name, float seconds, System.Func<float, float> sample) {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
