using RyanAssets.DataService;
using System.Collections.Generic;
using UnityEngine;
using Universes.murder_mystery.Server;

namespace Universes.UniverseData.murder_mystery.Server {
    /// <summary>
    /// Deals roles. Classic deals one murderer and one sheriff (more in big rounds) by weighted draw
    /// (<see cref="MM_RoleOdds"/>): players weigh more than NPCs and gain luck every round they miss a
    /// role, so everyone gets a turn. Infection only needs a share of infected NPCs.
    /// </summary>
    public class MM_Roles : MonoBehaviour {
        [Header("Infection")]
        [Tooltip("Share of the NPCs that start infected.")]
        [SerializeField, Range(0f, 1f)] float infectedShare = 0.08f;
        [Tooltip("At least this many start infected.")]
        [SerializeField, Min(1)] int minInfected = 1;
        [Tooltip("Never more than this share of the NPCs start infected.")]
        [SerializeField, Range(0f, 1f)] float maxInfectedShare = 0.2f;

        /// <summary>How many of <paramref name="npcCount"/> NPCs start an Infection round infected.</summary>
        public int InfectedCount(int npcCount) {
            int target = Mathf.Max(Mathf.RoundToInt(npcCount * infectedShare), minInfected);
            target = Mathf.Min(target, Mathf.Max(minInfected, Mathf.FloorToInt(npcCount * maxInfectedShare)));
            return Mathf.Min(target, npcCount);
        }

        /// <summary>
        /// Classic: deals the roles to <paramref name="players"/> followed by <paramref name="npcCount"/> NPCs.
        /// Returns one role per character in that order. <paramref name="forcedFirstPlayerRole"/> (editor
        /// play-tests only) gives the first player a fixed role.
        /// </summary>
        public static MM_Role[] DealClassic(IReadOnlyList<PlayerData> players, int npcCount, MM_ClassicTuning tuning,
            MM_Role forcedFirstPlayerRole = MM_Role.None) {
            int characters = players.Count + npcCount;
            BuildWeights(players, npcCount, tuning, out List<float> murdererWeights, out List<float> sheriffWeights);
            int murderers = tuning.MurderersFor(characters);
            int sheriffs = tuning.SheriffsFor(characters);
            if (forcedFirstPlayerRole is MM_Role.Murderer or MM_Role.Sheriff or MM_Role.Innocent && players.Count > 0) {
                // Take the first player out of the draw and give them the role directly.
                murdererWeights[0] = 0f;
                sheriffWeights[0] = 0f;
                if (forcedFirstPlayerRole == MM_Role.Murderer) murderers--;
                if (forcedFirstPlayerRole == MM_Role.Sheriff) sheriffs--;
            }
            MM_Role[] roles = MM_RoleOdds.Deal(murdererWeights, sheriffWeights, murderers, sheriffs, () => Random.value);
            if (forcedFirstPlayerRole is MM_Role.Murderer or MM_Role.Sheriff or MM_Role.Innocent && players.Count > 0)
                roles[0] = forcedFirstPlayerRole;
            return roles;
        }

        /// <summary>Each player's chance (percent) of murderer and sheriff next round, with the NPCs that round would get.</summary>
        public static Dictionary<PlayerData, (byte murderer, byte sheriff)> Odds(IReadOnlyList<PlayerData> players, MM_ClassicTuning tuning) {
            int npcCount = tuning.NpcsFor(players.Count);
            int characters = players.Count + npcCount;
            BuildWeights(players, npcCount, tuning, out List<float> murdererWeights, out List<float> sheriffWeights);
            int murderers = tuning.MurderersFor(characters);
            int sheriffs = tuning.SheriffsFor(characters);
            Dictionary<PlayerData, (byte, byte)> odds = new();
            for (int i = 0; i < players.Count; i++)
                odds[players[i]] = (MM_RoleOdds.Percent(MM_RoleOdds.MurdererChance(murdererWeights, i, murderers)),
                    MM_RoleOdds.Percent(MM_RoleOdds.SheriffChance(murdererWeights, sheriffWeights, i, murderers, sheriffs)));
            return odds;
        }

        static void BuildWeights(IReadOnlyList<PlayerData> players, int npcCount, MM_ClassicTuning tuning,
            out List<float> murdererWeights, out List<float> sheriffWeights) {
            murdererWeights = new List<float>(players.Count + npcCount);
            sheriffWeights = new List<float>(players.Count + npcCount);
            foreach (PlayerData player in players) {
                MM_SaveData save = MM_SaveData.Of(player);
                murdererWeights.Add(MM_RoleOdds.Weight(true, save.murdererLuck, tuning.playerRoleWeight, tuning.luckPerRound, tuning.maxLuckRounds));
                sheriffWeights.Add(MM_RoleOdds.Weight(true, save.sheriffLuck, tuning.playerRoleWeight, tuning.luckPerRound, tuning.maxLuckRounds));
            }
            // A small lobby filled up with NPCs keeps a fair share of the big roles for its players.
            float npcWeight = MM_RoleOdds.NpcWeightFor(players.Count, npcCount, tuning.playerRoleWeight, tuning.playerShareOfBigRoles);
            for (int i = 0; i < npcCount; i++) {
                murdererWeights.Add(npcWeight);
                sheriffWeights.Add(npcWeight);
            }
        }
    }
}
