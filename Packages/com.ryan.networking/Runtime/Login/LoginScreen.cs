using RyanAssets.NetworkService;
using RyanAssets.UI;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace RyanAssets.Login {
    /// <summary>
    /// The account card over the game browser: "sign in" when signed out, "your account" when
    /// signed in. Continue hides it and shows the browser (<see cref="continueCanvas"/>).
    /// </summary>
    public class LoginScreen : MonoBehaviour {
        [Tooltip("Shown while signed out.")]
        [SerializeField] GameObject signInGroup;
        [Tooltip("Shown while signed in: username and Continue / Sign out.")]
        [SerializeField] GameObject accountGroup;
        [Tooltip("The screen behind the login screen (the game browser), shown on Continue.")]
        [SerializeField] CanvasGroupController continueCanvas;
        [Tooltip("The login screen's own settings screen (a private copy of the game menu).")]
        [SerializeField] HudPanel settingsPanel;
        [SerializeField, Range(0f, 1f)] float animationTime = 0.25f;

        void Start() {
            RefreshScreen();
            SetLoginScreenVisible(true, instant: true);
            if (settingsPanel != null)
                settingsPanel.SetVisible(false, instant: true);
        }

        public void RefreshScreen() {
            bool signedIn = IsSignedIn();
            signInGroup.SetActive(!signedIn);
            accountGroup.SetActive(signedIn);
        }

        /// <summary>Whether the player is signed in (always true in the Editor without network login).</summary>
        public static bool IsSignedIn() {
#if UNITY_EDITOR
            if (NetworkSettings.noNetworkLogin)
                return true;
#endif
            return UnityServices.State == ServicesInitializationState.Initialized
                && AuthenticationService.Instance.IsSignedIn;
        }

        /// <summary>The login screen sits behind the browser, so showing it hides the browser.</summary>
        public void SetLoginScreenVisible(bool visible, bool instant = false) =>
            continueCanvas.SetVisible(!visible, instant ? 0f : animationTime);

        // Buttons (wired in the prefab)
        public void Continue_ButtonPressed() => SetLoginScreenVisible(false);

        public void ToggleSettings_ButtonPressed() {
            if (settingsPanel != null)
                settingsPanel.Toggle();
        }
    }
}
