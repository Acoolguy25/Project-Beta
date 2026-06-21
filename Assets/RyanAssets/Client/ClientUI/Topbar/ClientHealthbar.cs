using UnityEngine;
using TMPro;
using UnityEngine.UI;
using RyanAssets.UI;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;

namespace RyanAssets.Client.ClientUI.Topbar {
    public class ClientHealthbar : MonoBehaviour{
        [SerializeField]
        TextMeshProUGUI healthText;
        [SerializeField]
        Image healthPanel;

        CanvasGroupController canvasGroupController;
        LocalCharacter localCharacter;
        void Start() {
            canvasGroupController = GetComponent<CanvasGroupController>();
            LocalPlayer.Instance.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }
        void OnDestroy() {
            LocalPlayer.Instance.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
        }
        void OnCharacterAdded(Transform character) {
            localCharacter = character?.GetComponent<LocalCharacter>();
            //localCharacter.OnDied += OnCharacterDied;
            if (localCharacter != null) {
                localCharacter.Health.OnChange += (_, _, _) => Refresh();
                localCharacter.MaxHealth.OnChange += (_, _, _) => Refresh();
            }
            Refresh();
        }
        void Refresh() {
            if (localCharacter != null) {
                long Health = localCharacter.Health.Value;
                long MaxHealth = localCharacter.MaxHealth.Value;
                float HealthPercent = localCharacter.IsFullHealth() ? 1f : Mathf.Min(1f, (float) Health / MaxHealth);
                healthText.text = $"{Health}/{MaxHealth}";
                healthPanel.color = Color.LerpUnclamped(Color.red, Color.green, HealthPercent);
                healthPanel.GetComponent<RectTransform>().anchorMax = new Vector2(HealthPercent, 1);
                canvasGroupController.SetVisible(!localCharacter.IsFullHealth() && Health != 0);
            } else
                canvasGroupController.SetVisible(false);
        }
    }
}
