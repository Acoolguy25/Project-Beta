using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using RyanAssets.TweenService.TweenComponents;
using RyanAssets.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Stamina {
    public class StaminaUI : MonoBehaviour {
        [SerializeField]
        Image staminaSlider;
        [SerializeField, Min(0f), Tooltip("How long the bar remains visible after stamina reaches its maximum.")]
        float fullStaminaFadeDelay = 1f;
        [SerializeField, Min(0f), Tooltip("How long the stamina bar takes to fade in or out.")]
        float visibilityFadeDuration = 0.5f;
        LocalCharacter localCharacter;
        CanvasGroupController canvasGroupController;
        Coroutine fullStaminaFadeCoroutine;
        void Start() {
            canvasGroupController = GetComponent<CanvasGroupController>();
            SetVisible();
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved += OnCharacterRemoved;
            StaminaController.StaminaChanged += OnStaminaChanged;
        }
        private void OnDestroy() {
            StaminaController.StaminaChanged -= OnStaminaChanged;
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved -= OnCharacterRemoved;
            if (localCharacter != null) {
                localCharacter.OnDied -= OnCharacterDied;
                localCharacter.OnRevive -= OnCharacterRevived;
            }
            CancelFullStaminaFade();
        }
        void OnCharacterAdded(LocalCharacter character) {
            localCharacter = character.GetComponent<LocalCharacter>();
            //localCharacter.StaminaChanged += (_) => OnStaminaChanged(false);
            localCharacter.OnDied += OnCharacterDied;
            localCharacter.OnRevive += OnCharacterRevived;
            OnStaminaChanged(true);
            SetVisible(true);
            UpdateVisibilityForStamina();
        }
        void OnCharacterDied(RyanAssets.Shared.Declarations.DamageType source, IEntity sourceEntity) {
            CancelFullStaminaFade();
            SetVisible();
        }
        void OnCharacterRevived() {
            OnStaminaChanged(true);
            SetVisible(true);
            UpdateVisibilityForStamina();
        }
        void OnCharacterRemoved(LocalCharacter oldCharacter) {
            if (oldCharacter != null) {
                oldCharacter.OnDied -= OnCharacterDied;
                oldCharacter.OnRevive -= OnCharacterRevived;
            }
            if (localCharacter == oldCharacter)
                localCharacter = null;
            CancelFullStaminaFade();
            SetVisible();
        }
        void SetVisible(bool visible = false) {
            canvasGroupController.SetVisible(visible, visibilityFadeDuration);
        }
        void OnStaminaChanged(float newStamina) {
            OnStaminaChanged(false);
            UpdateVisibilityForStamina();
        }
        void OnStaminaChanged(bool Instant) {
            //TweenRectTransform.AnchorTween(staminaSlider.rectTransform, Instant ? 0 : 0.25f, Vector2.zero, new Vector2(localCharacter.Stamina / localCharacter.MaxStamina.Value, 1));
            if (!StaminaController.StaminaLoaded)
                return;
            // Always keep it instant
            staminaSlider.rectTransform.anchorMax = new Vector2(StaminaController.Stamina / StaminaController.MaxStamina, 1);
        }
        void UpdateVisibilityForStamina() {
            if (!StaminaController.StaminaLoaded || !StaminaController.StaminaEnabled)
                return;

            if (StaminaController.Stamina >= StaminaController.MaxStamina) {
                if (fullStaminaFadeCoroutine == null)
                    fullStaminaFadeCoroutine = StartCoroutine(FadeAfterFullStaminaDelay());
                return;
            }

            CancelFullStaminaFade();
            SetVisible(true);
        }
        IEnumerator FadeAfterFullStaminaDelay() {
            yield return new WaitForSeconds(fullStaminaFadeDelay);
            fullStaminaFadeCoroutine = null;
            if (StaminaController.StaminaLoaded && StaminaController.StaminaEnabled && StaminaController.Stamina >= StaminaController.MaxStamina)
                SetVisible();
        }
        void CancelFullStaminaFade() {
            if (fullStaminaFadeCoroutine == null)
                return;
            StopCoroutine(fullStaminaFadeCoroutine);
            fullStaminaFadeCoroutine = null;
        }
    }
}
