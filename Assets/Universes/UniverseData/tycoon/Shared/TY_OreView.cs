using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>Authored brick that rides the belt: its body takes each stage's material, its glow the stage's halo.</summary>
    public sealed class TY_OreView : MonoBehaviour {
        public Renderer body;
        [Tooltip("Soft pool of light on the belt under the brick, tinted per upgrade stage by the client; hidden on raw bricks.")]
        public SpriteRenderer glow;
    }
}
