using UnityEngine;
using RyanAssets.UI;
using RyanAssets.Input;
using UnityEngine.UI;
using RyanAssets.Client.ClientCore;

namespace RyanAssets.Client.ClientUI.Topbar {
    public class ClientTopbar : MonoBehaviour {
        public static ClientTopbar Instance;
        [SerializeField]
        CanvasGroupController chatCanvas, gameSettingsCanvas, playerListCanvas;
        CanvasGroupController topbarCanvas;
        [SerializeField]
        Button chatButton, gameSettingsButton, playerListButton;
        [SerializeField]
        Text experienceText;
        void Awake() {
            Instance = this;
            topbarCanvas = GetComponent<CanvasGroupController>();
        }
        void OnEnable() {
            TopbarControls.menuToggledEvent += ToggleGameSettingsCanvas_ButtonPressed;
            TopbarControls.playerListEvent += TogglePlayerListCanvas_ButtonPressed;
            SetCanvasVisibility(chatCanvas, chatButton, true, 0f);
            SetCanvasVisibility(gameSettingsCanvas, gameSettingsButton, false, 0f);
            SetCanvasVisibility(playerListCanvas, playerListButton, true, 0f);
            SetCanvasVisibility(topbarCanvas, null, true, 0f);
            experienceText.text = ClientConnector.joinUniverseId;
        }
        void OnDisable() {
            TopbarControls.menuToggledEvent -= ToggleGameSettingsCanvas_ButtonPressed;
            TopbarControls.playerListEvent -= TogglePlayerListCanvas_ButtonPressed;
        }
        public Button GetButton(CanvasGroupController canvas){
            Button button;
            button = canvas switch {
                var c when c == chatCanvas => chatButton,
                var c when c == gameSettingsCanvas => gameSettingsButton,
                var c when c == playerListCanvas => playerListButton,
                _ => null
            };
            return button;
        }
        public void EnsureCanvasVisibility(CanvasGroupController canvas, bool visibility = true, bool instant = true) {
            SetCanvasVisibility(canvas, GetButton(canvas), visibility, instant? 0f: 1/3f);
        }
        public void CloseCanvasVisibility(CanvasGroupController canvas){
            SetCanvasVisibility(canvas, GetButton(canvas), false, 1/3f);
        }
        public void SetCanvasVisibility(CanvasGroupController canvas, Button button, bool newVisible, float duration) {
            if (canvas == gameSettingsCanvas)
                InputService.SetInputScreenActive(InputScreen.GameSettings, newVisible);

            canvas.SetVisible(newVisible, duration);
            if (button)
                button.GetComponent<Image>().color = newVisible ? new Color32(0x14, 0x7D, 0xC5, 0x73) : new Color32(0x14, 0x14, 0x14, 0x73);
        }
        public void ToggleCanvasVisibility(CanvasGroupController canvas, Button button, float duration = 1 / 3f) {
            bool newVisible = canvas.targetAlpha == 0f;
            SetCanvasVisibility(canvas, button, newVisible, duration);
        }
        public void ToggleChatCanvas_ButtonPressed() {
            ToggleCanvasVisibility(chatCanvas, chatButton);
        }
        public void ToggleGameSettingsCanvas_ButtonPressed() {
            ToggleCanvasVisibility(gameSettingsCanvas, gameSettingsButton);
        }
        public void TogglePlayerListCanvas_ButtonPressed() {
            ToggleCanvasVisibility(playerListCanvas, playerListButton);
        }
    }
}
