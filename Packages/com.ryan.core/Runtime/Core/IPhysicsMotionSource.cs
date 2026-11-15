using UnityEngine;

namespace RyanAssets.Core {
    /// <summary>Exposes the pose scheduled for the current physics step to grounded riders.</summary>
    public interface IPhysicsMotionSource {
        bool HasScheduledPose { get; }
        Vector3 ScheduledPosition { get; }
        Quaternion ScheduledRotation { get; }
    }
}
