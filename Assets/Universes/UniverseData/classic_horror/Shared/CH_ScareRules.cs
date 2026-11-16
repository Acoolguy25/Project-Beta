namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// The decisions behind encounter scares, kept free of Unity and networking so they can be
    /// tested: when the Presence shows itself as a warning, and how warnings and discoveries are
    /// rationed per investigator.
    /// </summary>
    public static class CH_ScareRules {
        /// <summary>
        /// A warning plays when the Presence hunts someone it can see inside the scare distance, but
        /// not inside its strike range: there the fatal scare is the one that plays, and a warning
        /// would only stack on top of it.
        /// </summary>
        public static bool ShouldWarn(bool visible, float distance, float strikeDistance, float scareDistance) =>
            visible && distance >= strikeDistance && distance < scareDistance;

        /// <summary>
        /// Whether a non-fatal scare may play for an investigator who has seen <paramref name="shown"/>
        /// of them this case, the last at <paramref name="lastShown"/> seconds, at time <paramref name="now"/>.
        /// </summary>
        public static bool CanShowNonFatal(int shown, float lastShown, float now, int perCase, float cooldown) =>
            shown < perCase && (shown == 0 || now - lastShown >= cooldown);
    }

    /// <summary>One investigator's non-fatal scares this case: how many, and when the last one played.</summary>
    public struct CH_ScareRation {
        public int Shown;
        public float LastShown;

        /// <summary>Records a scare at <paramref name="now"/> if the rules allow one, and says whether it did.</summary>
        public bool TryTake(float now, int perCase, float cooldown) {
            if (!CH_ScareRules.CanShowNonFatal(Shown, LastShown, now, perCase, cooldown)) return false;
            Shown++;
            LastShown = now;
            return true;
        }
    }
}
