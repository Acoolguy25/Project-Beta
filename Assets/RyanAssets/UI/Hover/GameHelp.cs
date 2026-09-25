using System;

namespace RyanAssets.UI.Hover {
    /// <summary>
    /// Whether game modes show their own help: the tooltips that explain a mode's controls and the
    /// instruction lines on its HUD. General interface tooltips are unaffected.
    /// <para>
    /// The value is the player's "Game Help" setting. It lives here, below both the settings UI and
    /// every game mode, so a mode can honour it without depending on the settings screen, and the
    /// settings screen can publish it without knowing which modes exist.
    /// </para>
    /// </summary>
    public static class GameHelp {
        static bool enabled = true;

        /// <summary>Raised with the new value whenever the player changes the setting.</summary>
        public static event Action<bool> Changed;

        public static bool Enabled {
            get => enabled;
            set {
                if (enabled == value)
                    return;
                enabled = value;
                Changed?.Invoke(value);
            }
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            enabled = true;
            Changed = null;
        }
    }
}
