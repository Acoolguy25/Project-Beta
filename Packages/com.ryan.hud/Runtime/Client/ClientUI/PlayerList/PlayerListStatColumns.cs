using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.PlayerList {
    /// <summary>
    /// The right-aligned stat cells of a player list row or header. Cells are clones of
    /// <see cref="cellTemplate"/>, kept in a fixed-width layout so headers and rows line up.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerListStatColumns : MonoBehaviour {
        [Tooltip("Inactive label cloned for each column.")]
        [SerializeField] TMP_Text cellTemplate;

        readonly List<TMP_Text> cells = new();

        public int Count => cells.Count;

        public void SetCount(int count) {
            while (cells.Count < count) {
                TMP_Text cell = Instantiate(cellTemplate, transform, false);
                cell.name = $"Stat{cells.Count}";
                cell.gameObject.SetActive(true);
                cells.Add(cell);
            }
            while (cells.Count > count) {
                TMP_Text last = cells[^1];
                cells.RemoveAt(cells.Count - 1);
                Destroy(last.gameObject);
            }
        }

        public void Set(int index, string value) {
            if (index >= 0 && index < cells.Count)
                cells[index].text = value;
        }

        public void Set(int index, int value) => Set(index, FormatStat(value));

        /// <summary>1234 -> "1,234"; 12345 -> "12.3K"; 1234567 -> "1.23M". Keeps cells narrow.</summary>
        public static string FormatStat(long value) {
            long magnitude = value < 0 ? -value : value;
            if (magnitude < 10_000)
                return value.ToString("N0", CultureInfo.InvariantCulture);
            if (magnitude < 1_000_000)
                return (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            if (magnitude < 1_000_000_000)
                return (value / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            return (value / 1_000_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "B";
        }
    }
}
