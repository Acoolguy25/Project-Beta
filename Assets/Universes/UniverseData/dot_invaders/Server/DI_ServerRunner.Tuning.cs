#if UNITY_SERVER
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Command-facing tuning accessors and match restart.
    public sealed partial class DI_ServerRunner {
        internal float NPCIntelligence => npcIntelligence;
        internal float TurretRange => turretRange;
        internal float TurretFireRate => turretFireRate;
        internal int MaxCapacity => maxCapacity;
        internal float MoveSpeed => moveSpeed;
        internal float MoveSpeedMultiplier => moveSpeedMultiplier;
        internal float SuperProductionSpeed => superProductionSpeed;
        internal float SuperSpeedupSeconds => superSpeedupSeconds;
        internal int SuperMaxCapacity => superMaxCapacity;
        internal float ProductionSpeed => productionSpeed;
        internal float SendInterval => sendInterval;
        internal float SendIntervalMultiplier => sendIntervalMultiplier;
        internal float SpeedBaseBonus => speedBaseBonus;
        internal float FactoryMultiplier => factoryMultiplier;
        internal float FortressMultiplier => fortressMultiplier;
        internal float NpcAggression => npcAggression;
        internal int SecondsRemaining => secondsRemaining;
        internal bool MatchInProgress => matchInProgress;

        internal float GetPlayerDamageMultiplier(int clientId) =>
            playerDamageMultipliers.TryGetValue(clientId, out float value) ? value : 1f;

        internal float GetPlayerProductionMultiplier(int clientId) =>
            playerProductionMultipliers.TryGetValue(clientId, out float value) ? value : 1f;

        internal bool SetPlayerMultiplier(int clientId, float value, bool damage) {
            if (!matchInProgress || !playerTeams.ContainsKey(clientId) || float.IsNaN(value) || float.IsInfinity(value))
                return false;
            var multipliers = damage ? playerDamageMultipliers : playerProductionMultipliers;
            value = Mathf.Clamp(value, 1f, 10f);
            if (value == 1f)
                multipliers.Remove(clientId);
            else
                multipliers[clientId] = value;
            return true;
        }

        internal void SetSuperProduction(float speed, float speedupSeconds) {
            superProductionSpeed = Mathf.Clamp(speed, 0.01f, 500f);
            superSpeedupSeconds = Mathf.Clamp(speedupSeconds, 0.01f, 600f);
            BroadcastState();
        }

        internal void SetSuperMaxCapacity(int value) {
            superMaxCapacity = Mathf.Clamp(value, 1, 10000);
            BroadcastState();
        }

        internal void SetNPCIntelligence(float value) {
            npcIntelligence = Mathf.Clamp(value, 0f, 100f);
            BroadcastState();
        }

        internal void SetTurretRange(float value) {
            turretRange = Mathf.Max(MinimumTurretRange, value);
            BroadcastState();
        }

        internal void SetTurretFireRate(float value) {
            turretFireRate = Mathf.Max(0.01f, value);
            BroadcastState();
        }

        internal void SetMaxCapacity(int value) {
            maxCapacity = Mathf.Max(1, value);
            BroadcastState();
        }

        internal void SetMoveSpeedMultiplier(float value) {
            moveSpeedMultiplier = Mathf.Clamp(value, DI_Rules.MinMoveSpeedMultiplier, DI_Rules.MaxMoveSpeedMultiplier);
            BroadcastState();
        }

        internal void SetProductionSpeed(float value) {
            productionSpeed = Mathf.Clamp(value, 0.01f, 100f);
            BroadcastState();
        }

        internal void SetSendIntervalMultiplier(float value) {
            sendIntervalMultiplier = Mathf.Clamp(value, DI_Rules.MinSendIntervalMultiplier, DI_Rules.MaxSendIntervalMultiplier);
            // Bases already mid-dispatch keep the rate they were ordered at;
            // only idle bases would otherwise never pick the new value up.
            foreach (BaseState state in bases)
                if (state.pendingTroops == 0)
                    state.sendInterval = sendInterval;
            BroadcastState();
        }

        internal void SetSpeedBaseBonus(float value) {
            speedBaseBonus = Mathf.Clamp(value, 0f, 5f);
            BroadcastState();
        }

        internal void SetFactoryMultiplier(float value) {
            factoryMultiplier = Mathf.Clamp(value, DI_Rules.MinSpecialBaseMultiplier, DI_Rules.MaxSpecialBaseMultiplier);
            BroadcastState();
        }

        internal void SetFortressMultiplier(float value) {
            fortressMultiplier = Mathf.Clamp(value, DI_Rules.MinSpecialBaseMultiplier, DI_Rules.MaxSpecialBaseMultiplier);
            BroadcastState();
        }

        internal void SetNpcAggression(float value) {
            npcAggression = Mathf.Clamp(value, 0f, 100f);
            BroadcastState();
        }

        internal void SetSecondsRemaining(int value) {
            secondsRemaining = Mathf.Clamp(value, 1, 7200);
            UpdateTopMessage();
            BroadcastState();
        }

        /// <summary>Rerolls the board without disturbing the running match clock.</summary>
        internal bool RestartMatch() {
            // The countdown lives in StartAsync; restarting after it exits would
            // leave a match that never ticks down, so only reroll a live match.
            if (!matchInProgress)
                return false;
            InitializeMatch();
            return true;
        }
    }
}
#endif
