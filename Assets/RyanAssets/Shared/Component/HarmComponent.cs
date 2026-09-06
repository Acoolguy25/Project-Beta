using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Shared.Declarations;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Shared.Component {
    public class HarmComponent : NetworkBehaviour {
        [SerializeField]
        private DamageConfig damageConfig = new();

        [SerializeField, Min(0f)]
        private float repeatDelay = 1.5f;

        [SerializeField]
        private TeamColor[] targetTeams;

        private readonly Dictionary<EntityBase, float> nextHarmTimes = new();

        private void OnCollisionStay(Collision collision) {
            TryHarm(collision.collider);
        }

        //private void OnTriggerStay(Collider other) {
        //    TryHarm(other);
        //}

        private void TryHarm(Collider other) {
            EntityBase character = other.GetComponentInParent<EntityBase>();

            if (character == null || !CanTarget(character))
                return;

            if (nextHarmTimes.TryGetValue(character, out float nextHarmTime) &&
                Time.time < nextHarmTime)
                return;

            nextHarmTimes[character] = Time.time + repeatDelay;

#if UNITY_SERVER
            HarmServerRpc(character);
#else
            Harm(character);
#endif
        }

        private bool CanTarget(EntityBase character) {
            if (!character.IsController)
                return false;

            if (targetTeams.Length == 0)
                return true;

            return targetTeams.Contains(character.Team.realTeam);
        }

        [ServerRpc(RequireOwnership = false)]
        private void HarmServerRpc(EntityBase character, NetworkConnection conn = null) {
#if UNITY_SERVER
            if (character.Owner != conn) {
                conn.Kick(FishNet.Managing.Server.KickReason.ExploitAttempt);
                return;
            }

            Harm(character);
#endif
        }
        private void Harm(EntityBase character) {
            character.TakeDamage(damageConfig.damageAmount, damageConfig.damageType);
        }
    }
}
