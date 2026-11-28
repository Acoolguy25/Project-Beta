using System;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;

namespace Universes.UniverseData.tycoon.Server {
    // Saved progress: the wallet, rebirths, perks, the plot's build, the goal, counters and the daily chest
    // survive between sessions, and the time away pays offline earnings. The live state stays in
    // Tycooner/Plot; it is copied into TY_SaveData only when a save is written. The retention kit keeps
    // its own block of the same document (TY_SaveData.retention) and is never touched here.
    public sealed partial class TY_ServerRunner {
        /// <summary>Loads the player's save and applies it when it arrives (normally before they reach a plot).</summary>
        async UniTaskVoid LoadProgressAsync(PlayerData player) {
            TY_SaveData save = await PlayerProgress.Load<TY_SaveData>(player, TY_SaveData.Universe);
            // The player may have left while the save loaded.
            if (player == null || !players.TryGetValue(player.Owner, out Tycooner t)) return;
            NetworkConnection connection = player.Owner;
            // Read before the first capture overwrites it with "now".
            long lastSeen = save.lastSeenUnix;
            PlayerProgress.CaptureBeforeSave<TY_SaveData>(player, s => CaptureProgress(connection, t, s));
            t.loaded = true;
            if (save.IsEmpty) {
                SendWallet(connection);
                return;
            }

            t.cash = save.cash;
            t.lifetime = save.lifetime;
            t.rebirths = save.rebirths;
            t.tokens = save.tokens;
            t.perks = save.perks;
            t.coins = save.coins;
            t.collects = save.collects;
            t.chestDay = save.chestDay;
            t.invested = save.invested;
            t.peakIncome = save.peakIncome;
            t.pets = save.PetLevels();
            t.hatches = save.hatches;
            t.sinceEpic = save.sinceEpic;
            t.statsFrame = -1;
            if (t.plot >= 0) RestoreBuild(t.plot, save);
            t.goalIndex = save.goalIndex >= 0 ? save.goalIndex : FirstGoalFor(connection, t, save.built);
            ApplySpeed(player, t);
            WriteLeaderboard(player);
            plotsDirty = true;
            // Offline pay depends on level unlocks (Night Shift, Overtime, Tycoon Legend), which are only
            // known once the kit is ready: pay now if it is, otherwise from OnRetentionReady.
            t.offlineSince = lastSeen;
            t.offlineBuilt = save.built;
            TryPayOffline(connection, t);
            SendWallet(connection);
        }

        void OnRetentionReady(NetworkConnection connection, int level) {
            if (connection != null && players.TryGetValue(connection, out Tycooner t)) TryPayOffline(connection, t);
        }

        /// <summary>Pays (or welcomes back) once both the tycoon save is applied and the player's level is known.</summary>
        void TryPayOffline(NetworkConnection connection, Tycooner t) {
            if (!t.loaded || t.offlineSince < 0 || !retention.IsReady(connection)) return;
            long since = t.offlineSince;
            t.offlineSince = -1;
            if (!PayOfflineEarnings(connection, t, t.offlineBuilt, since))
                t.welcome.Hold(new TY_EventBroadcast { kind = TY_EventKind.Info, plot = t.plot, item = -1, text = "Welcome back! Your tycoon progress was restored." });
            // The welcome first: the client holds the card's cash back from the wallet that follows.
            SendWelcome(connection, t);
            SendWallet(connection);
        }

        /// <summary>
        /// Sends the held welcome-back event once the client listens. A join's first events can arrive
        /// before the client has registered its handlers (they would be dropped), so the save may load
        /// first and the card still shows.
        /// </summary>
        void SendWelcome(NetworkConnection connection, Tycooner t) {
            if (connection == null || !connection.IsActive || !t.welcome.TryTake(out TY_EventBroadcast welcome)) return;
            welcome.plot = t.plot;
            InstanceFinder.ServerManager.Broadcast(connection, welcome);
        }

        /// <summary>
        /// A save from before goals existed starts at the first goal it has not met, so a veteran is not
        /// paid for a burst of beginner goals (their build counts are taken from the saved build).
        /// </summary>
        int FirstGoalFor(NetworkConnection connection, Tycooner t, ulong savedBuild) {
            TY_GoalStats stats = GoalStats(connection, t);
            if (stats.purchased == 0) stats.purchased = savedBuild;
            return TY_Goals.FirstUnmet(stats);
        }

        /// <summary>
        /// "While you were away": pays a share of the saved build's income for the time since the last
        /// save (capped; Night Shift and Overtime raise the cap and share). Returns whether it paid.
        /// </summary>
        bool PayOfflineEarnings(NetworkConnection connection, Tycooner t, ulong savedBuild, long lastSeenUnix) {
            double away = TY_OfflineEarnings.AwaySeconds(lastSeenUnix, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            bool nightShift = Has(connection, TY_Retention.NightShift), overtime = Has(connection, TY_Retention.Overtime);
            double income = BaseIncomeOf(savedBuild, t, connection);
            long cash = offline.Payout(income, away, nightShift, overtime);
            if (cash <= 0) return false;
            Pay(connection, t, cash);
            t.welcome.Hold(new TY_EventBroadcast {
                kind = TY_EventKind.OfflineEarnings, plot = t.plot, item = -1, amount = cash, extra = (long)away,
                text = offline.Describe(away, nightShift, overtime),
            });
            return true;
        }

        /// <summary>Rebuilds a freshly claimed plot from the claimer's save.</summary>
        void RestoreBuild(NetworkConnection connection, int index) {
            if (PlayerProgress.TryGet(connection, out TY_SaveData save)) RestoreBuild(index, save);
        }

        void RestoreBuild(int index, TY_SaveData save) {
            if (save.built == 0) return;
            plots[index].purchased |= save.built;
            plotsDirty = true;
        }

        /// <summary>Copies the live state into the save right before it is written.</summary>
        void CaptureProgress(NetworkConnection connection, Tycooner t, TY_SaveData save) {
            // A player who left while their save loaded never had it applied: keep the save as it was.
            if (!t.loaded) return;
            save.cash = t.cash;
            save.lifetime = t.lifetime;
            save.rebirths = t.rebirths;
            save.tokens = t.tokens;
            save.perks = t.perks;
            save.goalIndex = t.goalIndex;
            save.coins = t.coins;
            save.collects = t.collects;
            save.chestDay = t.chestDay;
            save.invested = t.invested;
            save.peakIncome = t.peakIncome;
            save.SetPetLevels(t.pets);
            save.hatches = t.hatches;
            save.sinceEpic = t.sinceEpic;
            save.lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Without a plot (not claimed yet, or already released on leave) the saved build is kept.
            if (t.plot >= 0 && t.plot < plots.Length && plots[t.plot].owner == connection) save.built = plots[t.plot].purchased;
        }

        /// <summary>Captures the plot's build before it is released, whichever leave handler runs first.</summary>
        void CaptureProgressOnLeave(PlayerData player, Tycooner t) {
            NetworkConnection connection = player.Owner;
            PlayerProgress.Modify<TY_SaveData>(player, s => CaptureProgress(connection, t, s));
        }
    }
}
