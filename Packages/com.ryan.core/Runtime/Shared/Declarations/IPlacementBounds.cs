using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    /// <summary>Stable bounds in the structure root's local space, excluding scaffolds and effects.</summary>
    public interface IPlacementBounds {
        Bounds LocalPlacementBounds { get; }
    }
}
