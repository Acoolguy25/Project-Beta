using RyanAssets.Characters.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// The obby's dash meter: a labelled charge bar low in the HUD that shows while the dash is spent or
    /// recharging, reads "DASH" in gold once a burst can start, drains while one runs, shows "RECHARGING" while
    /// it refills (only on solid ground, after a short pause), flashes when a dash is refused, and fades out a
    /// moment after it is full again. It reads <see cref="CharacterDash.Local"/>. While it is on screen the
    /// kit's own thin stamina strip is hidden (and shown again when the obby HUD goes away). The class keeps
    /// its old name because the client prefab refers to it.
    /// </summary>
    public sealed class OB_StaminaMeter : MonoBehaviour {
        [Tooltip("Fades the whole meter.")]
        public CanvasGroup group;
        [Tooltip("Fill of the bar: its right anchor follows the charge.")]
        public RectTransform fill;
        [Tooltip("Coloured image of the fill.")]
        public Image fillImage;
        [Tooltip("The caption: DASH when a burst can start, RECHARGING while it cannot.")]
        public TMP_Text label;
        public Color readyColor = new(1f, 0.79f, 0.25f);
        [Tooltip("Colour while charging below the level a burst needs, and while a refused dash flashes.")]
        public Color lowColor = new(1f, 0.3f, 0.18f);
        public string readyText = "DASH", chargingText = "RECHARGING";
        [Tooltip("Seconds a refused dash flashes the meter.")]
        [Min(0f)] public float deniedFlashSeconds = 0.35f;
        [Tooltip("Seconds the meter stays after the dash is fully charged again.")]
        [Min(0f)] public float fullHoldSeconds = 1f;
        [Tooltip("Seconds the meter takes to fade in or out.")]
        [Min(0.01f)] public float fadeSeconds = 0.3f;
        [Tooltip("Seconds between looks for the kit's stamina strip until it is found.")]
        [Min(0.1f)] public float findInterval = 1f;

        Canvas kitStrip;
        bool kitStripWasEnabled;
        float fullSince = float.NegativeInfinity, nextFind, deniedUntil = float.NegativeInfinity;
        DashState shownState = (DashState)(-1);
        CharacterDash watched;

        void OnEnable() {
            if (group != null) group.alpha = 0f;
            nextFind = 0f;
            shownState = (DashState)(-1);
        }

        void OnDisable() {
            Unwatch();
            RestoreKitStrip();
        }

        void Watch(CharacterDash dash) {
            Unwatch();
            watched = dash;
            if (watched != null) watched.Denied += OnDenied;
        }

        void Unwatch() {
            if (watched != null) watched.Denied -= OnDenied;
            watched = null;
        }

        void OnDenied() => deniedUntil = Time.unscaledTime + deniedFlashSeconds;

        void Update() {
            HideKitStrip();
            CharacterDash dash = CharacterDash.Local;
            if (!ReferenceEquals(dash, watched)) Watch(dash);
            if (dash == null || !dash.Enabled || LocalPlayer.Character == null || LocalPlayer.Character.IsDead) {
                Fade(0f);
                return;
            }
            float charge = dash.Charge01;
            if (fill != null) {
                var anchor = fill.anchorMax;
                anchor.x = charge;
                fill.anchorMax = anchor;
            }
            DashState state = dash.State;
            bool denied = Time.unscaledTime < deniedUntil;
            if (fillImage != null) fillImage.color = denied || state == DashState.Recharging ? Color.Lerp(readyColor, lowColor, denied ? 1f : 0.6f) : readyColor;
            if (label != null && state != shownState) {
                shownState = state;
                label.text = state == DashState.Recharging ? chargingText : readyText;
                label.color = state == DashState.Recharging ? lowColor : Color.white;
            }
            bool full = dash.Charge01 >= 1f && !dash.IsDashing;
            if (!full) fullSince = float.PositiveInfinity;
            else if (float.IsPositiveInfinity(fullSince)) fullSince = Time.unscaledTime;
            Fade(Time.unscaledTime - fullSince < fullHoldSeconds || denied ? 1f : 0f);
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
