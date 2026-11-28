namespace Universes.UniverseData.capture_the_flag {
    /// <summary>
    /// The default XP and gold of Flag Wars' rewards: the server runner's serialized fields start from
    /// these, and the pacing test models a day of play with the same numbers.
    /// </summary>
    public static class CTF_RewardDefaults {
        public const int KillXp = 10, FlagTakeXp = 10, CaptureXp = 50, ReturnXp = 15;
        public const int WinXp = 100, WinGold = 25, LossXp = 40, LossGold = 5, DrawXp = 60, DrawGold = 10;
        /// <summary>Gold per flag captured, paid on the summary.</summary>
        public const int CaptureGold = 20;
    }
}
