using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Anything a commander can give an order to.
    /// <para>
    /// War Valley has two kinds: the vehicles a production building turns out
    /// (<see cref="WV_UnitBrain"/>) and the foot soldiers a player trains directly
    /// (<see cref="WV_TroopBrain"/>). They move through completely different machinery - a bespoke
    /// motor versus <c>LocalNPC</c> - but a right click means the same thing to both, so the order
    /// receiver validates ownership once and dispatches through this rather than branching on type.
    /// </para>
    /// </summary>
    public interface WV_ICommandable {
        void OrderMove(Vector3 destination);
        void OrderAttackMove(Vector3 destination);
        void OrderAttack(IEntity target);
        void OrderStop();
        void OrderHoldPosition();
    }
}
