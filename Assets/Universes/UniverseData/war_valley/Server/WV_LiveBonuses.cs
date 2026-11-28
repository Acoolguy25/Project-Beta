using System.Collections.Generic;
using FishNet;
using RyanAssets.Characters.Shared;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Makes a doctrine picked mid-round act on what the commander already fields, not only on what
    /// they build next: troops, vehicles and standing structures take the new health (the added share
    /// healed, as the flag's Bastion does) and troops the new damage. Then every client is told what
    /// the pick touched (<see cref="WV_DoctrineEffect"/>) so it can be seen landing on the army.
    /// </summary>
    public static class WV_LiveBonuses {
        /// <summary>The most things one pick's effect names, so a big base does not flood the wire or the screen.</summary>
        public const int MaxEffectTargets = 40;

        static readonly List<GameCharacter> troops = new();
        static readonly List<int> ids = new();

        /// <summary>
        /// Applies the step from <paramref name="before"/> to <paramref name="after"/> (a commander's
        /// bonuses either side of a pick of <paramref name="doctrine"/>) to their troops, vehicles and
        /// structures, and broadcasts the effect. Returns how many things it touched.
        /// </summary>
        public static int Apply(int clientId, WV_DoctrineDefinition doctrine, in WV_CommanderModifiers before,
            in WV_CommanderModifiers after, WV_ServerTroops squad) {
            if (doctrine == null || clientId == WV_Owned.NoOwner)
                return 0;
            WV_DoctrineTargets targets = WV_DoctrineCatalog.Targets(doctrine);
            ids.Clear();

            if ((targets & WV_DoctrineTargets.Troops) != 0 && squad != null) {
                troops.Clear();
                squad.Reinforce(clientId, Ratio(after.TroopHealthMultiplier, before.TroopHealthMultiplier),
                    Ratio(after.TroopDamageMultiplier, before.TroopDamageMultiplier), troops);
                foreach (GameCharacter troop in troops)
                    Add(troop != null ? troop.NetworkObject : null);
            }

            float vehicleRatio = Ratio(after.VehicleHealthMultiplier, before.VehicleHealthMultiplier);
            float structureRatio = Ratio(after.StructureHealthMultiplier, before.StructureHealthMultiplier);
            foreach (WV_Owned owned in WV_Owned.All) {
                if (owned == null || !owned.IsOwnedBy(clientId))
                    continue;
                if (owned.TryGetComponent(out WV_Unit unit)) {
                    if (unit.IsDead || (targets & WV_DoctrineTargets.Vehicles) == 0)
                        continue;
                    if (vehicleRatio > 1.0001f) {
                        long max = unit.MaxHealth.Value;
                        long added = WV_CommanderModifiers.Scale(max, vehicleRatio) - max;
                        if (added > 0)
                            unit.HealMaxHealth(added);
                    }
                    Add(unit.NetworkObject);
                    continue;
                }
                if (!owned.TryGetComponent(out WV_Constructable site))
                    continue;
                bool named = (targets & WV_DoctrineTargets.Structures) != 0
                             || ((targets & WV_DoctrineTargets.Turrets) != 0 && owned.TryGetComponent(out WV_DefenseTurret _))
                             || ((targets & WV_DoctrineTargets.Mines) != 0 && owned.TryGetComponent(out WV_IncomeBuilding _))
                             || ((targets & WV_DoctrineTargets.Production) != 0 && owned.TryGetComponent(out WV_ProductionBuilding _));
                if (!named)
                    continue;
                if ((targets & WV_DoctrineTargets.Structures) != 0)
                    site.Reinforce(structureRatio);
                Add(site.NetworkObject);
            }

            if ((targets & WV_DoctrineTargets.Flag) != 0 && WV_Flag.Instance != null && !WV_Flag.Instance.IsDead)
                Add(WV_Flag.Instance.NetworkObject);

            if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
                InstanceFinder.ServerManager.Broadcast(new WV_DoctrineEffect {
                    clientId = clientId,
                    doctrine = (byte)doctrine.Id,
                    objectIds = ids.ToArray()
                });
            return ids.Count;
        }

        /// <summary>How much a multiplier grew: 1 when it did not.</summary>
        public static float Ratio(float after, float before) => before > 0.0001f ? Mathf.Max(1f, after / before) : 1f;

        static void Add(FishNet.Object.NetworkObject networkObject) {
            if (networkObject != null && networkObject.IsSpawned && ids.Count < MaxEffectTargets)
                ids.Add(networkObject.ObjectId);
        }
    }
}
