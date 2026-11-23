namespace Universes.UniverseData.murder_mystery {
    /// <summary>How a Classic round is decided, and small round rules shared by the server and the tests.</summary>
    public static class MM_RoundRules {
        /// <summary>
        /// The round's outcome so far: the innocents win as soon as every murderer is down (even if the last
        /// innocent fell in the same moment) or when time runs out with survivors; the murderer wins once
        /// nobody else is left. <see cref="MM_Outcome.None"/> while it is still being played.
        /// </summary>
        public static MM_Outcome Decide(int murderersAlive, int survivorsAlive, bool timeUp) {
            if (murderersAlive <= 0)
                return MM_Outcome.MurdererStopped;
            if (survivorsAlive <= 0)
                return MM_Outcome.MurdererWon;
            return timeUp ? MM_Outcome.InnocentsSurvived : MM_Outcome.None;
        }

        /// <summary>Whether someone dealt <paramref name="role"/> is on the winning side of <paramref name="outcome"/>.</summary>
        public static bool IsWinner(MM_Role role, MM_Outcome outcome) => outcome switch {
            MM_Outcome.MurdererWon => role == MM_Role.Murderer,
            MM_Outcome.MurdererStopped or MM_Outcome.InnocentsSurvived => role is MM_Role.Innocent or MM_Role.Sheriff,
            _ => false,
        };

        /// <summary>Whether a bag holding <paramref name="coins"/> has room for one more.</summary>
        public static bool BagHasRoom(int coins, int bagSize) => coins < bagSize;

        /// <summary>"Every murderer was caught" etc., the line under the results headline.</summary>
        public static string Reason(MM_Outcome outcome, string stoppedBy) => outcome switch {
            MM_Outcome.MurdererStopped => string.IsNullOrEmpty(stoppedBy) ? "The murderer is dead" : $"{stoppedBy} stopped the murderer",
            MM_Outcome.InnocentsSurvived => "The innocents outlasted the murderer",
            MM_Outcome.MurdererWon => "Nobody was left to stop the murderer",
            _ => string.Empty,
        };
    }
}
