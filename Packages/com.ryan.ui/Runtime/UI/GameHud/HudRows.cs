using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>
    /// A list of identical rows (scoreboards, result tables) made from an inactive template row.
    /// Call <see cref="SetCount"/>, then fill each <see cref="HudRow"/> from <see cref="Row"/>.
    /// </summary>
    public sealed class HudRows : MonoBehaviour {
        [Tooltip("Inactive row; copied for each row shown.")]
        public HudRow template;

        readonly List<HudRow> rows = new();

        public int Count { get; private set; }

        public void SetCount(int count) {
            Count = Mathf.Max(0, count);
            while (rows.Count < Count) {
                HudRow row = Instantiate(template, template.transform.parent);
                row.name = $"Row {rows.Count + 1}";
                rows.Add(row);
            }
            for (int i = 0; i < rows.Count; i++) {
                bool used = i < Count;
                if (rows[i].gameObject.activeSelf != used)
                    rows[i].gameObject.SetActive(used);
                if (used && rows[i].stripe != null)
                    rows[i].stripe.enabled = i % 2 == 1;
            }
        }

        public HudRow Row(int index) => rows[index];
    }
}
