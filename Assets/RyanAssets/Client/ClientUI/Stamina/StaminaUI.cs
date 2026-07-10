using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.TweenService.TweenComponents;
using RyanAssets.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Stamina {
    public class StaminaUI : MonoBehaviour {
        [SerializeField]
        Image staminaSlider;
        LocalCharacter localCharacter;
        CanvasGroupController canvasGroupController;
        void Start() {
            canvasGroupController = GetComponent<CanvasGroupController>();
            LocalPlayer.Instance.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }
        void OnCharacterAdded(Transform character) {
            canvasGroupController.SetVisible(character != null, 0.5f);
            if (character == null) {
                OnCharacterRemoved();
                return;
            }
            localCharacter = character.GetComponent<LocalCharacter>();
            localCharacter.StaminaChanged += (_) => OnStaminaChanged(false);
            localCharacter.OnDied += OnCharacterDied;
            OnStaminaChanged(true);
            SetVisible(true);
        }
        void OnCharacterDied(DamageSource source) {
            SetVisible();
        }
        void OnCharacterRemoved() {
            SetVisible();
        }
        void SetVisible(bool visible = false) {
            canvasGroupController.SetVisible(false, 0.5f);
        }
        void OnStaminaChanged(bool Instant) {
            TweenRectTransform.AnchorTween(staminaSlider.rectTransform, Instant ? 0 : 0.25f, Vector2.zero, new Vector2(localCharacter.Stamina / localCharacter.MaxStamina.Value, 1));
        }
    }
}
