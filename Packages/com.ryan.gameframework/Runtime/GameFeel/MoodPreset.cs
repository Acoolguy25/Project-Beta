namespace RyanAssets.GameFeel {
    /// <summary>Post-processing looks generated as URP Volume profiles (see <see cref="MoodVolume"/>).</summary>
    public enum MoodPreset {
        /// <summary>Saturated, bright, soft bloom: obbies, tycoons, arcade games.</summary>
        ArcadeBright = 0,
        /// <summary>Warm white balance, gentle contrast and vignette: houses, shops, lobbies.</summary>
        WarmIndoor = 1,
        /// <summary>Cool, darker, bloom on lights: night maps.</summary>
        Night = 2,
        /// <summary>Desaturated, dark, heavy vignette and grain: horror.</summary>
        Horror = 3,
    }
}
