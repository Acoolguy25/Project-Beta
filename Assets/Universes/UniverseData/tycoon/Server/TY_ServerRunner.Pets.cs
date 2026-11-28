using FishNet.Connection;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.DataService;
using RyanAssets.Shared.Global;
using Random = UnityEngine.Random;

namespace Universes.UniverseData.tycoon.Server {
    // Brick Buddies: the pet crate on the owner's plot. Each step onto its pad buys one crate (priced in
    // seconds of the owner's income), rolls a pet by rarity (with a pity guarantee) and adds it to the
    // saved collection, which raises income for good (see TY_Pets). Epic and Legendary pets are announced
    // to the whole server.
    public sealed partial class TY_ServerRunner {
        /// <summary>The crate's price for this player right now (from their plot's income, rush hours left out).</summary>
        long CratePrice(NetworkConnection connection, Tycooner t) =>
            petCrate.Price(OwnsPlot(connection, t) ? BaseIncomeOf(plots[t.plot].purchased, t, connection) : 0d);

        void TryHatch(NetworkConnection connection, Tycooner t, Plot plot) {
            // The collection is part of the save: wait for it, so a hatch is never overwritten by the load.
            if (!t.loaded) return;
            long price = CratePrice(connection, t);
            if (t.cash < price) {
                if (!Refuse(connection, t, RefuseCrate)) return;
                long missing = (long)System.Math.Ceiling(price - t.cash);
                Event(connection, TY_EventKind.TooPoor, t.plot, -1, missing, $"You need ${TY_Catalog.Money(missing)} more for a pet crate.");
                return;
            }
            t.cash -= price;
            t.statsFrame = -1;
            TY_Rarity rarity = petCrate.RollRarity(Random.value, t.sinceEpic);
            int pet = TY_PetCrate.PickPet(rarity, Random.value);
            t.pets = TY_Pets.Repair(t.pets);
            TY_Hatch hatch = TY_PetCrate.Apply(t.pets, pet);
            t.hatches++;
            t.sinceEpic = TY_PetCrate.NextSinceEpic(t.sinceEpic, rarity);
            long refund = hatch.maxed ? (long)(price * petCrate.maxedRefund) : 0;
            if (refund > 0) Pay(connection, t, refund);

            TY_Pet data = TY_Pets.All[pet];
            string rarityName = TY_Pets.RarityName(data.rarity).ToUpperInvariant();
            string text = hatch.maxed ? $"{data.name} is already max level! +${TY_Catalog.Money(refund)} back."
                : hatch.isNew ? $"NEW PET! {rarityName} {data.name}: +{TY_Pets.Bonus(pet, 1) * 100d:0}% income."
                : $"{data.name} levelled up to Lv {hatch.level}: +{TY_Pets.Bonus(pet, hatch.level) * 100d:0}% income.";
            Event(connection, TY_EventKind.PetHatched, t.plot, pet, hatch.level, text, hatch.Flags);
            if (data.rarity >= TY_Rarity.Epic)
                ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} hatched {(data.rarity == TY_Rarity.Epic ? "an" : "a")} {rarityName} {data.name}!", SystemMessageSource.CustomMessage));
            retention.Count(connection, TY_Retention.StatHatch);
            retention.AddXp(connection, petCrate.xp, "Pet crates");
            NotePeakIncome(connection, t, plot);
            plotsDirty = true;
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendWallet(connection);
        }

        /// <summary>Refusal key of the crate (items use their catalog index).</summary>
        const int RefuseCrate = -100;

        /// <summary>Gives a player a pet (test probes only, like <see cref="GrantCash"/>).</summary>
        public void GrantPet(NetworkConnection connection, int pet, int level) {
            if (!TY_Pets.IsValid(pet)) return;
            Tycooner t = For(connection);
            t.pets = TY_Pets.Repair(t.pets);
            t.pets[pet] = (byte)System.Math.Clamp(level, 0, TY_Pets.MaxLevel);
            t.statsFrame = -1;
            plotsDirty = true;
            SendWallet(connection);
        }
    }
}
