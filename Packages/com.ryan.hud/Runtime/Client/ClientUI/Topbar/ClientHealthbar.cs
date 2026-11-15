using RyanAssets.Cameras;
using RyanAssets.Characters.Shared;
using RyanAssets.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Topbar {
    /// <summary>
    /// Compact health bar for the character the camera follows. It stays hidden at full health,
    /// so it only takes screen space while it matters.
    /// </summary>
    [RequireComponent(typeof(CanvasGroupController))]
    public class ClientHealthbar : MonoBehaviour {
        [SerializeField] TMP_Text healthText;
        [Tooltip("Fill image; its right anchor follows the health fraction.")]
        [SerializeField] Image fill;
        [Tooltip("Below this fraction the bar uses the theme's warning colour, and below half of it the danger colour.")]
        [SerializeField, Range(0f, 1f)] float lowHealthFraction = 0.5f;

        CanvasGroupController visibility;
        GameCharacter observed;

        void Awake() => visibility = GetComponent<CanvasGroupController>();

        void Start() {
            CameraController.OnCameraTargetAdded += Observe;
            CameraController.OnCameraTargetRemoved += StopObserving;
            if (CameraController.targetCharacter != null)
                Observe(CameraController.targetCharacter);
            Refresh();
        }

        void OnDestroy() {
            CameraController.OnCameraTargetAdded -= Observe;
            CameraController.OnCameraTargetRemoved -= StopObserving;
            StopObserving(observed);
        }

        void Observe(GameCharacter character) {
            StopObserving(observed);
            observed = character;
            character.Health.OnChange += OnHealthChanged;
            character.MaxHealth.OnChange += OnHealthChanged;
            Refresh();
        }

        void StopObserving(GameCharacter character) {
            if (character == null || character != observed)
                return;
            character.Health.OnChange -= OnHealthChanged;
            character.MaxHealth.OnChange -= OnHealthChanged;
            observed = null;
            Refresh();
        }

        void OnHealthChanged(long oldValue, long newValue, bool asServer) => Refresh();

        void Refresh() {
            if (observed == null) {
                visibility.SetVisible(false);
                return;
            }
            long health = observed.Health.Value;
            long maxHealth = observed.MaxHealth.Value;
            float fraction = observed.IsFullHealth || maxHealth <= 0 ? 1f : Mathf.Clamp01((float)health / maxHealth);

            UITheme theme = UITheme.Active;
            if (healthText != null)
                healthText.text = $"{health}<color=#{UITheme.ToRichTextHex(theme.textMuted)}>/{maxHealth}</color>";
            if (fill != null) {
                fill.color = fraction > lowHealthFraction ? theme.success
                    : fraction > lowHealthFraction * 0.5f ? theme.warning
                    : theme.danger;
                fill.rectTransform.anchorMax = new Vector2(fraction, 1f);
            }
            visibility.SetVisible(!observed.IsFullHealth && health > 0, theme.fadeDuration);
        }
    }
}
