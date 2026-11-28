using System;
using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
using RyanAssets.Server.ServerCore;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Universes.UniverseData.tycoon.Server {
    // Reasons to come back: the retention kit (levels and their unlocks, daily challenges, streak, the
    // rebirth summary card), the goal chain and the daily chest. Offline earnings live with the save
    // (TY_ServerRunner.Progress.cs) because they are paid from it.
    public sealed partial class TY_ServerRunner {
        [Header("Retention (levels, daily challenges, streak)")]
        [Tooltip("Level track and streak; starts from TY_Retention.DefaultTuning (slower than the kit's defaults).")]
        [SerializeField] RetentionTuning retentionTuning = TY_Retention.DefaultTuning();
        [Tooltip("Strength of the tycoon's level unlocks (Lucky Coins, Big Hopper, ...).")]
        [SerializeField] TY_UnlockTuning unlockTuning = new();
        [Tooltip("Minutes on a claimed plot before today's streak bonus pays (a tycoon has no match to finish).")]
        [SerializeField, Min(0f)] float streakMinutes = 3f;

        [Header("Offline earnings")]
        [SerializeField] TY_OfflineEarnings offline = new();

        [Header("Daily chest")]
        [Tooltip("What the chest pays on each day of the play streak (cash grows, perk tokens on set days).")]
        [SerializeField] TY_DailyChest chest = new();

        [Header("Goals")]
        [Tooltip("Most goals completed per player per second, so a burst of them each gets its moment.")]
        [SerializeField, Min(1)] int goalsPerTick = 1;

        RetentionService<TY_SaveData> retention;

        void ValidateRetentionTuning() {
            retentionTuning?.Validate();
            unlockTuning?.Validate();
            offline?.Validate();
            chest?.Validate();
            goldCoins?.Validate();
            rewards?.Validate();
        }

        // ---------------------------------------------------------------- level unlocks
        /// <summary>Whether the player owns a level unlock (checked at use time; false while their save loads).</summary>
        bool Has(NetworkConnection connection, string unlockId) =>
            connection != null && retention != null && retention.IsUnlocked(connection, unlockId);

        double LevelIncomeMultiplier(NetworkConnection connection) => Has(connection, TY_Retention.Legend) ? unlockTuning.legendIncome : 1d;

        /// <summary>The rush-hour multiplier for this player (1 outside a rush hour; Rush VIP adds to it).</summary>
        float RushMultiplierFor(NetworkConnection connection) {
            float now = Time.time;
            if (!rushHour.IsActive(now)) return 1f;
            return rushHour.Multiplier(now) + (Has(connection, TY_Retention.RushVip) ? unlockTuning.rushVipBonus : 0f);
        }

        long CapacityOf(Plot plot, Tycooner t) {
            long capacity = TY_Catalog.Capacity(plot.purchased, t.perks);
            return Has(plot.owner, TY_Retention.BigHopper) ? (long)(capacity * (double)unlockTuning.bigHopperCapacity) : capacity;
        }

        /// <summary>Seconds until the next gold coin on this player's plot.</summary>
        float GoldenWait(NetworkConnection owner) =>
            Random.Range(goldCoins.interval.x, goldCoins.interval.y) * (Has(owner, TY_Retention.CoinRain) ? unlockTuning.coinRainInterval : 1f);

        // ---------------------------------------------------------------- once a second
        /// <summary>Play time (streak, "minutes" challenges), challenge stats and goals for one player.</summary>
        void TickPlayer(NetworkConnection connection, Tycooner t, float dt) {
            if (t.unreportedEarnings > 0) {
                retention.Count(connection, TY_Retention.StatEarn, (int)Math.Min(int.MaxValue, t.unreportedEarnings));
                t.unreportedEarnings = 0;
            }
            if (t.plot >= 0) {
                t.plotSeconds += dt;
                int minutes = Mathf.FloorToInt(t.plotSeconds / 60f);
                if (minutes > t.minutesReported) {
                    retention.Count(connection, TY_Retention.StatMinutes, minutes - t.minutesReported);
                    // Once a minute: the kit sends a message per call, so play-time XP is not paid every tick.
                    retention.AddXp(connection, rewards.minuteXp * (minutes - t.minutesReported), "Time on your plot", announce: false);
                    t.minutesReported = minutes;
                }
                if (!t.streakClaimed && t.plotSeconds >= streakMinutes * 60f) {
                    ClaimStreak(connection, t);
                }
                double income = BaseIncomeOf(plots[t.plot].purchased, t, connection);
                retention.Reach(connection, TY_Retention.StatIncome, (int)Math.Min(int.MaxValue, income));
            }
            if (t.loaded) CompleteMetGoals(connection, t, goalsPerTick);
        }

        /// <summary>
        /// Pays today's streak bonus (once a session) and sends the kit's snapshot at once, so the HUD chip's
        /// streak count moves with the chest's "Day N" toast instead of at the next snapshot.
        /// </summary>
        void ClaimStreak(NetworkConnection connection, Tycooner t) {
            t.streakClaimed = true;
            retention.ClaimDailyStreak(connection);
            retention.SendSnapshot(connection);
        }

        /// <summary>
        /// Completes up to <paramref name="limit"/> goals in a row that the player has met. With
        /// <paramref name="cardLines"/> (a rebirth's summary card), each goal's XP is paid by its own pinned
        /// line on the card instead of joining the folded "Goals" line, so the goal the rebirth met
        /// ("Rebirth 2 times") is seen there.
        /// </summary>
        void CompleteMetGoals(NetworkConnection connection, Tycooner t, int limit, List<RewardLine> cardLines = null) {
            if (!t.loaded) return;
            for (int i = 0; i < limit && TY_Goals.IsMet(t.goalIndex, GoalStats(connection, t)); i++) CompleteGoal(connection, t, cardLines);
        }

        // ---------------------------------------------------------------- goals
        /// <summary>
        /// The player's goal stats, computed once per frame (a tick checks goals and then sends the wallet);
        /// anything that changes them (a payment, a purchase, a rebirth) marks them stale.
        /// </summary>
        TY_GoalStats GoalStats(NetworkConnection connection, Tycooner t) {
            if (t.statsFrame == Time.frameCount) return t.stats;
            bool hasPlot = OwnsPlot(connection, t);
            ulong purchased = hasPlot ? plots[t.plot].purchased : 0ul;
            int perkLevels = 0;
            foreach (TY_Perk perk in TY_Perks.All) perkLevels += t.perks.Level(perk.id);
            t.stats = new TY_GoalStats {
                purchased = purchased, collects = t.collects, coins = t.coins,
                lifetime = (long)Math.Min(long.MaxValue / 4d, t.lifetime),
                // The income the wallet shows (rush hours included), so the goal card and the wallet agree.
                income = hasPlot ? (long)Math.Min(long.MaxValue / 4d, IncomeOf(plots[t.plot], t)) : 0,
                rebirths = t.rebirths, perkLevels = perkLevels,
            };
            t.statsFrame = Time.frameCount;
            return t.stats;
        }

        /// <summary>The cash <paramref name="goal"/> pays this player now (income-based goals use the income without rush hours).</summary>
        long GoalCash(NetworkConnection connection, Tycooner t, in TY_Goal goal) {
            double income = t.plot >= 0 && t.plot < plots.Length ? BaseIncomeOf(plots[t.plot].purchased, t, connection) : 0d;
            return goal.CashReward(income);
        }

        void CompleteGoal(NetworkConnection connection, Tycooner t, List<RewardLine> cardLines = null) {
            TY_Goal goal = TY_Goals.Get(t.goalIndex);
            long cash = GoalCash(connection, t, goal);
            int index = t.goalIndex++;
            t.statsFrame = -1;
            if (cash > 0) Pay(connection, t, cash);
            t.tokens += goal.tokens;
            if (cardLines != null) cardLines.Add(RewardLine.Headline(goal.Describe(), goal.xp, 0, $"Goal {index + 1}"));
            else retention.AddXp(connection, goal.xp, "Goals");
            retention.Count(connection, TY_Retention.StatGoals);
            string reward = cash > 0 ? $"+${TY_Catalog.Money(cash)}" : "";
            if (goal.tokens > 0) reward += (reward.Length > 0 ? ", " : "+") + $"{goal.tokens} perk token{(goal.tokens == 1 ? "" : "s")}";
            Event(connection, TY_EventKind.Goal, t.plot, index, cash, $"Goal complete: {goal.Describe()}! {reward}", goal.tokens);
            plotsDirty = true;
            SendWallet(connection);
        }

        // ---------------------------------------------------------------- daily chest
        /// <summary>Today's chest is waiting (only once the save is applied, so it never pays twice in a day).</summary>
        bool ChestReady(Tycooner t) => TY_DailyChest.Ready(t.loaded, t.chestDay, RetentionDay.Of(DateTime.UtcNow));

        /// <summary>The player's streak day today, from the kit's streak in the save (1 while it is not known).</summary>
        int StreakDayToday(NetworkConnection connection, DateTime now) {
            if (!retention.IsReady(connection) || !PlayerProgress.TryGet(connection, out TY_SaveData save) || save.retention == null) return 1;
            return TY_DailyChest.StreakDayToday(save.retention.streakDays, save.retention.streakDay, RetentionDay.Of(now));
        }

        /// <summary>Streak day of the chest the sign names: today's while it waits, else tomorrow's (if the streak goes on).</summary>
        int ChestStreakDay(NetworkConnection connection, Tycooner t, DateTime now) {
            int today = StreakDayToday(connection, now);
            return ChestReady(t) ? today : today + 1;
        }

        void OpenChest(NetworkConnection connection, Tycooner t, Plot plot) {
            DateTime now = DateTime.UtcNow;
            int day = StreakDayToday(connection, now);
            t.chestDay = RetentionDay.Of(now);
            // The chest is the daily check-in: it also claims today's streak (if a few minutes on the plot have not already).
            if (!t.streakClaimed) ClaimStreak(connection, t);
            // From the best income reached, not the build standing now: opening it right after a rebirth
            // pays as much as opening it with the factory complete.
            long cash = chest.Cash(NotePeakIncome(connection, t, plot), day);
            int tokens = chest.Tokens(day);
            Pay(connection, t, cash);
            t.tokens += tokens;
            retention.AddXp(connection, chest.Xp(day), "Daily chest");
            string prize = $"+${TY_Catalog.Money(cash)}" + (tokens > 0 ? $" and +{tokens} perk token{(tokens == 1 ? "" : "s")}" : "");
            Event(connection, TY_EventKind.DailyChest, t.plot, day, cash, $"Day {day} chest! {prize}. Tomorrow: {chest.Prize(day + 1)}.",
                RetentionDay.SecondsUntilReset(now));
            plotsDirty = true;
            SendWallet(connection);
        }
    }
}
