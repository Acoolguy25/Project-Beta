using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>Authored plot base: the parts clients relabel or toggle as the plot changes hands.</summary>
    public sealed class TY_PlotView : MonoBehaviour {
        public TMP_Text ownerLabel, bankLabel;
        [Tooltip("Shown while the plot is free.")]
        public GameObject claimPad;
        [Tooltip("Bought items and buttons are spawned under this.")]
        public Transform itemRoot;
        [Tooltip("The conveyor belt; clients scroll its texture while the plot is running.")]
        public Renderer belt;
    }
}
