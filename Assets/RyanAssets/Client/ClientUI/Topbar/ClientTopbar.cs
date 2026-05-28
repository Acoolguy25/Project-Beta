using UnityEngine;
using RyanAssets.UI;
using RyanAssets.Characters;

namespace RyanAssets.Client.ClientUI.Topbar {
    public class ClientTopbar : MonoBehaviour {
        [SerializeField]
        CanvasGroupController chatCanvas, gameSettingsCanvas, playerListCanvas;
        CanvasGroupController topbarCanvas;
        void Start() {
            topbarCanvas = GetComponent<CanvasGroupController>();
            chatCanvas.SetVisible(true);
            gameSettingsCanvas.SetVisible(false);
            playerListCanvas.SetVisible(true);
            topbarCanvas.SetVisible(true);
        }
        void OnEnable() {
            SharedInputController.menuToggledEvent += ToggleGameSettingsCanvas_ButtonPressed;
            SharedInputController.playerListEvent += TogglePlayerListCanvas_ButtonPressed;
        }
        void OnDisable() {
            SharedInputController.menuToggledEvent -= ToggleGameSettingsCanvas_ButtonPressed;
            SharedInputController.playerListEvent -= TogglePlayerListCanvas_ButtonPressed;
        }
        public void ToggleChatCanvas_ButtonPressed() {
            chatCanvas.SetVisible(chatCanvas.targetAlpha == 0f);
        }
        public void ToggleGameSettingsCanvas_ButtonPressed() {
            gameSettingsCanvas.SetVisible(gameSettingsCanvas.targetAlpha == 0f);
        }
        public void TogglePlayerListCanvas_ButtonPressed() {
            playerListCanvas.SetVisible(playerListCanvas.targetAlpha == 0f);
        }
    }
}