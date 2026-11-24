using System;
using System.Collections.Generic;
using RyanAssets.Retention;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>How a match ended for one player.</summary>
    public enum CTF_Outcome : byte { Won, LateWin, Draw, Lost }

    /// <summary>What one player's summary card is built from (the server fills it in).</summary>
    public struct CTF_SummaryInput {
        public CTF_Outcome outcome;
        public int redScore, blueScore;
        public bool mvp;
        public int captures, kills, deaths;
        /// <summary>Share of the match gone when the player joined (for the late-join line).</summary>
        public float joinedAtShare;
        /// <summary>Today's twist bonus on the result (Tough Bots), in XP; 0 for none.</summary>
        public int twistBonusXp;
        public string twistTitle;
        /// <summary>At the level cap: the standing goal nearest done ("Wins 34/50"); empty below the cap.</summary>
        public string nextGoal;
    }

    /// <summary>The XP and gold the result lines pay (the server's tuning).</summary>
    public struct CTF_SummaryPay {
        public int winXp, winGold, lossXp, lossGold, drawXp, drawGold, captureGold;
        public string mvpTitle;

        public static CTF_SummaryPay Defaults => new() {
            winXp = CTF_RewardDefaults.WinXp, winGold = CTF_RewardDefaults.WinGold, lossXp = CTF_RewardDefaults.LossXp,
            lossGold = CTF_RewardDefaults.LossGold, drawXp = CTF_RewardDefaults.DrawXp, drawGold = CTF_RewardDefaults.DrawGold,
            captureGold = CTF_RewardDefaults.CaptureGold, mvpTitle = "MVP",
        };
    }

    /// <summary>
    /// The match summary card's title and lines (the retention kit draws them): the result as the
    /// pinned headline, the MVP beside it, the twist's bonus, the flag bonus, one info line, and at the
    /// cap the next standing goal. Pure, so the tests read exactly what a player sees.
    /// </summary>
    public static class CTF_Summary {
        public static CTF_Outcome Outcome(bool teamWon, bool winCounts, bool draw) =>
            teamWon ? (winCounts ? CTF_Outcome.Won : CTF_Outcome.LateWin) : draw ? CTF_Outcome.Draw : CTF_Outcome.Lost;

        public static string Title(CTF_Outcome outcome) => outcome switch {
            CTF_Outcome.Won => "Victory!",
            CTF_Outcome.LateWin => "Joined late",
            CTF_Outcome.Draw => "Draw",
            _ => "Defeat",
        };

        /// <summary>The XP the result itself pays (what the twist's bonus is a share of).</summary>
        public static int ResultXp(CTF_Outcome outcome, in CTF_SummaryPay pay) => outcome switch {
            CTF_Outcome.Won => pay.winXp,
            CTF_Outcome.Draw => pay.drawXp,
            _ => pay.lossXp,
        };

        public static void Lines(in CTF_SummaryInput input, in CTF_SummaryPay pay, List<RewardLine> into) {
            into.Clear();
            string score = $"{input.redScore}-{input.blueScore}";
            // The result is the headline: it shows first and never folds.
            switch (input.outcome) {
                case CTF_Outcome.Won:
                    into.Add(RewardLine.Headline("Victory", pay.winXp, pay.winGold, score));
                    break;
                case CTF_Outcome.LateWin:
                    // Joined after the cut-off and made no flag play: paid as taking part, not as a win.
                    into.Add(RewardLine.Headline("Joined late", pay.lossXp, pay.lossGold, $"{input.joinedAtShare:P0} gone"));
                    into.Add(RewardLine.Info("Win not counted", "grab a flag next time"));
                    break;
                case CTF_Outcome.Draw:
                    into.Add(RewardLine.Headline("Draw", pay.drawXp, pay.drawGold, score));
                    break;
                default:
                    into.Add(RewardLine.Headline("Defeat", pay.lossXp, pay.lossGold, score));
                    break;
            }
            // The MVP medal's line (paid when it was given) is pinned beside the result.
            if (input.mvp) into.Add(RewardLine.Headline(string.IsNullOrEmpty(pay.mvpTitle) ? "MVP" : pay.mvpTitle, 0, 0, "best on the field"));
            if (input.twistBonusXp > 0) into.Add(new RewardLine($"{input.twistTitle} bonus", input.twistBonusXp));
            if (input.captures > 0) into.Add(new RewardLine("Flag bonus", 0, input.captures * Math.Max(0, pay.captureGold), input.captures));
            // One info line: every medal and flag play already has its own XP line, and the twist has its chip.
            into.Add(RewardLine.Info("Eliminations / deaths", $"{input.kills} / {input.deaths}"));
            if (!string.IsNullOrEmpty(input.nextGoal)) into.Add(RewardLine.Info("Next goal", input.nextGoal));
        }
    }
}
