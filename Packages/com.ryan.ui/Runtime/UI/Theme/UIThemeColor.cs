using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI {
    /// <summary>
    /// Paints a <see cref="Graphic"/> (Image, TMP text, ...) with a colour slot of the active
    /// <see cref="UITheme"/>, so recolouring the theme asset recolours every element that uses the slot.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Graphic))]
    public class UIThemeColor : MonoBehaviour {
        [SerializeField] UIColorRole role = UIColorRole.Surface;
        [Tooltip("Multiplied into the slot's alpha, for fainter variants of a colour.")]
        [SerializeField, Range(0f, 1f)] float opacity = 1f;

        Graphic graphic;

        public UIColorRole Role {
            get => role;
            set { role = value; Apply(); }
        }

        public float Opacity {
            get => opacity;
            set { opacity = value; Apply(); }
        }

        void OnEnable() {
            UITheme.Changed += OnThemeChanged;
            Apply();
        }

        void OnDisable() => UITheme.Changed -= OnThemeChanged;

        void OnValidate() => Apply();

        void OnThemeChanged(UITheme _) => Apply();

        public void Apply() {
            if (graphic == null)
                graphic = GetComponent<Graphic>();
            if (graphic == null)
                return;
            Color color = UITheme.Active.Get(role);
            color.a *= opacity;
            graphic.color = color;
        }
    }
}
