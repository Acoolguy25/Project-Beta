using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Marks a wall the character can kick off: touch it in the air and press jump again.
    /// Speeds are set per wall so a level can tune them to its gravity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallJumpSurface : MonoBehaviour, IWallJumpSurface {
        [Min(0f)] public float upSpeed = 11f;
        [Min(0f)] public float awaySpeed = 7f;
        public float UpSpeed => upSpeed;
        public float AwaySpeed => awaySpeed;

        public void Configure(float up, float away) {
            upSpeed = Mathf.Max(0f, up);
            awaySpeed = Mathf.Max(0f, away);
        }
    }
}
