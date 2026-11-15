using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>
    /// Ground that carries a standing character without moving itself, such as a conveyor belt.
    /// Character movement adds the returned velocity while the character stands on the collider.
    /// </summary>
    public interface ISurfaceVelocity {
        Vector3 GetSurfaceVelocity(Vector3 characterPosition);
    }

    /// <summary>A wall an airborne character can kick off with a fresh jump press.</summary>
    public interface IWallJumpSurface {
        /// <summary>Vertical speed a wall jump starts with.</summary>
        float UpSpeed { get; }
        /// <summary>Horizontal speed away from the wall a wall jump starts with.</summary>
        float AwaySpeed { get; }
    }
}
