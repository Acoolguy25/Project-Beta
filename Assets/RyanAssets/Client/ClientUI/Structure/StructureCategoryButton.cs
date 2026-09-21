using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Build {
    /// <summary>
    /// One tab in the structure menu's category sidebar.
    /// <para>
    /// The sidebar is built from the categories the live build list actually contains, so this is an
    /// authored prefab the menu clones per category rather than a fixed set of buttons. Its label and
    /// selection stripe are wired in the Inspector so the menu never searches for them by name.
    /// </para>
    /// </summary>
    public class StructureCategoryButton : MonoBehaviour {
        [SerializeField] private Button button;
        [SerializeField] private TextMeshProUGUI label;
        [Tooltip("Stripe shown only while this category is the selected one.")]
        [SerializeField] private GameObject selectedIndicator;

        [Header("Label Colours")]
        [SerializeField] private Color selectedColor = new(1f, 0.82f, 0.35f, 1f);
        [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.6f);

        /// <summary>The category this tab filters to, or null for the "all items" tab.</summary>
        public string Category { get; private set; }

        public void Bind(string category, string text, UnityAction onClick) {
            Category = category;
            if (label != null)
                label.text = text;
            if (button != null) {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(onClick);
            }
        }

        public void SetSelected(bool selected) {
            if (selectedIndicator != null)
                selectedIndicator.SetActive(selected);
            if (label != null)
                label.color = selected ? selectedColor : normalColor;
        }
    }
}
