using FishNet.Object;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Works out which commander a killing blow belongs to: the owner of the structure, vehicle or
    /// troop that dealt it, or the commander themself. Feeds kill challenges, kill XP and the Bounty
    /// Hunters doctrine.
    /// </summary>
    public static class WV_KillCredit {
        /// <summary>The commander behind <paramref name="attacker"/>, or <see cref="WV_Owned.NoOwner"/>.</summary>
        public static int CommanderOf(IEntity attacker) {
            if (attacker is not Component component || component == null)
                return WV_Owned.NoOwner;

            // Structures and vehicles carry their builder's id.
            if (component.TryGetComponent(out WV_Owned owned) && owned.OwnerClientId != WV_Owned.NoOwner)
                return owned.OwnerClientId;
            // Troops are server-owned characters with a brain that remembers their commander.
            if (component.TryGetComponent(out WV_TroopBrain brain) && brain.OwnerClientId != WV_Owned.NoOwner)
                return brain.OwnerClientId;
            // A commander's own character is owned by their connection.
            if (component is NetworkBehaviour behaviour && behaviour.Owner != null && behaviour.Owner.IsValid)
                return behaviour.Owner.ClientId;
            return WV_Owned.NoOwner;
        }
    }
}
