using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// The pet crate on the owner's plot (authored by TY_Authoring.Pets): a brick crate with a lid, a sign
    /// naming its price, the collection count and the pity countdown, and a glow while it is affordable.
    /// Clients show it only on the local player's own plot; the server decides what it hatches.
    /// </summary>
    public sealed class TY_CrateView : MonoBehaviour {
        [Tooltip("The crate body the hatch shakes, and the lid it throws open.")]
        public Transform body, lid;
        [Tooltip("Light and sparkle shown while the player can afford a crate.")]
        public GameObject readyGlow;
        public TMP_Text label;
        [Tooltip("{0} = price, {1} = pets found, {2} = pets in total, {3} = the pity line (may be empty).")]
        public string format = "PET CRATE  <color={4}>${0}</color>\n<size=58%>Found {1}/{2}{3}</size>";
        public Color affordableColor = new(0.35f, 1f, 0.5f), expensiveColor = new(1f, 0.55f, 0.5f);

        string shown;

        public void Show(long price, bool affordable, int found, int total, int untilPity) {
            string pity = untilPity > 0 ? $"  ·  Epic+ in {untilPity}" : "";
            string colour = "#" + ColorUtility.ToHtmlStringRGB(affordable ? affordableColor : expensiveColor);
            string text = string.Format(format, TY_Catalog.Money(price), found, total, pity, colour);
            if (readyGlow != null && readyGlow.activeSelf != affordable) readyGlow.SetActive(affordable);
            if (text == shown || label == null) return;
            shown = text;
            label.text = text;
        }
    }
}
