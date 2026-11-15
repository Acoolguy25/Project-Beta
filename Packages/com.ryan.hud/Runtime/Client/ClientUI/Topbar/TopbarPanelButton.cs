using RyanAssets.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Topbar {
    /// <summary>
    /// A top bar button that opens and closes one <see cref="HudPanel"/> and lights up while that
    /// panel is open. Colours come from the active <see cref="UITheme"/>.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class TopbarPanelButton : MonoBehaviour {
        [SerializeField] HudPanelId panel;
        [SerializeField] Image background;
        [SerializeField] Image icon;
        [Tooltip("Optional bar under the button shown while the panel is open.")]
        [SerializeField] Graphic activeIndicator;

        Button button;

        void Awake() {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnClicked);
        }

        void OnEnable() {
            HudPanel.VisibilityChanged += OnPanelVisibilityChanged;
            UITheme.Changed += OnThemeChanged;
            Refresh(HudPanel.IsVisible(panel));
        }

        void Start() => Refresh(HudPanel.IsVisible(panel));

        void OnDisable() {
            HudPanel.VisibilityChanged -= OnPanelVisibilityChanged;
            UITheme.Changed -= OnThemeChanged;
        }

        void OnClicked() => HudPanel.Toggle(panel);

        void OnPanelVisibilityChanged(HudPanelId changed, bool visible) {
            if (changed == panel)
                Refresh(visible);
        }

        void OnThemeChanged(UITheme _) => Refresh(HudPanel.IsVisible(panel));

        void Refresh(bool open) {
            UITheme theme = UITheme.Active;
            if (background != null)
                background.color = open ? theme.surfaceRaised : theme.surface;
            if (icon != null)
                icon.color = open ? theme.accent : theme.textPrimary;
            if (activeIndicator != null) {
                activeIndicator.color = theme.accent;
                activeIndicator.enabled = open;
            }
        }
    }
}
