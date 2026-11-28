using FishNet;
using FishNet.Object;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Component;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Shows a doctrine landing on the field (<see cref="WV_DoctrineEffect"/>): a ring in the commander's
    /// colour under every troop, vehicle and building it changed, green crosses where it added health,
    /// gold where it pays, a short flash and a boosted health bar on each, and one line of floating text
    /// over the first of them. Every client sees it, so allies see a commander's pick too (the pick's sound
    /// is the picker's own, played by <see cref="WV_DoctrinePresenter"/>).
    /// </summary>
    public static class WV_DoctrineEffects {
        const float TroopRing = 1.1f;
        const float VehicleRing = 1.7f;
        const float BuildingRing = 2.8f;
        const float FlagRing = 3.4f;

        public static void Play(in WV_DoctrineEffect effect, WV_HealthBarLayer bars) {
            WV_DoctrineDefinition doctrine = WV_DoctrineCatalog.IsValid(effect.doctrine)
                ? WV_DoctrineCatalog.Get((WV_DoctrineId)effect.doctrine) : null;
            if (doctrine == null || effect.objectIds == null || InstanceFinder.ClientManager == null)
                return;

            Color tint = WV_Rules.GetCommanderUIColor(effect.clientId);
            WV_CommanderModifiers step = doctrine.PerRank;
            bool health = step.TroopHealthBonus > 0f || step.VehicleHealthBonus > 0f || step.StructureHealthBonus > 0f
                          || doctrine.FlagHealthBonus > 0f || doctrine.FlagRepair > 0f || step.TroopRegenPerSecond > 0f;
            bool pays = doctrine.InstantFunds > 0 || step.BountyBonus > 0f || step.IncomeBonus > 0f || step.FundsPerKill > 0
                        || step.DeliveryRebate > 0f;
            bool labelled = false;

            foreach (int id in effect.objectIds) {
                if (!InstanceFinder.ClientManager.Objects.Spawned.TryGetValue(id, out NetworkObject target) || target == null)
                    continue;
                Vector3 position = target.transform.position;
                float ring = target.TryGetComponent(out WV_Flag _) ? FlagRing
                    : target.TryGetComponent(out WV_Unit _) ? VehicleRing
                    : target.TryGetComponent(out WV_Constructable _) ? BuildingRing
                    : TroopRing;
                GameFx.Play(FxKind.ShockwaveRing, position + Vector3.up * 0.15f, tint, ring);
                if (health)
                    GameFx.Play(FxKind.Heal, position, tint, ring * 0.8f);
                else if (pays)
                    GameFx.Play(FxKind.CoinBurst, position + Vector3.up, ring * 0.7f);
                if (target.TryGetComponent(out EntityBase entity)) {
                    HitFlash.On(entity, Color.Lerp(tint, Color.white, 0.5f), 0.35f);
                    if (bars != null)
                        bars.Boost(entity, tint);
                }
                if (!labelled) {
                    labelled = true;
                    FloatingText.Spawn(position + Vector3.up * (ring + 2f), doctrine.Title.ToUpperInvariant(),
                        Color.Lerp(tint, Color.white, 0.3f), 1.3f);
                }
            }
        }
    }
}
