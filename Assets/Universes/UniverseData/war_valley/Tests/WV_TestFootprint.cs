using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Stands in for a structure that declares its grid footprint, without its network components.</summary>
    public sealed class WV_TestFootprint : MonoBehaviour, IGridFootprint {
        public int Cells;

        public int FootprintCells => Cells;
    }
}
