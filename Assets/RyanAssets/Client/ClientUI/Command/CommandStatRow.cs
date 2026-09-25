using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>One "label ... value" line on a <see cref="SelectionInfoPanel"/>.</summary>
    public sealed class CommandStatRow : MonoBehaviour {
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] TextMeshProUGUI value;

        public void Bind(CommandStat stat) {
            if (label != null)
                label.text = stat.Label ?? string.Empty;
            if (value != null)
                value.text = stat.Value ?? string.Empty;
        }
    }
}
