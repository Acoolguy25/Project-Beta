using TMPro;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>Pooled world-space popups: rise, pop in, fade out, always facing the camera.</summary>
    internal sealed class FloatingTextPool {
        struct Popup {
            public FloatingTextLabel label;
            public Transform transform;
            public TMP_Text text;
            public bool active;
            public float age;
            public Vector3 origin;
            public Vector3 scatter;
            public FloatingTextStyle style;
        }

        // Fractions of a popup's life spent popping in and fading out.
        const float PopInShare = 0.18f;
        const float FadeOutStart = 0.6f;

        readonly GameFeelSettings.FloatingTextTuning tuning;
        readonly Transform parent;
        readonly Popup[] popups;
        int created;
        bool warnedMissingPrefab;

        public FloatingTextPool(Transform parent, GameFeelSettings.FloatingTextTuning tuning) {
            this.parent = parent;
            this.tuning = tuning;
            popups = new Popup[Mathf.Max(1, tuning.poolSize)];
        }

        /// <summary>Takes a popup (a free one, or the oldest) and returns its text to fill in, or null without a prefab.</summary>
        public TMP_Text Spawn(Vector3 position, in FloatingTextStyle style) {
            int index = Take();
            if (index < 0)
                return null;
            ref Popup popup = ref popups[index];
            popup.active = true;
            popup.age = 0f;
            popup.origin = position;
            popup.style = style;
            Vector2 offset = style.scatter > 0f ? Random.insideUnitCircle * style.scatter : Vector2.zero;
            popup.scatter = new Vector3(offset.x, 0f, offset.y);
            popup.text.color = style.color;
            popup.transform.position = position;
            popup.transform.localScale = Vector3.zero; // sized on the next LateUpdate, once the camera is known
            if (!popup.label.gameObject.activeSelf)
                popup.label.gameObject.SetActive(true);
            return popup.text;
        }

        int Take() {
            int oldest = -1;
            for (int i = 0; i < created; i++) {
                if (!popups[i].active)
                    return i;
                if (oldest < 0 || popups[i].age / popups[i].style.duration > popups[oldest].age / popups[oldest].style.duration)
                    oldest = i;
            }
            if (created < popups.Length && Create(created))
                return created++;
            return oldest;
        }

        bool Create(int index) {
            if (tuning.prefab == null) {
                if (!warnedMissingPrefab) {
                    warnedMissingPrefab = true;
                    Debug.LogError("GameFeel: GameFeelSettings has no floating text prefab. Run Ryan > Game Feel > Rebuild.");
                }
                return false;
            }
            FloatingTextLabel label = Object.Instantiate(tuning.prefab, parent);
            label.name = tuning.prefab.name + " " + index;
            label.gameObject.SetActive(false);
            popups[index] = new Popup { label = label, transform = label.transform, text = label.Text };
            return label.Text != null;
        }

        public void LateUpdate(Camera camera) {
            float dt = Time.unscaledDeltaTime;
            Transform view = camera != null ? camera.transform : null;
            for (int i = 0; i < created; i++) {
                ref Popup popup = ref popups[i];
                if (!popup.active)
                    continue;
                if (popup.label == null) {
                    popup.active = false;
                    continue;
                }
                popup.age += dt;
                float t = popup.age / popup.style.duration;
                if (t >= 1f) {
                    popup.active = false;
                    popup.label.gameObject.SetActive(false);
                    continue;
                }

                Vector3 position = popup.origin + popup.scatter * EaseOutCubic(Mathf.Min(1f, t * 2f))
                    + Vector3.up * (popup.style.rise * EaseOutCubic(t));
                float size = popup.style.size;
                if (view != null) {
                    float distance = Vector3.Distance(view.position, position);
                    size *= Mathf.Clamp(distance / tuning.referenceDistance, tuning.minDistanceScale, tuning.maxDistanceScale);
                    size = CapToScreen(popup, size, distance, camera);
                    popup.transform.rotation = view.rotation;
                }
                float pop = t < PopInShare
                    ? Mathf.LerpUnclamped(popup.style.pop, 1f, EaseOutBack(t / PopInShare))
                    : 1f;
                popup.transform.position = position;
                popup.transform.localScale = Vector3.one * (size * pop);

                Color color = popup.style.color;
                color.a *= t < FadeOutStart ? 1f : 1f - (t - FadeOutStart) / (1f - FadeOutStart);
                popup.text.color = color;
            }
        }

        /// <summary>
        /// Shrinks a popup whose text would cover more than <c>maxScreenHeight</c> of the view. Uses the
        /// text's own height at scale 1 (from its last mesh), so multi-line text is measured as a whole.
        /// </summary>
        float CapToScreen(in Popup popup, float size, float distance, Camera camera) {
            float textHeight = popup.text.textBounds.size.y;
            if (textHeight <= 0f || tuning.maxScreenHeight <= 0f)
                return size;
            float viewHeight = camera.orthographic
                ? camera.orthographicSize * 2f
                : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float maxWorld = tuning.maxScreenHeight * viewHeight;
            return textHeight * size > maxWorld ? maxWorld / textHeight : size;
        }

        static float EaseOutCubic(float t) {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        static float EaseOutBack(float t) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
