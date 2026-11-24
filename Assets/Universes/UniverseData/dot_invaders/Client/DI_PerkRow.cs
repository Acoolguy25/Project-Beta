#if !UNITY_SERVER
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>How a perk row reads in the panel.</summary>
    public enum DI_PerkRowState {
        Locked,
        Available,
        Equipped,
    }

    /// <summary>
    /// One perk in the perk panel: icon, name, what it does and whether it is equipped, free to equip
    /// or still locked. Cloned from the template row DI_Authoring builds; presentation only.
    /// </summary>
    public sealed class DI_PerkRow : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text description;
        [SerializeField] TMP_Text status;
        [Tooltip("Accent stripe shown while the perk is equipped.")]
        [SerializeField] Image equippedMark;
        [SerializeField] CanvasGroup group;

        Action<string> clicked;

        public string PerkId { get; private set; }

        void Awake() {
            if (button != null)
                button.onClick.AddListener(OnClick);
        }

        void OnDestroy() {
            if (button != null)
                button.onClick.RemoveListener(OnClick);
        }

        public void Bind(DI_PerkDefinition perk, Sprite sprite, Action<string> onClick) {
            PerkId = perk.Id;
            clicked = onClick;
            gameObject.name = perk.Id;
            if (icon != null && sprite != null)
                icon.sprite = sprite;
            title.text = perk.Title;
            description.text = perk.Description;
        }

        public void SetState(DI_PerkRowState state, string statusText, Color statusColor, Color accent, float lockedAlpha) {
            status.text = statusText;
            status.color = statusColor;
            if (equippedMark != null) {
                equippedMark.enabled = state == DI_PerkRowState.Equipped;
                equippedMark.color = accent;
            }
            if (icon != null)
                icon.color = state == DI_PerkRowState.Locked ? statusColor : accent;
            if (group != null)
                group.alpha = state == DI_PerkRowState.Locked ? lockedAlpha : 1f;
            if (button != null)
                button.interactable = state != DI_PerkRowState.Locked;
        }

        void OnClick() {
            if (!string.IsNullOrEmpty(PerkId))
                clicked?.Invoke(PerkId);
        }
    }
}
#endif
