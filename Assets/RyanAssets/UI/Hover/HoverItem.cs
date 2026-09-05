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
    }
}
