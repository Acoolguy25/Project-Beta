using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// A wardrobe, cupboard or coffin an investigator can climb into. Father Crane cannot see or hear anyone inside,
    /// but if he watched them climb in, or opens it while sweeping the area, they are caught.
    /// </summary>
    public sealed class CH_HidingSpot : MonoBehaviour {
        [Tooltip("Where the hidden investigator stands (feet), inside the furniture.")]
        public Transform inside;
        [Tooltip("Where they step back out to, and where the priest stands to open it.")]
        public Transform exit;
        [Tooltip("Shown on the prompt: \"Hide in the wardrobe\".")]
        public string label = "wardrobe";

        public Vector3 Inside => inside != null ? inside.position : transform.position;
        public Vector3 Exit => exit != null ? exit.position : transform.position + transform.forward;
        /// <summary>Where the interaction prompt focuses: about chest height at the front.</summary>
        public Vector3 Handle => Exit + Vector3.up * 1.2f + (Inside - Exit) * 0.5f;
    }
}
