using System;
using System.Collections.Generic;

namespace RyanAssets.Retention {
    /// <summary>
    /// One line of a match summary: "Victory +100 XP", "Kills x5 +50 XP", or an info line with no
    /// reward ("Stars  3 / 3", "Rating  +12") made with <see cref="Info"/>.
    /// </summary>
    public readonly struct RewardLine {
        public readonly string Label;
        /// <summary>Game XP (account XP follows <see cref="RetentionTuning.accountXpScale"/>).</summary>
        public readonly int Xp;
        /// <summary>Account gold.</summary>
        public readonly int Gold;
        /// <summary>How many rewards were merged into this line (shown as "x5" when above 1).</summary>
        public readonly int Count;
        /// <summary>Optional short text shown with the line ("3 stars", "Rating +12"); null when none.</summary>
        public readonly string Detail;
        /// <summary>
        /// A headline line (made with <see cref="Headline"/> or <see cref="AsHeadline"/>): it shows first
        /// on the card and is never folded into "+N more rewards".
        /// </summary>
        public readonly bool Pinned;

        public RewardLine(string label, int xp, int gold = 0, int count = 1) : this(label, xp, gold, count, null) { }

        public RewardLine(string label, int xp, int gold, int count, string detail) : this(label, xp, gold, count, detail, false) { }

        RewardLine(string label, int xp, int gold, int count, string detail, bool pinned) {
            Label = label ?? string.Empty;
            Xp = Math.Max(0, xp);
            Gold = Math.Max(0, gold);
            Count = Math.Max(1, count);
            Detail = string.IsNullOrEmpty(detail) ? null : detail;
            Pinned = pinned;
        }

        /// <summary>A line that pays nothing and shows <paramref name="detail"/> on the card ("Stars", "3 / 3").</summary>
        public static RewardLine Info(string label, string detail) => new(label, 0, 0, 1, detail);

        /// <summary>
        /// The match's headline ("3-star defence", "Rebirth 4"): shown first on the card and never folded.
        /// Keep it to one or two per match.
        /// </summary>
        public static RewardLine Headline(string label, int xp, int gold = 0, string detail = null) => new(label, xp, gold, 1, detail, true);

        /// <summary>This line with <paramref name="detail"/> shown next to it.</summary>
        public RewardLine WithDetail(string detail) => new(Label, Xp, Gold, Count, detail, Pinned);

        /// <summary>This line pinned as a headline (see <see cref="Pinned"/>).</summary>
        public RewardLine AsHeadline() => new(Label, Xp, Gold, Count, Detail, true);

        /// <summary>How much the line pays, to decide which lines fold first (XP plus gold).</summary>
        internal long Value => (long)Xp + Gold;

        internal static RewardLine Make(string label, int xp, int gold, int count, string detail, bool pinned) => new(label, xp, gold, count, detail, pinned);

        /// <summary>True when the line pays XP or gold.</summary>
        public bool HasReward => Xp > 0 || Gold > 0;

        /// <summary>Nothing to pay and nothing to show; such lines are skipped.</summary>
        public bool IsEmpty => !HasReward && Detail == null;

        public override string ToString() {
            string text = Count > 1 ? $"{Label} x{Count}" : Label;
            if (Detail != null) text += $" ({Detail})";
            return HasReward ? $"{text} +{Xp} XP" : text;
        }
    }

    /// <summary>
    /// What a player earned in one match: reward lines (same labels merge), totals, the level before
    /// and after, and the unlocks reached. Built by the server service from <c>BeginMatch</c> to
    /// <c>FinishMatch</c> and sent as a <see cref="RetentionSummaryBroadcast"/>.
    /// </summary>
    public sealed class MatchSummary {
        /// <summary>
        /// Most lines the card shows. Past it, the least valuable lines fold into one
        /// "+N more rewards" line (headlines never fold), so the card always fits.
        /// </summary>
        public const int MaxLines = 8;
        /// <summary>Kept for compatibility; the folded line now reads <see cref="FoldedFormat"/>.</summary>
        public const string OtherLabel = "Other rewards";
        /// <summary>Label of the folded line: {0} = how many lines it holds.</summary>
        public const string FoldedFormat = "+{0} more rewards";
        /// <summary>Distinct lines kept per match; later new labels go straight to the folded line.</summary>
        const int MaxKept = 64;

