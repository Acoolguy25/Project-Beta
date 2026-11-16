namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Story lines are written "SPEAKER / words" ("DISPATCH / ...", "Keeper's instructions / ...").
    /// The radio shows the speaker on its caption and the journal uses it as the entry heading.
    /// </summary>
    public static class CH_StoryText {
        public const string Separator = " / ";

        /// <summary>Splits a line into its speaker or heading and its body. Lines without one return an empty speaker.</summary>
        public static void Split(string line, out string speaker, out string body) {
            speaker = string.Empty;
            body = line ?? string.Empty;
            int split = body.IndexOf(Separator, System.StringComparison.Ordinal);
            // A separator near the end is part of the sentence, not a heading.
            if (split <= 0 || split > 60) return;
            speaker = body.Substring(0, split).Trim();
            body = body.Substring(split + Separator.Length).Trim();
        }

        public static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
