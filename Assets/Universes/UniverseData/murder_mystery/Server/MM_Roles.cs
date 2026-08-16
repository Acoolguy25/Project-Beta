using RyanAssets.DataService;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Universes.UniverseData.murder_mystery.Server {
    public class MM_Roles : MonoBehaviour {
        // NPCRoles can be one of three
        // TeamColor.Red (Murderer, least common, at least 1)
        // TeamColor.Blue (Sheriff, somewhat uncommon, at least 1) (MUST BE A PLAYER)
        // TeamColor.Green (Innocent, default assignment if no special role)
        [Header("Role Assignment Tuning")]
        [Tooltip("Base chance any single NPC/player rolls Murderer, before min/max clamping.")]
        [SerializeField] public static float murdererBaseChance = 0.08f;

        [Tooltip("Minimum number of Murderers, regardless of pool size.")]
        [SerializeField] public static int minMurderers = 1;

        [Tooltip("Murderers never exceed this fraction of the total pool (NPCs + players).")]
        [SerializeField] public static float murdererMaxRatio = 0.2f;

        [Tooltip("Base chance any single eligible player rolls Sheriff, before min/max clamping.")]
        [SerializeField] public static float sheriffBaseChance = 0.15f;

        [Tooltip("Minimum number of Sheriffs (requires at least this many players).")]
        [SerializeField] public static int minSheriffs = 1;

        [Tooltip("Sheriffs never exceed this fraction of the player pool.")]
        [SerializeField] public static float sheriffMaxRatio = 0.34f;

        public void AssignRoles(int npcCount, int playerCount, out List<TeamColor> NPCRoles, out List<TeamColor> PlayerRoles, out int startMurd, out int startSheriff, out int startInnocent) {
            NPCRoles = Enumerable.Repeat(TeamColor.Green, npcCount).ToList();
            PlayerRoles = Enumerable.Repeat(TeamColor.Green, playerCount).ToList();

            startMurd = 0;
            startSheriff = 0;
            startInnocent = 0;

            int totalCount = npcCount + playerCount;
            if (totalCount <= 0) return;

            // --- Sheriff: must be a player, so pick from PlayerRoles indices only ---
            if (playerCount > 0) {
                int sheriffTarget = Mathf.RoundToInt(playerCount * sheriffBaseChance);
                sheriffTarget = Mathf.Max(sheriffTarget, minSheriffs);
                sheriffTarget = Mathf.Min(sheriffTarget, Mathf.FloorToInt(playerCount * sheriffMaxRatio));
                sheriffTarget = Mathf.Clamp(sheriffTarget, minSheriffs, playerCount);

                List<int> playerIndices = Enumerable.Range(0, playerCount).ToList();
                Shuffle(playerIndices);
                for (int i = 0; i < sheriffTarget; i++) {
                    PlayerRoles[playerIndices[i]] = TeamColor.Blue;
                }
            }
            else {
                Debug.LogWarning("AssignRoles: no players available to assign Sheriff role.");
            }

            // --- Murderer: rarest role, can be NPC or player, at least 1 guaranteed ---
            int murdererTarget = Mathf.RoundToInt(totalCount * murdererBaseChance);
            murdererTarget = Mathf.Max(murdererTarget, minMurderers);
            murdererTarget = Mathf.Min(murdererTarget, Mathf.Max(minMurderers, Mathf.FloorToInt(totalCount * murdererMaxRatio)));
            murdererTarget = Mathf.Min(murdererTarget, totalCount);

            // Build a combined pool of (isPlayer, index), excluding players already made Sheriff
            List<(bool isPlayer, int index)> eligible = new List<(bool, int)>(totalCount);
            for (int i = 0; i < npcCount; i++) eligible.Add((false, i));
            for (int i = 0; i < playerCount; i++) {
                if (PlayerRoles[i] != TeamColor.Blue) eligible.Add((true, i));
            }

            Shuffle(eligible);
            int assigned = 0;
            for (int i = 0; i < eligible.Count && assigned < murdererTarget; i++, assigned++) {
                var (isPlayer, index) = eligible[i];
                if (isPlayer) PlayerRoles[index] = TeamColor.Red;
                else NPCRoles[index] = TeamColor.Red;
            }

            startMurd = NPCRoles.Count(r => r == TeamColor.Red) + PlayerRoles.Count(r => r == TeamColor.Red);
            startSheriff = PlayerRoles.Count(r => r == TeamColor.Blue);
            startInnocent = totalCount - startMurd - startSheriff;
        }

        private void Shuffle<T>(List<T> list) {
            for (int i = list.Count - 1; i > 0; i--) {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}