namespace RyanAssets.Shared.Declarations {
    /// <summary>
    /// A structure that states how many build-grid cells it spans instead of having the span measured
    /// from its bounds.
    /// <para>
    /// The span decides where a structure snaps: an odd number of cells centres it in a cell, an even
    /// number on a grid line. That is right for a building, whose bounds fill its cells, but not for a
    /// thin post meant to cap the grid point where two fence runs meet - measured, it is one cell wide
    /// and would snap to a cell's centre, off the corner it is for. Placement on the client and the
    /// server both read this from the same prefab, so they always agree.
    /// </para>
    /// </summary>
    public interface IGridFootprint {
        /// <summary>Cells spanned on the wider side. Zero or less means "measure it from the bounds".</summary>
        int FootprintCells { get; }
    }
}
