using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>Runs every active <see cref="Punch"/>: one entry per transform, base scale restored at the end.</summary>
    internal sealed class PunchAnimator {
        internal enum Mode {
            Scale,
            Squash,
            Pop,
        }

        struct Item {
            public Transform target;
            public Vector3 baseScale;
            public Mode mode;
            public float amount;
            public float duration;
            public float age;
        }

        // Wobbles a punch makes before it settles.
        const float PunchCycles = 1.5f;
        // Share of a squash's amount given to the sideways bulge (volume is roughly kept).
        const float BulgeShare = 0.5f;
        const int InitialCapacity = 32;

        readonly List<Item> items = new(InitialCapacity);

        public void Add(Transform target, Mode mode, float amount, float seconds) {
            if (target == null || seconds <= 0f)
                return;
            for (int i = 0; i < items.Count; i++) {
                if (items[i].target != target)
                    continue;
                // Restart from the original scale, so rapid punches never compound.
                Item existing = items[i];
                existing.mode = mode;
                existing.amount = amount;
                existing.duration = seconds;
                existing.age = 0f;
                items[i] = existing;
                return;
            }
            items.Add(new Item { target = target, baseScale = target.localScale, mode = mode, amount = amount, duration = seconds });
        }

        public void Cancel(Transform target) {
            for (int i = items.Count - 1; i >= 0; i--) {
                if (items[i].target != target)
                    continue;
                if (target != null)
                    target.localScale = items[i].baseScale;
                RemoveAt(i);
            }
        }

        public void RestoreAll() {
            for (int i = 0; i < items.Count; i++)
                if (items[i].target != null)
                    items[i].target.localScale = items[i].baseScale;
            items.Clear();
        }

        public void Update(float dt) {
            for (int i = items.Count - 1; i >= 0; i--) {
                Item item = items[i];
                if (item.target == null) {
                    RemoveAt(i);
                    continue;
                }
                item.age += dt;
                float t = item.age / item.duration;
                if (t >= 1f) {
                    item.target.localScale = item.baseScale;
                    RemoveAt(i);
                    continue;
                }
                item.target.localScale = Vector3.Scale(item.baseScale, Factor(item.mode, item.amount, t));
                items[i] = item;
            }
        }

        void RemoveAt(int index) {
            int last = items.Count - 1;
            items[index] = items[last];
            items.RemoveAt(last);
        }

        static Vector3 Factor(Mode mode, float amount, float t) {
            switch (mode) {
                case Mode.Squash: {
                    // Starts squashed, rebounds into a stretch, settles.
                    float wave = Mathf.Cos(t * Mathf.PI * 2f * PunchCycles * 0.5f) * Damp(t);
                    float bulge = 1f + amount * BulgeShare * wave;
                    return new Vector3(bulge, 1f - amount * wave, bulge);
                }
                case Mode.Pop: {
                    // Grows in from `amount` (a scale, e.g. 0.6) with a little overshoot.
                    float s = Mathf.LerpUnclamped(amount, 1f, EaseOutBack(t));
                    return new Vector3(s, s, s);
                }
                default: {
                    float s = 1f + amount * Mathf.Sin(t * Mathf.PI * 2f * PunchCycles) * Damp(t);
                    return new Vector3(s, s, s);
                }
            }
        }

        static float Damp(float t) {
            float u = 1f - t;
            return u * u;
        }

        static float EaseOutBack(float t) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
