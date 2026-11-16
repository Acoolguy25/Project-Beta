namespace RyanAssets.GameFeel {
    /// <summary>
    /// The kit's shared sound cues. Each one maps to an <see cref="SfxSound"/> in the
    /// <see cref="SfxCueLibrary"/> (<c>GameFeel/Generated/GameFeelCues.asset</c>). Values are stored
    /// in assets, so only ever append new cues at the end.
    /// </summary>
    public enum SfxCue {
        UiClick = 0,
        UiHover = 1,
        Coin = 2,
        Pickup = 3,
        Purchase = 4,
        Build = 5,
        Hit = 6,
        Kill = 7,
        Explosion = 8,
        ExplosionLarge = 9,
        Whoosh = 10,
        CountdownBeep = 11,
        Go = 12,
        Win = 13,
        Lose = 14,
        LevelUp = 15,
        Error = 16,
        Alarm = 17,
        Footstep = 18,
        Jump = 19,
        Land = 20,
        /// <summary>A brass horn blast: round or wave start, kick-off, overtime.</summary>
        Horn = 21,
    }
}