        readonly List<RewardLine> lines = new();
        readonly List<RewardLine> shown = new();
        readonly List<int> order = new();
        bool dirty;
        int overflowCount, overflowXp, overflowGold;

        public MatchSummary(LevelProgress before) {
            Before = before;
            After = before;
        }

        /// <summary>Headline of the card ("Victory!", "Round over").</summary>
        public string Title { get; set; } = string.Empty;
        public LevelProgress Before { get; }
        public LevelProgress After { get; set; }

        /// <summary>
        /// The card's lines: headlines first, then the other lines in the order they were added. Past
        /// <see cref="MaxLines"/>, the least valuable lines (info lines first) fold into a last
        /// "+N more rewards" line that pays their total.
        /// </summary>
        public IReadOnlyList<RewardLine> Lines {
            get {
                if (dirty) Layout();
                return shown;
            }
        }

        /// <summary>Unlocks earned between <see cref="Before"/> and <see cref="After"/>.</summary>
        public List<UnlockDefinition> Unlocks { get; } = new();
        /// <summary>The streak day paid by this match (0 = no streak bonus this match).</summary>
        public int StreakDays { get; set; }

        public int TotalXp { get; private set; }
        public int TotalGold { get; private set; }

        /// <summary>
        /// Adds a line, merging it into an earlier line with the same label (rewards add up, the newer
        /// detail wins, a headline stays a headline). Empty lines are ignored.
        /// </summary>
        public void Add(RewardLine line) {
            if (line.IsEmpty) return;
            TotalXp = SaturatingAdd(TotalXp, line.Xp);
            TotalGold = SaturatingAdd(TotalGold, line.Gold);
            dirty = true;
            for (int i = 0; i < lines.Count; i++) {
                if (lines[i].Label != line.Label) continue;
                lines[i] = Merge(lines[i], line);
                return;
            }
            if (lines.Count < MaxKept || line.Pinned) {
                lines.Add(line);
                return;
            }
            overflowCount++;
            overflowXp = SaturatingAdd(overflowXp, line.Xp);
            overflowGold = SaturatingAdd(overflowGold, line.Gold);
        }

        void Layout() {
            dirty = false;
            shown.Clear();
            int pinned = 0;
            foreach (RewardLine line in lines)
                if (line.Pinned) pinned++;
            int others = lines.Count - pinned;
            bool fold = overflowCount > 0 || pinned + others > MaxLines;
            // Room for the other lines: what the headlines leave, less one slot for the folded line.
            int room = fold ? Math.Max(0, MaxLines - pinned - 1) : others;

            // Keep the most valuable other lines (earlier first on a tie), shown in the order added.
            order.Clear();
            for (int i = 0; i < lines.Count; i++)
                if (!lines[i].Pinned) order.Add(i);
            order.Sort((a, b) => {
                int byValue = lines[b].Value.CompareTo(lines[a].Value);
                return byValue != 0 ? byValue : a.CompareTo(b);
            });
            int foldedCount = overflowCount, foldedXp = overflowXp, foldedGold = overflowGold;
            for (int k = room; k < order.Count; k++) {
                RewardLine line = lines[order[k]];
                foldedCount++;
                foldedXp = SaturatingAdd(foldedXp, line.Xp);
                foldedGold = SaturatingAdd(foldedGold, line.Gold);
            }
            if (order.Count > room) order.RemoveRange(room, order.Count - room);
            order.Sort();

            foreach (RewardLine line in lines)
                if (line.Pinned) shown.Add(line);
            foreach (int index in order) shown.Add(lines[index]);
            // One plain line (count 1, no detail): it pays the folded total, it never shows a summed "xN".
            if (foldedCount > 0) shown.Add(new RewardLine(string.Format(FoldedFormat, foldedCount), foldedXp, foldedGold));
        }

        static RewardLine Merge(RewardLine a, RewardLine b) {
            // Info lines repeat a value, they do not stack: "x2" is only for rewards.
            int count = a.HasReward && b.HasReward ? a.Count + b.Count : a.Count;
            return RewardLine.Make(a.Label, SaturatingAdd(a.Xp, b.Xp), SaturatingAdd(a.Gold, b.Gold), count, b.Detail ?? a.Detail, a.Pinned || b.Pinned);
        }

        static int SaturatingAdd(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);
    }
}
