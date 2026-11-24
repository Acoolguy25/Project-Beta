using RyanAssets.Characters.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// The obby's stamina meter: a labelled bar at the bottom centre of the HUD that shows while stamina is
    /// spent, turns warning red once there is not enough left for a jump, and fades out a moment after it
    /// refills. Sprinting and jumping both spend stamina, so it gates every hop and needs to be read at a
    /// glance. While it is on screen the kit's own thin full-width stamina strip is hidden (and shown again
    /// when the obby HUD goes away), so the two never show at once.
    /// </summary>
    public sealed class OB_StaminaMeter : MonoBehaviour {
        [Tooltip("Fades the whole meter.")]
        public CanvasGroup group;
        [Tooltip("Fill of the bar: its right anchor follows the stamina fraction.")]
        public RectTransform fill;
        [Tooltip("Coloured image of the fill.")]
        public Image fillImage;
        [Tooltip("The STAMINA caption; it says OUT OF STAMINA while a jump cannot be paid.")]
        public TMP_Text label;
        public Color fullColor = new(1f, 0.79f, 0.25f);
        public Color lowColor = new(1f, 0.3f, 0.18f);
        [Tooltip("Stamina a jump costs (the character controller's jump cost); below it the meter warns.")]
        [Min(0f)] public float jumpCost = 5f;
        [Tooltip("Fraction of the pool under which the bar starts shifting toward the warning colour.")]
        [Range(0f, 1f)] public float lowFraction = 0.3f;
        [Tooltip("Seconds the meter stays after stamina is full again.")]
        [Min(0f)] public float fullHoldSeconds = 1f;
        [Tooltip("Seconds the meter takes to fade in or out.")]
        [Min(0.01f)] public float fadeSeconds = 0.3f;
        [Tooltip("Seconds between looks for the kit's stamina strip until it is found.")]
        [Min(0.1f)] public float findInterval = 1f;

        Canvas kitStrip;
        bool kitStripWasEnabled;
        float fullSince = float.NegativeInfinity, nextFind;
        bool warned;

        void OnEnable() {
            if (group != null) group.alpha = 0f;
            nextFind = 0f;
            warned = false;
            if (label != null) label.text = "STAMINA";
        }

        void OnDisable() => RestoreKitStrip();

        void Update() {
            HideKitStrip();
            if (!StaminaController.StaminaLoaded || !StaminaController.StaminaEnabled || LocalPlayer.Character == null || LocalPlayer.Character.IsDead) {
                Fade(0f);
                return;
            }
            float max = StaminaController.MaxStamina;
            float stamina = StaminaController.Stamina;
            float fraction = Mathf.Clamp01(stamina / max);
            if (fill != null) {
                var anchor = fill.anchorMax;
                anchor.x = fraction;
                fill.anchorMax = anchor;
            }
            bool low = stamina < jumpCost;
            if (fillImage != null) {
                float warn = low ? 1f : lowFraction > 0f ? Mathf.Clamp01(1f - fraction / lowFraction) : 0f;
                fillImage.color = Color.Lerp(fullColor, lowColor, warn);
            }
            if (label != null && low != warned) {
                warned = low;
                label.text = low ? "OUT OF STAMINA" : "STAMINA";
                label.color = low ? lowColor : Color.white;
            }
            if (stamina < max) fullSince = float.PositiveInfinity;
            else if (float.IsPositiveInfinity(fullSince)) fullSince = Time.unscaledTime;
            Fade(Time.unscaledTime - fullSince < fullHoldSeconds ? 1f : 0f);
        }

        void Fade(float target) {
            if (group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }

        // The kit draws stamina as a thin strip along the bottom edge (its own canvas, kept by the client
        // prefab across games); the obby's labelled meter replaces it while the obby HUD is up.
        void HideKitStrip() {
            if (kitStrip != null || Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + findInterval;
            var strip = FindAnyObjectByType<RyanAssets.Client.ClientUI.Stamina.StaminaUI>(FindObjectsInactive.Include);
            if (strip == null || !strip.TryGetComponent(out kitStrip)) return;
            kitStripWasEnabled = kitStrip.enabled;
            kitStrip.enabled = false;
        }

        void RestoreKitStrip() {
            if (kitStrip != null) kitStrip.enabled = kitStripWasEnabled;
            kitStrip = null;
        }
    }
}
