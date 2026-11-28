using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using Universes.UniverseData.murder_mystery;
using Channel = FishNet.Transporting.Channel;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// The server side of the perk picker: a player steps a slot through their unlocked perks
    /// (<see cref="MM_PerkRequest"/>), the pick is saved, and they are told (<see cref="MM_PerksBroadcast"/>).
    /// A pick made during a round applies from the next one: <see cref="MM_ClassicRound"/> reads the perks when
    /// it deals the roles.
    /// </summary>
    public static class MM_PerkPicker {
        public static void OnPerkRequest(NetworkConnection conn, MM_PerkRequest request, Channel channel) {
            if (conn == null || !PlayerData.TryGetPlayerData(conn, out PlayerData player))
                return;
            if (request.slot is not (MM_PerkSlot.Murderer or MM_PerkSlot.Innocent))
                return;
            int step = request.step < 0 ? -1 : 1;
            bool saved = PlayerProgress.Modify<MM_SaveData>(player, save => save.StepPerk(request.slot, step));
            if (saved)
                Send(player, request.slot);
        }

        /// <summary>Tells <paramref name="player"/> their perks; <paramref name="changedSlot"/> when they just changed one.</summary>
        public static void Send(PlayerData player, MM_PerkSlot? changedSlot = null) {
            if (player == null || !player.Owner.IsActive || InstanceFinder.ServerManager == null)
                return;
            MM_SaveData save = MM_SaveData.Of(player);
            InstanceFinder.ServerManager.Broadcast(player.Owner, new MM_PerksBroadcast {
                murdererPerk = MM_Perks.Sanitize(save.murdererPerk, MM_PerkSlot.Murderer, save.roundsPlayed),
                innocentPerk = MM_Perks.Sanitize(save.innocentPerk, MM_PerkSlot.Innocent, save.roundsPlayed),
                roundsPlayed = save.roundsPlayed,
                changed = changedSlot.HasValue,
                changedSlot = changedSlot ?? MM_PerkSlot.Murderer,
            });
        }

        public static void SendAll() {
            foreach (PlayerData player in PlayerData.Players.Values)
                Send(player);
        }
    }
}
