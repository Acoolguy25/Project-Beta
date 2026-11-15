using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>
    /// A short stack of event lines ("Red took the Blue flag!") that fade out on their own. Rows are
    /// pooled copies of the inactive <see cref="template"/> child; the newest is on top.
    /// </summary>
    public sealed class HudToastFeed : MonoBehaviour {
        [Tooltip("Inactive row with a HudToast component; copied for every visible toast.")]
        public HudToast template;
        [Min(1)] public int maxVisible = 4;
        [Min(0.5f)] public float defaultSeconds = 4f;

        readonly List<HudToast> rows = new();

        /// <summary>Adds a toast at the top of the feed.</summary>
        public void Push(Sprite icon, Color accent, string text, float seconds = 0f) {
            HudToast row = null;
            foreach (HudToast candidate in rows)
                if (!candidate.gameObject.activeSelf) { row = candidate; break; }
            if (row == null && rows.Count < maxVisible) {
                row = Instantiate(template, template.transform.parent);
                row.name = "Toast";
                rows.Add(row);
            }
            if (row == null) {
                // Feed is full: reuse the oldest row (the bottom one).
                row = rows[0];
                foreach (HudToast candidate in rows)
                    if (candidate.transform.GetSiblingIndex() > row.transform.GetSiblingIndex()) row = candidate;
                row.Popup.HideImmediate();
            }
            row.Set(icon, accent, text);
            row.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            row.Popup.Show(seconds > 0f ? seconds : defaultSeconds);
        }

        public void Clear() {
            foreach (HudToast row in rows)
                row.Popup.HideImmediate();
        }
    }
}
