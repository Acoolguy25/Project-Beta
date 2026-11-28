using RyanAssets.Characters.Shared;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// One flashlight shot as everyone sees it: a spot light that lights what the beam reaches (a room in a
    /// Blackout too) and a soft cone of light along the beam, sized to the range and angle the server
    /// tested. It holds for a moment, fades and removes itself. Built by <c>HS_Authoring</c> as the
    /// FlashlightBeam prefab (<see cref="HS_Map.flashlightBeamPrefab"/>); <see cref="HS_AbilityFeel"/> fires it.
    /// </summary>
    public sealed class HS_FlashlightBeam : MonoBehaviour {
        [SerializeField] Light spot;
        [Tooltip("The cone's transform: a unit cone along +Z (apex at the origin, radius 1 at z = 1), scaled to the beam.")]
        [SerializeField] Transform cone;
        [SerializeField] Renderer coneRenderer;

        [Header("Look")]
        [Tooltip("Spot light intensity at full strength. URP lights fall off with the square of the distance, so a long beam needs a bright light.")]
        [SerializeField, Min(0f)] float spotIntensity = 260f;
        [Tooltip("The light reaches this much further than the beam's hit range, so the far end of the cone is lit too.")]
        [SerializeField, Min(1f)] float lightRangeScale = 1.2f;
        [SerializeField] Color beamColor = new(1f, 0.95f, 0.78f, 1f);
        [Tooltip("Opacity of the cone at full strength.")]
        [SerializeField, Range(0f, 1f)] float coneAlpha = 0.32f;
        [Tooltip("Seconds at full strength, then the fade.")]
        [SerializeField, Min(0f)] float holdSeconds = 0.35f;
        [SerializeField, Min(0.05f)] float fadeSeconds = 0.7f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock block;
        float firedAt = -1f;

        /// <summary>
        /// Points the beam from <paramref name="origin"/> along <paramref name="direction"/> and starts it. The
        /// cone and its light stop at the first wall or prop on the beam's axis, as the server's sight test does,
        /// so a beam never shows through a wall.
        /// </summary>
        public void Fire(Vector3 origin, Vector3 direction, float range, float angleDegrees) {
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
            direction.Normalize();
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            range = Mathf.Max(1f, ClipToWorld(origin, direction, range));
            angleDegrees = Mathf.Clamp(angleDegrees, 1f, 160f);
            float radius = range * Mathf.Tan(angleDegrees * 0.5f * Mathf.Deg2Rad);
            if (cone != null) cone.localScale = new Vector3(radius, radius, range);
            if (spot != null) {
                spot.type = LightType.Spot;
                spot.range = range * lightRangeScale;
                spot.spotAngle = angleDegrees;
                spot.innerSpotAngle = angleDegrees * 0.55f;
                spot.color = beamColor;
            }
            firedAt = Time.unscaledTime;
            Apply(1f);
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>Distance to the nearest world hit along the axis within <paramref name="range"/> (characters do not stop it).</summary>
        static float ClipToWorld(Vector3 origin, Vector3 direction, float range) {
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, range, ~0, QueryTriggerInteraction.Ignore);
            float nearest = range;
            for (int i = 0; i < count; i++) {
                if (Hits[i].distance >= nearest || Hits[i].collider.GetComponentInParent<LocalCharacter>() != null) continue;
                nearest = Hits[i].distance;
            }
            return nearest;
        }

        void Update() {
            if (firedAt < 0f) return;
            float age = Time.unscaledTime - firedAt;
            float strength = age <= holdSeconds ? 1f : 1f - (age - holdSeconds) / fadeSeconds;
            if (strength <= 0f) {
                Destroy(gameObject);
                return;
            }
            // Ease out: bright for most of the fade, then gone.
            Apply(strength * strength * (3f - 2f * strength));
        }

        void Apply(float strength) {
            if (spot != null) spot.intensity = spotIntensity * strength;
            if (coneRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            Color color = beamColor;
            color.a = coneAlpha * strength;
            block.SetColor(BaseColor, color);
            coneRenderer.SetPropertyBlock(block);
        }
    }
}
