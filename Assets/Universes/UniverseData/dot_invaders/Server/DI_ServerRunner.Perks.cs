#if UNITY_SERVER
using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Retention;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.dot_invaders {
    // Commander perks (unlocked by level, equipped between matches) and the Overdrive ability.
    public sealed partial class DI_ServerRunner {
        /// <summary>Shortest time between two perk requests from one client that are answered (seconds).</summary>
        const float PerkRequestInterval = 0.2f;

        sealed class OverdriveState {
            public bool used;
            /// <summary>Match time (<see cref="matchElapsed"/>) at which it stops.</summary>
            public float endsAt;
        }

        // Effects locked in for each player team when it entered the match.
        readonly Dictionary<int, DI_PerkEffects> teamEffects = new();
        // Teams that have Overdrive this match.
        readonly Dictionary<int, OverdriveState> overdrives = new();
        // Players whose loadout waits for their save to load.
        readonly HashSet<int> loadoutPending = new();
        readonly Dictionary<int, float> nextPerkRequestTime = new();
        readonly List<int> overdriveTeamsScratch = new();

        DI_PerkEffects EffectsOf(int teamId) =>
            teamId >= 0 && teamEffects.TryGetValue(teamId, out DI_PerkEffects effects) ? effects : DI_PerkEffects.None;

        /// <summary>Training multiplier of a team's bases: its perks and a running Overdrive.</summary>
        float TeamProductionMultiplier(int teamId) =>
            EffectsOf(teamId).production * (OverdriveRunning(teamId) ? overdrive.productionMultiplier : 1f);

        bool OverdriveRunning(int teamId) =>
            overdrives.TryGetValue(teamId, out OverdriveState state) && state.used && matchElapsed < state.endsAt;

        /// <summary>The player's saved loadout as their level allows it (empty while their save loads).</summary>
        List<string> LoadoutOf(NetworkConnection connection) {
            if (retention == null || !retention.IsReady(connection) || !PlayerProgress.TryGet(connection, out DI_SaveData save))
                return new List<string>();
            return DI_Perks.Sanitize(save.perks, retention.LevelOf(connection));
        }

        /// <summary>Locks the player's perks in for this match; waits for their save if it is still loading.</summary>
        void ApplyLoadout(NetworkConnection connection, int teamId) {
            if (retention == null || !retention.IsReady(connection)) {
                loadoutPending.Add(connection.ClientId);
                return;
            }
            loadoutPending.Remove(connection.ClientId);
            DI_PerkEffects effects = DI_Perks.EffectsOf(LoadoutOf(connection));
            teamEffects[teamId] = effects;
            if (effects.startTroops > 0) {
                foreach (BaseState state in bases)
                    if (state.ownerClientId == connection.ClientId) {
                        state.troops += effects.startTroops;
                        break;
                    }
            }
            if (effects.overdrive)
                overdrives[teamId] = new OverdriveState();
            else
                overdrives.Remove(teamId);
        }

        /// <summary>A save that loaded just after the match began still brings its perks, inside a short window.</summary>
        void ApplyLateLoadout(NetworkConnection connection) {
            RefreshEnteredRating(connection);
            if (!loadoutPending.Remove(connection.ClientId))
                return;
            if (!matchInProgress || matchElapsed > lateLoadoutSeconds || !playerTeams.TryGetValue(connection.ClientId, out int team) ||
                !dotInvadersTeams.Contains(team))
                return;
            ApplyLoadout(connection, team);
            BroadcastState();
        }

        void OnPerkRequest(NetworkConnection connection, DI_PerkRequest request, Channel channel) {
            if (!connection.IsValid || retention == null || !retention.IsReady(connection))
                return;
            float now = Time.unscaledTime;
            if (nextPerkRequestTime.TryGetValue(connection.ClientId, out float next) && now < next)
                return;
            nextPerkRequestTime[connection.ClientId] = now + PerkRequestInterval;
            List<string> loadout = DI_Perks.Sanitize(request.perks, retention.LevelOf(connection));
            PlayerProgress.Modify<DI_SaveData>(connection, save => save.perks = new List<string>(loadout));
            SendProfile(connection);
        }

        /// <summary>A newly unlocked perk goes straight into a free slot, and the player is told how to change it.</summary>
        void OnUnlocked(NetworkConnection connection, UnlockDefinition unlock) {
            string note;
            if (DI_Perks.Find(unlock.id) is { } perk) {
                List<string> loadout = LoadoutOf(connection);
                if (!loadout.Contains(perk.Id) && loadout.Count < DI_Perks.SlotsAt(retention.LevelOf(connection))) {
                    loadout.Add(perk.Id);
                    PlayerProgress.Modify<DI_SaveData>(connection, save => save.perks = new List<string>(loadout));
                    note = $"{perk.Title} unlocked and equipped for your next match. Press P to change perks.";
                } else {
                    note = $"{perk.Title} unlocked! Press P to equip it.";
                }
            } else if (unlock.id == DI_Perks.SecondSlot) {
                note = "Second perk slot unlocked! Press P to equip another perk.";
            } else {
                return;
            }
            ServerChat.SendSystemMessage(connection, new SystemMessageBroadcast(
                $"<color={DI_Rules.ChatAccentHex}>{note}</color>", SystemMessageSource.CustomMessage));
            SendProfile(connection);
        }

        void OnAbilityRequest(NetworkConnection connection, DI_AbilityRequest request, Channel channel) {
            if (request.ability != DI_AbilityRequest.Overdrive || !matchInProgress || !connection.IsValid ||
                !playerTeams.TryGetValue(connection.ClientId, out int team) || AbilityStateOf(team) != DI_AbilityState.Ready)
                return;
            OverdriveState state = overdrives[team];
            state.used = true;
            state.endsAt = matchElapsed + overdrive.seconds;
            SendTeamChatMessage(team, "activated OVERDRIVE!");
            BroadcastState();
        }

        DI_AbilityState AbilityStateOf(int teamId) {
            if (teamId < 0 || !overdrives.TryGetValue(teamId, out OverdriveState state))
                return DI_AbilityState.None;
            if (!state.used)
                return matchElapsed < overdrive.lockedSeconds ? DI_AbilityState.Locked : DI_AbilityState.Ready;
            return matchElapsed < state.endsAt ? DI_AbilityState.Active : DI_AbilityState.Spent;
        }

        float AbilitySecondsOf(int teamId, DI_AbilityState state) => state switch {
            DI_AbilityState.Active => Mathf.Max(0f, overdrives[teamId].endsAt - matchElapsed),
            DI_AbilityState.Locked => Mathf.Max(0f, overdrive.lockedSeconds - matchElapsed),
            _ => 0f,
        };

        int[] OverdriveTeams() {
            overdriveTeamsScratch.Clear();
            foreach (KeyValuePair<int, OverdriveState> entry in overdrives)
                if (entry.Value.used && matchElapsed < entry.Value.endsAt)
                    overdriveTeamsScratch.Add(entry.Key);
            return overdriveTeamsScratch.ToArray();
        }

        /// <summary>Forgets everything perks, rating and the kit tracked for the match that is ending.</summary>
        void ClearMatchProgression() {
            teamEffects.Clear();
            overdrives.Clear();
            loadoutPending.Clear();
            ClearRatingState();
        }

        void ForgetPlayerProgression(int clientId) {
            loadoutPending.Remove(clientId);
            nextPerkRequestTime.Remove(clientId);
            ratedPlayers.Remove(clientId);
        }
    }
}
#endif
