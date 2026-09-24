using System.Collections;
using RyanAssets.Shared.Component;
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

        /// <summary>
        /// How long a destroyed building's rubble stays before the plot is cleared.
        /// <para>
        /// Longer than a vehicle wreck, because a building coming down is worth watching, but it does
        /// have to end: a dead structure that never leaves blocks its own footprint, keeps its
        /// collider in every acquisition sweep, and reads to a player as something that refused to
        /// die.
        /// </para>
        /// </summary>
        const float RubbleLingerSeconds = 4f;

        readonly System.Collections.Generic.HashSet<StructureComponent> watchedStructures = new();

        void OnEnable() {
            WV_ProductionBuilding.UnitProduced += HandleUnitProduced;
            WV_ProductionBuilding.TroopProduced += HandleTroopProduced;
            EntityBase.EntityAdded += HandleEntityAdded;
            // Structures already on the field when this attaches still need their cleanup.
            foreach (EntityBase entity in EntityBase.All)
                HandleEntityAdded(entity);
        }

        void OnDisable() {
            WV_ProductionBuilding.UnitProduced -= HandleUnitProduced;
            WV_ProductionBuilding.TroopProduced -= HandleTroopProduced;
            EntityBase.EntityAdded -= HandleEntityAdded;
        }

        /// <summary>
        /// Gives every structure a death that ends in it leaving the field.
        /// <para>
        /// Walls and the objective already run their own despawn, so they are left alone rather than
        /// being despawned twice from two places.
        /// </para>
        /// </summary>
        void HandleEntityAdded(EntityBase entity) {
            if (entity is not StructureComponent structure)
                return;
            if (structure.GetComponent<WV_DestructibleObstacle>() != null)
                return;
            // Re-enabling this component re-walks the structures already on the field, so without
            // this a building would be queued for demolition once per pass.
            if (!watchedStructures.Add(structure))
                return;

            void HandleDied(DamageType source, IEntity attacker) {
                structure.OnDied -= HandleDied;
                watchedStructures.Remove(structure);
                if (isActiveAndEnabled)
                    StartCoroutine(DespawnRubble(structure));
            }

            structure.OnDied += HandleDied;
        }

        static IEnumerator DespawnRubble(StructureComponent structure) {
            if (structure == null)
                yield break;

            // A dead building stops being something that can be shot, walked into, or selected the
            // moment it dies; only the sight of it lingers.
            foreach (Collider collider in structure.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            yield return new WaitForSeconds(RubbleLingerSeconds);

            if (structure != null && structure.IsSpawned)
                structure.Despawn();
        }

        /// <summary>
        /// Hands a finished troop order to the squad roster, which owns the character prefab and the
        /// per-commander cap. A troop is not a <see cref="WV_Unit"/>, so it cannot be built here the
        /// way a vehicle is.
        /// </summary>
        static void HandleTroopProduced(WV_ProductionBuilding building, WV_TroopKind kind, int payerClientId) {
            if (WV_ServerTroops.Instance != null)
                WV_ServerTroops.Instance.TrainFromBuilding(building, kind, payerClientId);
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
