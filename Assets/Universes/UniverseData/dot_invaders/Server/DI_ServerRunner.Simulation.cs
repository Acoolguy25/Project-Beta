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
    // Per-step simulation: base production, dot movement, turrets and arrivals.
    public sealed partial class DI_ServerRunner {
        void UpdateBases(float deltaTime) {
            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.teamId < 0)
                    continue;

                state.productionDelay = Mathf.Max(0f, state.productionDelay - deltaTime);
                if (state.pendingTroops > 0) {
                    state.actionTimer += deltaTime;
                    while (state.actionTimer >= state.sendInterval) {
                        state.actionTimer -= state.sendInterval;
                        if (state.troops <= 0 || state.pendingTarget < 0) {
                            ClearSend(state);
                            break;
                        }

                        dots.Add(new DotState {
                            id = nextDotId++,
                            originBaseId = i,
                            sourceBaseId = i,
                            targetBaseId = state.pendingTarget,
                            ownerClientId = state.ownerClientId,
                            teamId = state.teamId,
                            route = state.pendingRoute,
                            routeIndex = 1
                        });
                        state.troops--;
                        CountStat(state.ownerClientId, DI_Retention.Stats.TroopsSent);
                        if (state.troops == 0)
                            state.damageTaken = 0f;
                        state.pendingTroops--;
                        if (state.pendingTroops == 0) {
                            ClearSend(state);
                            break;
                        }
                    }
                    continue;
                }

                if (state.isTurret || state.productionDelay > 0f || HasOutgoingTroops(i)) {
                    InterruptProduction(state);
                    continue;
                }

                float production = state.isSuperProducer
                    ? DI_Rules.SuperProductionOverInterval(state.uninterruptedSeconds, deltaTime,
                        superProductionSpeed, superSpeedupSeconds)
                    : deltaTime * DI_Rules.BaseProductionRate(productionSpeed, state.isFactory, factoryMultiplier);
                production *= ProductionMultiplier(state);
                state.uninterruptedSeconds = Mathf.Min(superSpeedupSeconds, state.uninterruptedSeconds + deltaTime);
                if (state.troops >= Capacity(state)) {
                    state.actionTimer = 0f;
                    continue;
                }
                state.actionTimer += production;
                while (state.actionTimer >= 1f && state.troops < Capacity(state)) {
                    state.actionTimer -= 1f;
                    state.troops++;
                }
            }
        }

        bool HasOutgoingTroops(int sourceBaseId) {
            for (int i = 0; i < dots.Count; i++) {
                if (dots[i].originBaseId == sourceBaseId && dots[i].teamId == bases[sourceBaseId].teamId)
                    return true;
            }
            return false;
        }

        void QueueSend(BaseState source, int targetBaseId, int troopCount, float dispatchInterval) {
            source.pendingTarget = targetBaseId;
            source.pendingTroops = Mathf.Clamp(troopCount, 0, source.troops);
            source.sendInterval = Mathf.Max(0.05f, dispatchInterval);
            InterruptProduction(source);
            source.pendingRoute = null;
        }

        void ClearSend(BaseState state) {
            state.pendingTarget = -1;
            state.pendingTroops = 0;
            state.actionTimer = 0f;
            state.sendInterval = sendInterval;
            state.pendingRoute = null;
        }

        void RefreshTeamSpeedBases() {
            teamSpeedBases.Clear();
            foreach (BaseState state in bases) {
                if (!state.isSpeedBase || state.teamId < 0)
                    continue;
                teamSpeedBases.TryGetValue(state.teamId, out int count);
                teamSpeedBases[state.teamId] = count + 1;
            }
        }

        void UpdateDots(float deltaTime) {
            while (deltaTime > 0f && dots.Count > 0) {
                float step = Mathf.Min(deltaTime, SimulationStep);
                // Split at waypoints: every collision sweep is a straight road
                // segment, and unused time carries onto the next leg this tick.
                foreach (DotState dot in dots) {
                    float distance = Vector2.Distance(bases[dot.sourceBaseId].position, bases[dot.targetBaseId].position);
                    float arrival = (1f - dot.progress) * distance / TeamMoveSpeed(dot.teamId);
                    step = Mathf.Min(step, Mathf.Max(0.000001f, arrival));
                }
                StepDots(step);
                deltaTime = Mathf.Max(0f, deltaTime - step);
            }
            if (deltaTime > 0f)
                UpdateTurrets(deltaTime);
        }

        void StepDots(float deltaTime) {
            // Shoot before advancing/arriving so even a troop on its final step
            // is still a moving target. Stationary garrisons are never queried.
            UpdateTurrets(deltaTime);
            previousDotPositions.Clear();
            for (int i = 0; i < dots.Count; i++) {
                DotState dot = dots[i];
                previousDotPositions.Add(GetDotPosition(dot));
                float distance = Vector2.Distance(
                    bases[dot.sourceBaseId].position,
                    bases[dot.targetBaseId].position);

                dot.progress = Mathf.Min(1f, dot.progress + TeamMoveSpeed(dot.teamId) * deltaTime / Mathf.Max(0.000001f, distance));
                if (dot.progress >= 0.999999f)
                    dot.progress = 1f;
            }

            HashSet<int> destroyed = destroyedDots;
            destroyed.Clear();
            float collisionDistanceSquared = CollisionDistance * CollisionDistance;
            for (int i = 0; i < dots.Count; i++) {
                if (destroyed.Contains(i))
                    continue;

                Vector2 firstPosition = GetDotPosition(dots[i]);
                for (int j = i + 1; j < dots.Count; j++) {
                    if (destroyed.Contains(j) || dots[i].teamId == dots[j].teamId)
                        continue;

                    Vector2 relativeStart = previousDotPositions[i] - previousDotPositions[j];
                    Vector2 relativeEnd = firstPosition - GetDotPosition(dots[j]);
                    Vector2 relativeTravel = relativeEnd - relativeStart;
                    float closestTime = relativeTravel.sqrMagnitude > 0f
                        ? Mathf.Clamp01(-Vector2.Dot(relativeStart, relativeTravel) / relativeTravel.sqrMagnitude) : 0f;
                    if ((relativeStart + relativeTravel * closestTime).sqrMagnitude <= collisionDistanceSquared) {
                        RecordClash((firstPosition + GetDotPosition(dots[j])) * 0.5f);
                        float firstDamage = ClashDamage(dots[i]);
                        float secondDamage = ClashDamage(dots[j]);
                        float exchange = Mathf.Min(dots[i].health / secondDamage, dots[j].health / firstDamage);
                        dots[i].health -= secondDamage * exchange;
                        dots[j].health -= firstDamage * exchange;
                        if (dots[j].health <= 0.00001f) {
                            destroyed.Add(j);
                            CountStat(dots[i].ownerClientId, DI_Retention.Stats.TroopsDefeated);
                        }
                        if (dots[i].health <= 0.00001f) {
                            destroyed.Add(i);
                            CountStat(dots[j].ownerClientId, DI_Retention.Stats.TroopsDefeated);
                            break;
                        }
                    }
                }
            }

            for (int i = dots.Count - 1; i >= 0; i--) {
                DotState dot = dots[i];
                if (destroyed.Contains(i)) {
                    dots.RemoveAt(i);
                } else if (dot.progress >= 1f) {
                    if (!ContinueRoute(dot)) {
                        ResolveArrival(dot);
                        dots.RemoveAt(i);
                    }
                }
            }
        }

        int TeamSpeedBaseCount(int teamId) =>
            teamId >= 0 && teamSpeedBases.TryGetValue(teamId, out int count) ? count : 0;

        /// <summary>Board units per second a team's troops actually travel at: speed bases and the team's perks.</summary>
        float TeamMoveSpeed(int teamId) =>
            DI_Rules.TeamMoveSpeed(moveSpeed, TeamSpeedBaseCount(teamId), speedBaseBonus) * EffectsOf(teamId).moveSpeed;

        /// <summary>Damage a troop deals in a road clash: the admin's multiplier and its team's clash perk.</summary>
        float ClashDamage(DotState dot) => GetPlayerDamageMultiplier(dot.ownerClientId) * EffectsOf(dot.teamId).clash;

        bool ContinueRoute(DotState dot) {
            // Hostile intermediate bases must be captured before later troops
            // can pass through. Friendly waypoints do not consume garrison space.
            if (bases[dot.targetBaseId].teamId != dot.teamId || dot.route == null ||
                dot.routeIndex + 1 >= dot.route.Length)
                return false;
            dot.sourceBaseId = dot.targetBaseId;
            dot.targetBaseId = dot.route[++dot.routeIndex];
            dot.progress = 0f;
            return true;
        }

        Vector2 GetDotPosition(DotState dot) {
            return Vector2.Lerp(
                bases[dot.sourceBaseId].position,
                bases[dot.targetBaseId].position,
                Mathf.Clamp01(dot.progress));
        }

        void UpdateTurrets(float deltaTime) {
            foreach (BaseState turret in bases) {
                turret.turretCooldown = Mathf.Max(0f, turret.turretCooldown - deltaTime);
                if (!turret.isTurret || turret.turretCooldown > 0f)
                    continue;

                int target = -1;
                float nearest = turretRange * turretRange;
                Vector2 shotPosition = default;
                for (int i = 0; i < dots.Count; i++) {
                    DotState dot = dots[i];
                    if (turret.teamId >= 0 && dot.teamId == turret.teamId || dot.progress >= 1f)
                        continue;
                    Vector2 start = GetDotPosition(dot);
                    Vector2 end = Vector2.MoveTowards(start, bases[dot.targetBaseId].position, TeamMoveSpeed(dot.teamId) * deltaTime);
                    Vector2 travel = end - start;
                    float along = travel.sqrMagnitude > 0f
                        ? Mathf.Clamp01(Vector2.Dot(turret.position - start, travel) / travel.sqrMagnitude) : 0f;
                    Vector2 closest = start + travel * along;
                    float distance = (closest - turret.position).sqrMagnitude;
                    if (distance <= nearest) {
                        nearest = distance;
                        target = i;
                        shotPosition = closest;
                    }
                }
                if (target < 0)
                    continue;
                turret.shotPosition = shotPosition;
                turret.shotSequence++;
                turret.turretCooldown = turretFireRate;
                dots.RemoveAt(target);
            }
        }

        void ResolveArrival(DotState dot) {
            BaseState target = bases[dot.targetBaseId];
            // Allied waypoints bypass arrival; only actual recipients interrupt training.
            // Preserve the sending timer if this base is also dispatching troops.
            target.uninterruptedSeconds = 0f;
            if (target.pendingTroops == 0)
                target.actionTimer = 0f;
            target.productionDelay = AttackedProductionDelay;
            if (target.teamId == dot.teamId) {
                target.troops++;
                return;
            }

            if (target.troops > 0) {
                float attack = GetPlayerDamageMultiplier(dot.ownerClientId) * EffectsOf(dot.teamId).siege * dot.health;
                target.damageTaken += DI_Rules.ArrivalDamage(attack, DefenceOf(target));
                int defeated = Mathf.Min(target.troops, Mathf.FloorToInt(target.damageTaken + 0.00001f));
                target.troops -= defeated;
                if (defeated > 0)
                    CountStat(dot.ownerClientId, DI_Retention.Stats.TroopsDefeated, defeated);
                target.damageTaken -= defeated;
                if (target.troops == 0)
                    target.damageTaken = 0f;
                return;
            }

            RecordCaptureFrom(target.teamId, dot.ownerClientId);
            target.ownerClientId = dot.ownerClientId;
            target.teamId = dot.teamId;
            target.troops = 1;
            target.damageTaken = 0f;
            target.turretCooldown = turretFireRate;
            ClearSend(target);
            RecordCapture(dot.ownerClientId, dot.teamId, target);
            ClaimBountyIfMarked(dot.targetBaseId, dot.ownerClientId, dot.teamId);
        }

        /// <summary>Queues a clash for the next snapshot's effects; presentation only.</summary>
        void RecordClash(Vector2 position) {
            if (clashPositions.Count < MaxClashEventsPerSnapshot)
                clashPositions.Add(position);
        }
    }
}
#endif
