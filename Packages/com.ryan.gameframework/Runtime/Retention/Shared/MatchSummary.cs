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

        public RewardLine(string label, int xp, int gold = 0, int count = 1) : this(label, xp, gold, count, null) { }

        public RewardLine(string label, int xp, int gold, int count, string detail) {
            Label = label ?? string.Empty;
            Xp = Math.Max(0, xp);
            Gold = Math.Max(0, gold);
            Count = Math.Max(1, count);
            Detail = string.IsNullOrEmpty(detail) ? null : detail;
        }

        /// <summary>A line that pays nothing and shows <paramref name="detail"/> on the card ("Stars", "3 / 3").</summary>
        public static RewardLine Info(string label, string detail) => new(label, 0, 0, 1, detail);

        /// <summary>This line with <paramref name="detail"/> shown next to it.</summary>
        public RewardLine WithDetail(string detail) => new(Label, Xp, Gold, Count, detail);

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
        /// <summary>More lines than this are folded into the last one, so the card always fits.</summary>
        public const int MaxLines = 8;
        public const string OtherLabel = "Other rewards";

        readonly List<RewardLine> lines = new();

        public MatchSummary(LevelProgress before) {
            Before = before;
            After = before;
        }

        /// <summary>Headline of the card ("Victory!", "Round over").</summary>
        public string Title { get; set; } = string.Empty;
        public LevelProgress Before { get; }
        public LevelProgress After { get; set; }
        public IReadOnlyList<RewardLine> Lines => lines;
        /// <summary>Unlocks earned between <see cref="Before"/> and <see cref="After"/>.</summary>
        public List<UnlockDefinition> Unlocks { get; } = new();
        /// <summary>The streak day paid by this match (0 = no streak bonus this match).</summary>
        public int StreakDays { get; set; }

        public int TotalXp { get; private set; }
        public int TotalGold { get; private set; }

        /// <summary>
        /// Adds a line, merging it into an earlier line with the same label (rewards add up, the newer
        /// detail wins). Past <see cref="MaxLines"/> lines, rewards fold into the last line and details
        /// are dropped, so keep a card's lines few. Empty lines are ignored.
        /// </summary>
        public void Add(RewardLine line) {
            if (line.IsEmpty) return;
            TotalXp = SaturatingAdd(TotalXp, line.Xp);
            TotalGold = SaturatingAdd(TotalGold, line.Gold);
            for (int i = 0; i < lines.Count; i++) {
                if (lines[i].Label != line.Label) continue;
                lines[i] = Merge(lines[i], line, lines[i].Label);
                return;
            }
            if (lines.Count < MaxLines) {
                lines.Add(line);
                return;
            }
            RewardLine last = lines[MaxLines - 1];
            lines[MaxLines - 1] = Merge(last, line, OtherLabel);
        }

        static RewardLine Merge(RewardLine a, RewardLine b, string label) {
            bool same = label == a.Label;
            // Info lines repeat a value, they do not stack: "x2" is only for rewards.
            int count = same && a.HasReward && b.HasReward ? a.Count + b.Count : a.Count;
            string detail = same ? b.Detail ?? a.Detail : null;
            return new RewardLine(label, SaturatingAdd(a.Xp, b.Xp), SaturatingAdd(a.Gold, b.Gold), same ? count : 1, detail);
        }

        static int SaturatingAdd(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);
    }
}
