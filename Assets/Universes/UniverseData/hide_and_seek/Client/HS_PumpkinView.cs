using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// One glowing pumpkin of the pumpkin hunt as every client sees it: it pops up when it lights, bobs and
    /// turns slowly, its halo breathes like a candle, and it shrinks away when someone takes it or the round
    /// ends. Built by <c>HS_Authoring</c> as the Pumpkin prefab (<see cref="HS_Map.pumpkinPrefab"/>);
    /// <see cref="HS_ClientController"/> places and removes it.
    /// </summary>
    public sealed class HS_PumpkinView : MonoBehaviour {
        [Tooltip("The jack-o'-lantern model (bobs and turns).")]
        [SerializeField] Transform body;
        [Tooltip("The soft additive halo around it (breathes).")]
        [SerializeField] Renderer halo;
        [Header("Motion")]
        [SerializeField, Min(0.05f)] float popSeconds = 0.35f;
        [SerializeField, Min(0.05f)] float shrinkSeconds = 0.3f;
        [SerializeField, Min(0f)] float bobHeight = 0.5f;
        [SerializeField, Min(0.1f)] float bobSeconds = 2.2f;
        [Tooltip("Degrees per second the pumpkin turns.")]
        [SerializeField] float spinSpeed = 40f;
        [Tooltip("The halo's brightness swings between this share of its authored colour and full (it is additive).")]
        [SerializeField, Range(0f, 1f)] float haloLow = 0.55f;
        [SerializeField, Min(0.1f)] float haloSeconds = 0.9f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock block;
        Vector3 bodyPosition;
        Color haloColor;
        float bornAt, endedAt = -1f, phase;

        public int Id { get; private set; }
        public bool Ending => endedAt >= 0f;

        void Awake() {
            if (body != null) bodyPosition = body.localPosition;
            if (halo != null) haloColor = halo.sharedMaterial.GetColor(BaseColor);
        }

        /// <summary>Lights pumpkin <paramref name="id"/> at <paramref name="position"/> (feet level).</summary>
        public void Place(int id, Vector3 position) {
            Id = id;
            transform.position = position;
            bornAt = Time.unscaledTime;
            endedAt = -1f;
            // Each pumpkin bobs out of step with the others.
            phase = Mathf.Repeat(id * 0.37f, 1f);
            transform.localScale = Vector3.zero;
        }

        /// <summary>Taken or put out: a short shrink, then it removes itself.</summary>
        public void End() {
            if (endedAt < 0f) endedAt = Time.unscaledTime;
        }

        void Update() {
            float now = Time.unscaledTime;
            float scale = EaseOutBack(Mathf.Clamp01((now - bornAt) / popSeconds));
            if (endedAt >= 0f) {
                float t = (now - endedAt) / shrinkSeconds;
                if (t >= 1f) {
                    Destroy(gameObject);
                    return;
                }
                scale *= 1f - t * t;
            }
            transform.localScale = Vector3.one * scale;
            if (body != null) {
                float bob = Mathf.Sin((now / bobSeconds + phase) * Mathf.PI * 2f) * 0.5f + 0.5f;
                body.localPosition = bodyPosition + Vector3.up * bob * bobHeight;
                body.localRotation = Quaternion.Euler(0f, (now + phase * 10f) * spinSpeed, 0f);
            }
            if (halo != null) {
                block ??= new MaterialPropertyBlock();
                float k = Mathf.Lerp(haloLow, 1f, Mathf.Sin((now / haloSeconds + phase) * Mathf.PI * 2f) * 0.5f + 0.5f);
                block.SetColor(BaseColor, new Color(haloColor.r * k, haloColor.g * k, haloColor.b * k, haloColor.a));
                halo.SetPropertyBlock(block);
            }
        }

        static float EaseOutBack(float t) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
