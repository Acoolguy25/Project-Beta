using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>
    /// Ground that carries a standing character without moving itself, such as a conveyor belt.
    /// Character movement adds the returned velocity while the character stands on the collider.
    /// </summary>
    public interface ISurfaceVelocity {
        Vector3 GetSurfaceVelocity(Vector3 characterPosition);
    }

    /// <summary>
    /// Ground with less grip than normal (ice). While a character stands on it, steering changes its
    /// planar velocity only gradually, so it slides when it starts, stops or turns.
    /// </summary>
    public interface ISurfaceTraction {
        /// <summary>Share of normal grip, from 0 (none) to 1 (normal ground).</summary>
        float Traction { get; }
    }

    /// <summary>A wall an airborne character can kick off with a fresh jump press.</summary>
    public interface IWallJumpSurface {
        /// <summary>Vertical speed a wall jump starts with.</summary>
        float UpSpeed { get; }
        /// <summary>Horizontal speed away from the wall a wall jump starts with.</summary>
        float AwaySpeed { get; }
    }

    /// <summary>A moving obstacle that knocks characters flying when it hits them, such as a spinner bar.</summary>
    public interface IKnockbackSurface {
        /// <summary>
        /// Velocity to give a character hit at <paramref name="point"/> (<paramref name="up"/> is the
        /// character's gravity up), or zero when the hit is too soft to count.
        /// </summary>
        Vector3 GetKnockback(Vector3 point, Vector3 up);
    }
}
