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

        /// <summary>
        /// The colour this component last painted. If the graphic no longer has it when re-enabled, a
        /// script recoloured it (team, role, result) and that colour is kept instead of the slot's.
        /// </summary>
        [SerializeField, HideInInspector] Color painted;
        [SerializeField, HideInInspector] bool hasPainted;

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
            if (graphic == null)
                graphic = GetComponent<Graphic>();
            bool recolouredByScript = hasPainted && graphic != null && graphic.color != painted;
            if (!recolouredByScript)
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
            painted = color;
            hasPainted = true;
        }
    }
}
