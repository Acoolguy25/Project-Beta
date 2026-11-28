using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// Where your decoy landed, shown only to you while it is live: a glowing noise maker that bobs, a ring
    /// that pulses out like the taunt it keeps faking, and a thin beam so you can find it from across the
    /// house. It shrinks away when its time is up. Built by <c>HS_Authoring</c> as the DecoyMarker prefab
    /// (<see cref="HS_Map.decoyMarkerPrefab"/>); <see cref="HS_AbilityFeel"/> places and removes it.
    /// </summary>
    public sealed class HS_DecoyMarker : MonoBehaviour {
        [SerializeField] Transform body;
        [SerializeField] Transform ring;
        [Header("Motion")]
        [SerializeField, Min(0.1f)] float pulseSeconds = 1.4f;
        [Tooltip("The ring grows from its authored size to this multiple, fading as it goes.")]
        [SerializeField, Min(1f)] float pulseScale = 2.4f;
        [SerializeField, Min(0f)] float bobHeight = 0.35f;
        [SerializeField, Min(0.05f)] float shrinkSeconds = 0.35f;
        [Tooltip("The noise maker and its beam hide while the camera is this close (world units), so a decoy at your feet never fills the view; the ring stays.")]
        [SerializeField, Min(0f)] float hideWithin = 9f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        Renderer ringRenderer;
        Renderer[] bodyRenderers = System.Array.Empty<Renderer>();
        bool bodyShown = true;
        MaterialPropertyBlock block;
        Vector3 ringScale, bodyPosition;
        Color ringColor;
        float until = float.MaxValue, endedAt = -1f;

        public bool Expired => endedAt >= 0f && Time.unscaledTime - endedAt >= shrinkSeconds;

        void Awake() {
            if (ring != null) {
                ringScale = ring.localScale;
                ringRenderer = ring.GetComponent<Renderer>();
                if (ringRenderer != null) ringColor = ringRenderer.sharedMaterial.GetColor(BaseColor);
            }
            if (body != null) bodyPosition = body.localPosition;
            bodyRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r != ringRenderer);
        }

        /// <summary>Hides the noise maker and beam while the camera is right on top of them.</summary>
        void FadeNearCamera() {
            Camera cam = Camera.main;
            bool show = cam == null || (cam.transform.position - transform.position).sqrMagnitude > hideWithin * hideWithin;
            if (show == bodyShown) return;
            bodyShown = show;
            foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = show;
        }

        /// <summary>Puts the marker on the decoy's spot for <paramref name="seconds"/>.</summary>
        public void Place(Vector3 position, float seconds) {
            transform.position = position;
            until = Time.unscaledTime + Mathf.Max(0.1f, seconds);
            endedAt = -1f;
            transform.localScale = Vector3.one;
        }

        /// <summary>Ends it now (the round moved on), with the short shrink.</summary>
        public void End() {
            if (endedAt < 0f) endedAt = Time.unscaledTime;
        }

        void Update() {
            float now = Time.unscaledTime;
            if (endedAt < 0f && now >= until) endedAt = now;
            if (endedAt >= 0f) {
                float t = (now - endedAt) / shrinkSeconds;
                if (t >= 1f) {
                    Destroy(gameObject);
                    return;
                }
                transform.localScale = Vector3.one * (1f - t);
            }
            FadeNearCamera();
            float phase = Mathf.Repeat(now / pulseSeconds, 1f);
            if (ring != null) {
                ring.localScale = Vector3.Scale(ringScale, new Vector3(Mathf.Lerp(1f, pulseScale, phase), 1f, Mathf.Lerp(1f, pulseScale, phase)));
                if (ringRenderer != null) {
                    block ??= new MaterialPropertyBlock();
                    Color c = ringColor;
                    c.a *= 1f - phase;
                    block.SetColor(BaseColor, c);
                    ringRenderer.SetPropertyBlock(block);
                }
            }
            if (body != null) body.localPosition = bodyPosition + Vector3.up * (Mathf.Sin(now * Mathf.PI * 2f / pulseSeconds) * 0.5f + 0.5f) * bobHeight;
        }
    }
}
