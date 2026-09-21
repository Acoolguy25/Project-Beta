using System.Collections;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Owns the server-side lifecycle of produced units: giving each one a brain, walking it to its
    /// building's rally point, and clearing the wreck once it dies.
    /// <para>
    /// The brain cannot be serialized onto the unit prefab, because this assembly is compiled only
    /// under <c>UNITY_SERVER</c> and a client build would load the prefab with a missing script. The
    /// shared production component therefore raises an event here the moment a unit spawns.
    /// </para>
    /// </summary>
    public sealed class WV_ServerArmy : MonoBehaviour {
        /// <summary>How long a destroyed unit's wreck stays on the field before it is despawned.</summary>
        const float WreckLingerSeconds = 6f;

        void OnEnable() {
            WV_ProductionBuilding.UnitProduced += HandleUnitProduced;
        }

        void OnDisable() {
            WV_ProductionBuilding.UnitProduced -= HandleUnitProduced;
        }

        void HandleUnitProduced(WV_ProductionBuilding building, WV_Unit unit) {
            if (unit == null)
                return;

            WV_UnitBrain brain = WV_UnitBrain.Attach(unit);
            // A unit leaves the factory already walking to the gather point, so a player who set a
            // rally point at the front does not have to re-order every batch by hand.
            brain?.OrderMove(building.RallyPoint);

            WatchForDeath(unit);
        }

        void WatchForDeath(WV_Unit unit) {
            void HandleDied(DamageType source, IEntity attacker) {
                unit.OnDied -= HandleDied;
                if (isActiveAndEnabled)
                    StartCoroutine(DespawnWreck(unit));
            }

            unit.OnDied += HandleDied;
        }

        static IEnumerator DespawnWreck(WV_Unit unit) {
            if (unit == null)
                yield break;

            // Stop the corpse from steering, shooting, or blocking navigation while it lingers.
            foreach (WV_UnitBrain brain in unit.GetComponents<WV_UnitBrain>())
                brain.enabled = false;
            foreach (WV_UnitMotor motor in unit.GetComponents<WV_UnitMotor>()) {
                motor.Stop();
                motor.enabled = false;
            }
            if (unit.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent))
                agent.enabled = false;
            foreach (Collider collider in unit.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            yield return new WaitForSeconds(WreckLingerSeconds);

            if (unit != null && unit.IsSpawned)
                unit.Despawn();
        }
    }
}
