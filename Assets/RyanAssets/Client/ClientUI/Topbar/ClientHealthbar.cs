using UnityEngine;
using TMPro;
using UnityEngine.UI;
using RyanAssets.UI;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Cameras;

namespace RyanAssets.Client.ClientUI.Topbar {
    public class ClientHealthbar : MonoBehaviour{
        [SerializeField]
        TextMeshProUGUI healthText;
        [SerializeField]
        Image healthPanel;

        CanvasGroupController canvasGroupController;
        void Start() {
            canvasGroupController = GetComponent<CanvasGroupController>();
            CameraController.OnCameraTargetAdded += OnCameraTargetAdded;
            CameraController.OnCameraTargetRemoved += OnCameraTargetRemoved;
        }
        void OnDestroy() {
            CameraController.OnCameraTargetAdded -= OnCameraTargetAdded;
            CameraController.OnCameraTargetRemoved -= OnCameraTargetRemoved;
        }
        void OnCameraTargetAdded(GameCharacter targetCharacter) {
            //targetCharacter.OnDied += OnCharacterDied;
            targetCharacter.Health.OnChange += OnHealthChanged;
            targetCharacter.MaxHealth.OnChange += OnHealthChanged;
            Refresh();
        }
        void OnCameraTargetRemoved(GameCharacter oldCharacter) {
            oldCharacter.Health.OnChange -= OnHealthChanged;
            oldCharacter.MaxHealth.OnChange -= OnHealthChanged;
            Refresh();
        }
        void OnHealthChanged(long oldValue, long newValue, bool asServer) {
            Refresh();
        }
        void Refresh() {
            if (CameraController.targetCharacter != null) {
                long Health = CameraController.targetCharacter.Health.Value;
                long MaxHealth = CameraController.targetCharacter.MaxHealth.Value;
                float HealthPercent = CameraController.targetCharacter.IsFullHealth ? 1f : Mathf.Min(1f, (float) Health / MaxHealth);
                healthText.text = $"{Health}/{MaxHealth}";
                healthPanel.color = Color.LerpUnclamped(Color.red, Color.green, HealthPercent);
                healthPanel.GetComponent<RectTransform>().anchorMax = new Vector2(HealthPercent, 1);
                canvasGroupController.SetVisible(!CameraController.targetCharacter.IsFullHealth && Health != 0);
            } else
                canvasGroupController.SetVisible(false);
        }
    }
}
