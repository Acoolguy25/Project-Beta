using FishNet.Object;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
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
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved += OnCharacterRemoved;
            StaminaController.StaminaChanged += OnStaminaChanged;
        }
        private void OnDestroy() {
            StaminaController.StaminaChanged -= OnStaminaChanged;
            LocalPlayer.OnCharacterRemoved -= OnCharacterRemoved;
        }
        void OnCharacterAdded(LocalCharacter character) {
            localCharacter = character.GetComponent<LocalCharacter>();
            //localCharacter.StaminaChanged += (_) => OnStaminaChanged(false);
            localCharacter.OnDied += OnCharacterDied;
            OnStaminaChanged(true);
            SetVisible(true);
        }
        void OnCharacterDied(RyanAssets.Shared.Declarations.DamageType source, NetworkObject sourceObject) {
            SetVisible();
        }
        void OnCharacterRemoved(LocalCharacter oldCharacter) {
            SetVisible();
        }
        void SetVisible(bool visible = false) {
            canvasGroupController.SetVisible(visible, 0.5f);
        }
        void OnStaminaChanged(float newStamina) {
            OnStaminaChanged(false);
        }
        void OnStaminaChanged(bool Instant) {
            //TweenRectTransform.AnchorTween(staminaSlider.rectTransform, Instant ? 0 : 0.25f, Vector2.zero, new Vector2(localCharacter.Stamina / localCharacter.MaxStamina.Value, 1));
            if (!StaminaController.StaminaLoaded)
                return;
            // Always keep it instant
            staminaSlider.rectTransform.anchorMax = new Vector2(StaminaController.Stamina / StaminaController.MaxStamina, 1);
        }
    }
}
