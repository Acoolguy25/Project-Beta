using Cysharp.Threading.Tasks;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;

namespace Universes.UniverseData.tycoon.Server {
    // Saved progress: the wallet, rebirths, perks and the plot's build survive between sessions.
    // The live state stays in Tycooner/Plot; it is copied into TY_SaveData only when a save is written.
    public sealed partial class TY_ServerRunner {
        /// <summary>Loads the player's save and applies it when it arrives (normally before they reach a plot).</summary>
        async UniTaskVoid LoadProgressAsync(PlayerData player) {
            TY_SaveData save = await PlayerProgress.Load<TY_SaveData>(player, TY_SaveData.Universe);
            // The player may have left while the save loaded.
            if (player == null || !players.TryGetValue(player.Owner, out Tycooner t)) return;
            NetworkConnection connection = player.Owner;
            PlayerProgress.CaptureBeforeSave<TY_SaveData>(player, s => CaptureProgress(connection, t, s));
            if (save.IsEmpty) return;

            t.cash = save.cash;
            t.lifetime = save.lifetime;
            t.rebirths = save.rebirths;
            t.tokens = save.tokens;
            t.perks = save.perks;
            if (t.plot >= 0) RestoreBuild(t.plot, save);
            ApplySpeed(player, t);
            WriteLeaderboard(player);
            plotsDirty = true;
            SendWallet(connection);
            Event(connection, TY_EventKind.Info, t.plot, -1, 0, "Welcome back! Your tycoon progress was restored.");
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
            save.cash = t.cash;
            save.lifetime = t.lifetime;
            save.rebirths = t.rebirths;
            save.tokens = t.tokens;
            save.perks = t.perks;
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
