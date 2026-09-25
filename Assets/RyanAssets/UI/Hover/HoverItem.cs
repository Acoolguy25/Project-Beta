using UnityEngine;

namespace RyanAssets.UI.Hover
{
    /// <summary>Marks a UI or world object as having hover text.</summary>
    [DisallowMultipleComponent]
    public sealed class HoverItem : MonoBehaviour
    {
        [SerializeField, TextArea(1, 8)]
        [Tooltip("Text displayed after the cursor rests over this object.")]
        string hoverText;

        public string HoverText => hoverText;

        /// <summary>
        /// Replaces the text at runtime, for items whose detail is live data - a price that becomes
        /// unaffordable, a requirement that gets researched. The manager re-reads the text every
        /// frame, so an open tooltip updates in place.
        /// </summary>
        public void SetText(string text) => hoverText = text;
    }
}
