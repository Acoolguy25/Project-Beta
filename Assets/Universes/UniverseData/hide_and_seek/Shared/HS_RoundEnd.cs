using System;
using System.Collections.Generic;
using RyanAssets.Retention;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>One player's part in a finished round, as the end of the round needs it.</summary>
    public readonly struct HS_RoundResult<TKey> {
        public readonly TKey Player;
        /// <summary>A hider nobody found.</summary>
        public readonly bool Survived;
        /// <summary>Survived, or a seeker whose side win counts (<see cref="HS_Rules.SeekerWinCounts"/>).</summary>
        public readonly bool Won;

        public HS_RoundResult(TKey player, bool survived, bool won) {
            Player = player;
            Survived = survived;
            Won = won;
        }
    }

    /// <summary>
    /// The decisions of a round's end and of its scoring moments, kept pure so they are tested: who is
    /// paid what at the end and in which order (the end points count towards the MVP), the summary card's
    /// lines, the last hider, the flashlight assist and who a shuffle pings.
    /// </summary>
    public static class HS_RoundEnd {
        /// <summary>The end-of-round points a player earns: Survived for a hider nobody found, Everyone found for a seeker whose win counts.</summary>
        public static int EndPoints(bool survived, bool won, HS_ScoreTuning scoring, out string reason) {
            if (survived) {
                reason = "Survived";
                return scoring.survivedPoints;
            }
            if (won) {
                reason = "Everyone found";
                return scoring.seekerWinPoints;
            }
            reason = null;
            return 0;
        }

        /// <summary>
        /// Pays every player's end points through <paramref name="score"/> (which adds them to
        /// <paramref name="ledger"/>), then names the MVP, so a hider whose Survived points put them on top
        /// is the MVP.
        /// </summary>
        public static bool Settle<TKey>(HS_RoundLedger<TKey> ledger, IReadOnlyList<HS_RoundResult<TKey>> results, HS_ScoreTuning scoring,
            Action<TKey, int, string> score, out TKey mvp, out int mvpPoints) {
            foreach (HS_RoundResult<TKey> result in results) {
                int points = EndPoints(result.Survived, result.Won, scoring, out string reason);
                if (points > 0) score(result.Player, points, reason);
            }
            return ledger.TryGetMvp(out mvp, out mvpPoints);
        }

        /// <summary>The summary card's title.</summary>
        public static string Title(bool survived, bool won, bool mvp) =>
            mvp ? "Round MVP!" : survived ? "You stayed hidden!" : won ? "Everyone found!" : "Round over";

        /// <summary>
        /// What the summary card adds at the end (points earned during the round are already on it): the
        /// round points against the record, the Round played XP, the result and the MVP pinned first (kit
        /// headlines), and the win streak.
        /// </summary>
        public static List<RewardLine> Lines(bool survived, bool won, bool mvp, int winStreak, int points, bool newBest, int bestPoints, HS_ScoreTuning scoring) {
            var lines = new List<RewardLine> {
                // A line that pays nothing: the round's points against the player's record, so the card shows the goal.
                RewardLine.Info("Round points", newBest ? $"{points} (new best!)" : $"{points} (best {bestPoints})"),
            };
            // Everyone who played gets something, so a seeker who found nobody does not end on "+0 XP".
            if (scoring.playedXp > 0) lines.Add(new RewardLine("Round played", scoring.playedXp));
            // The round result and the MVP are pinned first on the card, so the many small point lines of a long round fold before them.
            if (survived) lines.Add(RewardLine.Headline("Survived", scoring.Xp(scoring.survivedPoints), scoring.survivedGold));
            else if (won) lines.Add(RewardLine.Headline("Everyone found", scoring.Xp(scoring.seekerWinPoints)));
            if (mvp) lines.Add(RewardLine.Headline("Round MVP", scoring.mvpXp, scoring.mvpGold));
            if (won) {
                int streakXp = scoring.WinStreakXp(winStreak);
                if (streakXp > 0) lines.Add(new RewardLine("Win streak", streakXp).WithDetail($"{winStreak} wins in a row"));
            }
            return lines;
        }

        /// <summary>The last hider of a round that started with at least two is paid once, when the tag leaves one.</summary>
        public static bool LastHiderDue(bool alreadyPaid, int hidersAtStart, int hidersLeft) => !alreadyPaid && hidersAtStart >= 2 && hidersLeft == 1;

        /// <summary>
        /// A tag is a flashlight assist for the seeker whose beam dazzled the hider when that seeker is someone
        /// else, still here, and the tag came before the dazzle wore off plus <paramref name="grace"/> seconds.
        /// </summary>
        public static bool FlashAssist(bool dazzlerIsTagger, bool dazzlerPresent, float now, float dazzledUntil, float grace) =>
            dazzlerPresent && !dazzlerIsTagger && now <= dazzledUntil + grace;

        /// <summary>A hider is pinged at the end of a shuffle when they never left their room, are still hiding and are not blended.</summary>
        public static bool PingedAfterShuffle(bool moved, bool stillHiding, bool blended) => !moved && stillHiding && !blended;
    }
}
