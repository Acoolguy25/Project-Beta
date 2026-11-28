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
    // NPC team AI: targeting, route planning, forecasting and consolidation.
    public sealed partial class DI_ServerRunner {
        void UpdateNpcTeams(float deltaTime) {
            for (int teamIndex = 0; teamIndex < npcTeams.Count; teamIndex++) {
                NpcTeamState npcTeam = npcTeams[teamIndex];
                npcTeam.thinkTimer += deltaTime;
                float intelligence = npcIntelligence / 100f;
                float thinkInterval = Mathf.Lerp(NpcSlowThinkInterval, NpcFastThinkInterval, intelligence);
                if (npcTeam.thinkTimer < thinkInterval)
                    continue;
                npcTeam.thinkTimer %= thinkInterval;
                float aggression = npcAggression / 100f;
                float teamSpeed = TeamMoveSpeed(npcTeam.teamId);

                // An objective that is already ours is no longer an objective.
                if (npcTeam.focusTargetBaseId >= bases.Count ||
                    npcTeam.focusTargetBaseId >= 0 && bases[npcTeam.focusTargetBaseId].teamId == npcTeam.teamId)
                    npcTeam.focusTargetBaseId = -1;

                if (TryProtectNpcBase(npcTeam.teamId, intelligence))
                    continue;

                // Recovering a doomed wave happens before planning, so the freed
                // garrison can be spent on a target it can still take this think.
                TryAbandonHopelessAttack(npcTeam.teamId, intelligence);

                int bestSource = -1;
                int bestTroopCount = 0;
                int[] bestRoute = null;
                float bestScore = float.MinValue;
                for (int sourceId = 0; sourceId < bases.Count; sourceId++) {
                    BaseState source = bases[sourceId];
                    if (source.teamId != npcTeam.teamId || source.pendingTroops > 0 || HasOutgoingTroops(sourceId) ||
                        source.troops <= NpcReserveForBase(sourceId, npcTeam.teamId))
                        continue;

                    int reserve = NpcReserveForBase(sourceId, npcTeam.teamId);
                    int available = source.troops - reserve;
                    // Let a charging super base build a meaningful wave unless a base is in danger.
                    if (ShouldChargeNpcBase(source))
                        continue;

                    for (int targetId = 0; targetId < bases.Count; targetId++) {
                        BaseState target = bases[targetId];
                        if (target.teamId == npcTeam.teamId)
                            continue;

                        int[] route = FindNpcRoute(sourceId, targetId, npcTeam.teamId, false, intelligence);
                        if (route.Length < 2)
                            continue;
                        float routeDistance = RouteDistance(route);
                        int losses = EstimateTurretLosses(route, npcTeam.teamId, available, sendInterval);
                        // Speed bases make this team's waves land sooner. Planning with
                        // the base speed over-estimates how much the defender can train.
                        float travelTime = routeDistance / teamSpeed;
                        // Estimate the actual ramp before first contact. Using peak
                        // speed here makes a charging super base seem untouchable.
                        int growth = EstimateDefenderGrowth(targetId, travelTime);
                        // The garrison on the board is only part of what a wave meets. A
                        // neighbour walking in, a rival wave landing first, and a hostile
                        // stream already on our road all change the real cost, and reading
                        // them is most of what separates a strong team from one that keeps
                        // sending waves which are always just slightly too small.
                        int reinforcements = ScaleForecast(
                            EstimateDefenderReinforcements(targetId, travelTime), intelligence * 0.5f);
                        int thirdParty = ScaleForecast(
                            EstimateThirdPartyDamage(targetId, npcTeam.teamId, travelTime), intelligence);
                        int interception = ScaleForecast(
                            EstimateInterceptionLosses(route, npcTeam.teamId), intelligence);
                        // A fortress garrison takes more attackers per defender to empty.
                        int defense = DI_Rules.AttackersToDefeat(
                            target.troops + growth + reinforcements - thirdParty, DefenceOf(target));
                        int committed = CountIncomingTroops(targetId, npcTeam.teamId);
                        // A cautious team insists on a wider margin than an aggressive one.
                        int margin = Mathf.RoundToInt(Mathf.Lerp(6f, 1f, aggression));
                        if (committed >= defense + losses + interception + margin)
                            continue;
                        int required = Mathf.Max(1, defense + losses + interception + margin - committed);
                        if (available < required)
                            continue;

                        int sending = source.isSuperProducer ? available
                            : Mathf.Min(available, required + Mathf.Max(2, required / 5));
                        float objectivePriority = NpcTargetPriority(targetId, npcTeam.teamId);
                        // A garrison sitting at capacity has stopped producing, so
                        // spending it costs nothing and idling wastes the base.
                        float idleCapacity = source.troops >= Capacity(source) ? 40f : 0f;
                        // Finishing what the team started is worth more than a marginally
                        // better new idea every couple of seconds.
                        float focus = targetId == npcTeam.focusTargetBaseId ? NpcFocusBonus : 0f;
                        float strategicScore = objectivePriority + idleCapacity + focus + (sending - required) * 1.5f -
                            (losses + interception) * 5f - travelTime - (source.isSuperProducer ? 12f : 0f);
                        float score = Mathf.Lerp(UnityEngine.Random.Range(0f, 100f), strategicScore, intelligence);
                        if (score <= bestScore)
                            continue;

                        bestScore = score;
                        bestSource = sourceId;
                        bestTroopCount = sending;
                        bestRoute = route;
                    }
                }

                if (bestSource >= 0) {
                    BaseState source = bases[bestSource];
                    QueueSend(source, bestRoute[1], bestTroopCount, sendInterval);
                    source.pendingRoute = bestRoute;
                    npcTeam.focusTargetBaseId = bestRoute[bestRoute.Length - 1];
                    CommitSupportingWaves(npcTeam.teamId, bestSource, bestRoute, intelligence);
                } else
                    TryConsolidateNpcTroops(npcTeam.teamId, intelligence);
            }
        }

        bool ShouldChargeNpcBase(BaseState source) {
            // Sending resets the entire ramp. Wait until the configured production
            // cap is nearly full, then spend one wave instead of repeated trickles.
            return source.isSuperProducer && source.troops < Mathf.CeilToInt(
                Capacity(source) * Mathf.Clamp(npcSuperLaunchCapacityFraction, 0.1f, 1f));
        }

        bool TryProtectNpcBase(int teamId, float intelligence) {
            int threatenedBase = -1;
            float greatestUrgency = 0f;
            for (int i = 0; i < bases.Count; i++) {
                BaseState target = bases[i];
                if (target.teamId != teamId)
                    continue;
                int hostile = HostileThreat(i, teamId);
                if (hostile <= 0)
                    continue;
                int friendly = CountIncomingTroops(i, teamId);
                float urgency = hostile - friendly - Mathf.Max(0, target.troops - target.pendingTroops) +
                    (target.isSuperProducer ? 14f : target.isSpeedBase || target.isFactory ? 10f : 4f);
                if (urgency > greatestUrgency) {
                    greatestUrgency = urgency;
                    threatenedBase = i;
                }
            }
            if (threatenedBase < 0)
                return false;

            BaseState threatened = bases[threatenedBase];
            if (threatened.pendingTroops > 0 && HostileThreat(threatenedBase, teamId) >
                threatened.troops - threatened.pendingTroops + CountIncomingTroops(threatenedBase, teamId)) {
                ClearSend(threatened);
                if (HostileThreat(threatenedBase, teamId) <=
                    threatened.troops + CountIncomingTroops(threatenedBase, teamId))
                    return true;
            }

            int bestSource = -1;
            int[] bestRoute = null;
            float bestScore = float.MinValue;
            int needed = Mathf.Max(1, HostileThreat(threatenedBase, teamId) -
                CountIncomingTroops(threatenedBase, teamId) - bases[threatenedBase].troops + NpcFrontlineReserve);
            for (int i = 0; i < bases.Count; i++) {
                BaseState source = bases[i];
                if (i == threatenedBase || source.teamId != teamId || source.pendingTroops > 0 || HasOutgoingTroops(i))
                    continue;
                int available = source.troops - NpcReserveForBase(i, teamId);
                if (available <= 0)
                    continue;
                int[] route = FindNpcRoute(i, threatenedBase, teamId, true, intelligence);
                if (route.Length < 2)
                    continue;
                float score = Mathf.Min(available, needed) * 5f - RouteDistance(route) -
                    (source.isSuperProducer ? 10f : 0f);
                if (score > bestScore) {
                    bestScore = score;
                    bestSource = i;
                    bestRoute = route;
                }
            }
            if (bestSource < 0)
                return false;

            BaseState reinforcement = bases[bestSource];
            int troopCount = Mathf.Min(needed, reinforcement.troops - NpcReserveForBase(bestSource, teamId));
            QueueSend(reinforcement, bestRoute[1], troopCount, sendInterval);
            reinforcement.pendingRoute = bestRoute;
            return true;
        }

        /// <summary>Defenders the hostile troops heading for this base can defeat; fortresses halve the threat.</summary>
        int HostileThreat(int baseId, int teamId) =>
            DI_Rules.DefendersDefeatedBy(CountHostileIncomingTroops(baseId, teamId), DefenceOf(bases[baseId]));

        int NpcReserveForBase(int baseId, int teamId) {
            BaseState state = bases[baseId];
            int reserve = state.isSuperProducer ? NpcSuperBaseReserve
                : state.isSpeedBase ? NpcSpeedBaseReserve
                : NpcTroopReserve;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == baseId ? link.y : link.y == baseId ? link.x : -1;
                if (neighbor >= 0 && bases[neighbor].teamId != teamId) {
                    reserve = Mathf.Max(reserve, NpcFrontlineReserve);
                    break;
                }
            }
            // An aggressive team keeps less at home and commits the difference.
            reserve = Mathf.CeilToInt(reserve * Mathf.Lerp(1.5f, 0.6f, npcAggression / 100f));
            // An enemy stack next door is a threat before it ever launches. Reading it
            // is what stops a team being counter-attacked out of the base it just spent
            // its whole army taking.
            int adjacentThreat = 0;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == baseId ? link.y : link.y == baseId ? link.x : -1;
                if (neighbor >= 0 && bases[neighbor].teamId >= 0 && bases[neighbor].teamId != teamId)
                    adjacentThreat = Mathf.Max(adjacentThreat, bases[neighbor].troops);
            }
            return Mathf.Min(reserve, Mathf.Max(0, Capacity(state) - 1)) +
                Mathf.Min(10, CountHostileIncomingTroops(baseId, teamId)) +
                Mathf.Min(12, ScaleForecast(adjacentThreat, 0.4f * (npcIntelligence / 100f)));
        }

        float NpcTargetPriority(int targetId, int teamId) {
            BaseState target = bases[targetId];
            float priority = target.teamId < 0 ? 24f : target.ownerClientId >= 0 ? 82f : 58f;
            if (target.isTurret)
                priority += 55f;
            if (target.isSuperProducer)
                priority += target.teamId < 0 ? 105f : 85f;
            if (target.isSpeedBase) {
                // Each speed base multiplies this team's entire army speed, so the
                // first one is worth far more than a duplicate.
                priority += (target.teamId < 0 ? 70f : 56f) / (1 + TeamSpeedBaseCount(teamId));
            }
            // A factory doubles a base's output for good; a fortress is a strong point to hold.
            if (target.isFactory)
                priority += target.teamId < 0 ? NpcFactoryPriority : NpcFactoryPriority * 0.8f;
            if (target.isFortress)
                priority += NpcFortressPriority;
            // A bounty pays bonus troops to whoever takes it first, so it is worth racing for.
            if (targetId == bountyBaseId && target.teamId != teamId)
                priority += NpcBountyPriority;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == targetId ? link.y : link.y == targetId ? link.x : -1;
                if (neighbor >= 0 && bases[neighbor].teamId == teamId && bases[neighbor].isSuperProducer) {
                    priority += 35f;
                    break;
                }
            }
            return priority;
        }

        int EstimateDefenderGrowth(int targetId, float travelTime) {
            BaseState target = bases[targetId];
            if (target.teamId < 0 || target.isTurret || target.pendingTroops > 0 || HasOutgoingTroops(targetId))
                return 0;
            float productiveTime = Mathf.Max(0f, travelTime - target.productionDelay);
            float production = target.isSuperProducer
                ? DI_Rules.SuperProductionOverInterval(target.uninterruptedSeconds, productiveTime,
                    superProductionSpeed, superSpeedupSeconds)
                : productiveTime * DI_Rules.BaseProductionRate(productionSpeed, target.isFactory, factoryMultiplier);
            production *= ProductionMultiplier(target);
            return Mathf.Min(Mathf.Max(0, Capacity(target) - target.troops), Mathf.CeilToInt(production));
        }

        // A forecast is only as trustworthy as the team reading it. Confidence scales
        // how much of a projected figure a team plans around, so a poor team still
        // plans off little more than what is plainly on the board.
        static int ScaleForecast(int amount, float confidence) =>
            amount <= 0 ? 0 : Mathf.RoundToInt(amount * Mathf.Clamp01(confidence));

        /// <summary>
        /// Troops the defender can walk in from an adjacent base before a wave that is
        /// <paramref name="travelTime"/> seconds out arrives. No garrison empties itself
        /// to reinforce a neighbour, so a small reserve is always left behind.
        /// </summary>
        int EstimateDefenderReinforcements(int targetId, float travelTime) {
            BaseState target = bases[targetId];
            if (target.teamId < 0)
                return 0;
            float defenderSpeed = TeamMoveSpeed(target.teamId);
            int reinforcements = 0;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == targetId ? link.y : link.y == targetId ? link.x : -1;
                if (neighbor < 0 || bases[neighbor].teamId != target.teamId)
                    continue;
                float legTime = Vector2.Distance(bases[neighbor].position, target.position) / defenderSpeed;
                if (legTime >= travelTime)
                    continue;
                reinforcements += Mathf.Max(0, bases[neighbor].troops - NpcTroopReserve);
            }
            return reinforcements;
        }

        /// <summary>
        /// Damage a rival team's wave will already have done to this target before ours
        /// lands. Spotting it is what lets a team take a contested base cheaply instead
        /// of queueing behind someone else's fight.
        /// </summary>
        int EstimateThirdPartyDamage(int targetId, int teamId, float travelTime) {
            BaseState target = bases[targetId];
            int damage = 0;
            foreach (DotState dot in dots) {
                if (dot.teamId == teamId || dot.teamId == target.teamId)
                    continue;
                if (RouteDestination(dot.route, dot.routeIndex, dot.teamId, dot.targetBaseId) != targetId)
                    continue;
                if (EstimatedArrivalSeconds(dot) < travelTime)
                    damage++;
            }
            // Past emptying the garrison the rival takes the base itself, which is a
            // different fight; never plan on more than the defenders actually there.
            return Mathf.Min(DI_Rules.DefendersDefeatedBy(damage, DefenceOf(target)), Mathf.Max(0, target.troops));
        }

        /// <summary>
        /// Hostile troops already coming the other way down a leg of this route. They
        /// trade one for one on contact, so a team that ignores them keeps feeding a
        /// stream into a larger one.
        /// </summary>
        int EstimateInterceptionLosses(int[] route, int teamId) {
            int losses = 0;
            for (int i = 1; i < route.Length; i++) {
                foreach (DotState dot in dots) {
                    if (dot.teamId == teamId)
                        continue;
                    if (dot.sourceBaseId == route[i] && dot.targetBaseId == route[i - 1])
                        losses++;
                }
            }
            return losses;
        }

        /// <summary>Seconds until this troop reaches the end of its route.</summary>
        float EstimatedArrivalSeconds(DotState dot) {
            float speed = TeamMoveSpeed(dot.teamId);
            float seconds = (1f - dot.progress) * Vector2.Distance(
                bases[dot.sourceBaseId].position, bases[dot.targetBaseId].position) / speed;
            if (dot.route == null)
                return seconds;
            for (int i = dot.routeIndex + 1; i < dot.route.Length; i++)
                seconds += Vector2.Distance(bases[dot.route[i - 1]].position, bases[dot.route[i]].position) / speed;
            return seconds;
        }

        /// <summary>
        /// Adds further idle garrisons to the attack this team just launched, keeping only
        /// the ones whose wave lands close enough to the leading one to fight alongside it.
        /// Attacking with one base at a time is the single biggest reason a team holding a
        /// large army loses to a smaller defender that trains between each separate wave.
        /// </summary>
        void CommitSupportingWaves(int teamId, int primarySource, int[] primaryRoute, float intelligence) {
            // Timing several bases onto one target is a deliberate plan; a poor team
            // still attacks with whichever single base it happened to pick.
            if (intelligence < 0.5f)
                return;

            int targetId = primaryRoute[primaryRoute.Length - 1];
            float teamSpeed = TeamMoveSpeed(teamId);
            float leadArrival = RouteDistance(primaryRoute) / teamSpeed;
            float window = NpcCoordinationWindow * Mathf.Lerp(0.4f, 1f, intelligence);
            int committedSources = 0;

            for (int sourceId = 0; sourceId < bases.Count && committedSources < NpcMaxCoordinatedSources; sourceId++) {
                if (sourceId == primarySource)
                    continue;
                BaseState source = bases[sourceId];
                if (source.teamId != teamId || source.pendingTroops > 0 || HasOutgoingTroops(sourceId) ||
                    ShouldChargeNpcBase(source))
                    continue;

                int available = source.troops - NpcReserveForBase(sourceId, teamId);
                if (available <= 0)
                    continue;

                int[] route = FindNpcRoute(sourceId, targetId, teamId, false, intelligence);
                if (route.Length < 2)
                    continue;
                if (Mathf.Abs(RouteDistance(route) / teamSpeed - leadArrival) > window)
                    continue;
                if (EstimateTurretLosses(route, teamId, available, sendInterval) >= available)
                    continue;

                QueueSend(source, route[1], available, sendInterval);
                source.pendingRoute = route;
                committedSources++;
            }
        }

        /// <summary>
        /// Pulls the plug on a wave that can no longer take what it was sent for, so the
        /// rest of the garrison is spent somewhere it can still win instead of trickling
        /// into a base that has already outgrown it.
        /// </summary>
        bool TryAbandonHopelessAttack(int teamId, float intelligence) {
            // Reading a fight as already lost is a judgement a poor team does not make.
            if (intelligence < 0.65f)
                return false;

            bool abandoned = false;
            for (int i = 0; i < bases.Count; i++) {
                BaseState source = bases[i];
                if (source.teamId != teamId || source.pendingTroops <= 0)
                    continue;

                int targetId = RouteDestination(source.pendingRoute, 1, teamId, source.pendingTarget);
                if (targetId < 0 || targetId >= bases.Count || bases[targetId].teamId == teamId)
                    continue;

                float travelTime = (source.pendingRoute != null
                    ? RouteDistance(source.pendingRoute)
                    : Vector2.Distance(source.position, bases[targetId].position)) / TeamMoveSpeed(teamId);
                int defense = DI_Rules.AttackersToDefeat(bases[targetId].troops +
                    EstimateDefenderGrowth(targetId, travelTime) +
                    ScaleForecast(EstimateDefenderReinforcements(targetId, travelTime), intelligence * 0.5f),
                    DefenceOf(bases[targetId]));
                if (CountIncomingTroops(targetId, teamId) >= Mathf.CeilToInt(defense * NpcAbandonThreshold))
                    continue;

                ClearSend(source);
                abandoned = true;
            }
            return abandoned;
        }

        int CountIncomingTroops(int targetId, int teamId) {
            int count = 0;
            foreach (DotState dot in dots) {
                int destination = RouteDestination(dot.route, dot.routeIndex, dot.teamId, dot.targetBaseId);
                if (dot.teamId == teamId && destination == targetId)
                    count++;
            }
            foreach (BaseState source in bases) {
                int destination = RouteDestination(source.pendingRoute, 1, source.teamId, source.pendingTarget);
                if (source.teamId == teamId && source.pendingTroops > 0 && destination == targetId)
                    count += source.pendingTroops;
            }
            return count;
        }

        int CountHostileIncomingTroops(int targetId, int teamId) {
            int count = 0;
            foreach (DotState dot in dots) {
                int destination = RouteDestination(dot.route, dot.routeIndex, dot.teamId, dot.targetBaseId);
                if (dot.teamId != teamId && destination == targetId)
                    count++;
            }
            foreach (BaseState source in bases) {
                int destination = RouteDestination(source.pendingRoute, 1, source.teamId, source.pendingTarget);
                if (source.teamId >= 0 && source.teamId != teamId && source.pendingTroops > 0 && destination == targetId)
                    count += source.pendingTroops;
            }
            return count;
        }

        int RouteDestination(int[] route, int routeIndex, int teamId, int fallback) {
            if (route == null || route.Length == 0)
                return fallback;
            for (int i = routeIndex; i < route.Length; i++)
                if (bases[route[i]].teamId != teamId)
                    return route[i];
            return route[route.Length - 1];
        }

        int[] FindNpcRoute(int sourceId, int targetId, int teamId, bool friendlyTarget, float intelligence) {
            int count = bases.Count;
            var distances = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++) { distances[i] = float.PositiveInfinity; previous[i] = -1; }
            distances[sourceId] = 0f;
            for (int step = 0; step < count; step++) {
                int current = -1;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && (current < 0 || distances[i] < distances[current])) current = i;
                if (current < 0 || float.IsPositiveInfinity(distances[current])) break;
                if (current == targetId) break;
                visited[current] = true;
                foreach (Vector2Int link in links) {
                    int neighbor = link.x == current ? link.y : link.y == current ? link.x : -1;
                    if (neighbor < 0 || visited[neighbor]) continue;
                    bool canEnter = neighbor == targetId
                        ? (friendlyTarget ? bases[neighbor].teamId == teamId : bases[neighbor].teamId != teamId)
                        : bases[neighbor].teamId == teamId;
                    if (!canEnter) continue;
                    // Cost the leg in distance plus the distance spent under fire,
                    // so a smarter team detours around hostile turret coverage.
                    float exposure = TurretExposureSeconds(current, neighbor, teamId) * TeamMoveSpeed(teamId) *
                        Mathf.Lerp(0f, NpcTurretAvoidance, intelligence);
                    float distance = distances[current] + exposure +
                        Vector2.Distance(bases[current].position, bases[neighbor].position);
                    if (distance < distances[neighbor]) { distances[neighbor] = distance; previous[neighbor] = current; }
                }
            }
            if (float.IsPositiveInfinity(distances[targetId])) return Array.Empty<int>();
            var route = new List<int>();
            for (int at = targetId; at >= 0; at = previous[at]) route.Add(at);
            route.Reverse();
            return route.ToArray();
        }

        float RouteDistance(int[] route) {
            float distance = 0f;
            for (int i = 1; i < route.Length; i++)
                distance += Vector2.Distance(bases[route[i - 1]].position, bases[route[i]].position);
            return distance;
        }

        int EstimateTurretLosses(int[] route, int teamId, int troopCount, float dispatchInterval) {
            int losses = 0;
            for (int i = 1; i < route.Length && losses < troopCount; i++)
                losses += EstimateTurretLosses(route[i - 1], route[i], teamId, troopCount - losses, dispatchInterval);
            return Mathf.Min(troopCount, losses);
        }

        int EstimateTurretLosses(int sourceId, int targetId, int teamId, int troopCount, float dispatchInterval) {
            if (troopCount <= 0)
                return 0;
            int losses = 0;
            foreach (BaseState turret in bases) {
                float covered = TurretCoverageSeconds(turret, sourceId, targetId, teamId);
                if (covered <= 0f)
                    continue;
                // A stream remains exposed between its first and last troop.
                // Assume every hostile/neutral turret can concentrate on this wave.
                float exposure = covered + (troopCount - 1) * dispatchInterval;
                losses += 1 + Mathf.FloorToInt(exposure / turretFireRate);
            }
            return Mathf.Min(troopCount, losses);
        }

        /// <summary>Total seconds one troop of <paramref name="teamId"/> spends under hostile turret fire on this leg.</summary>
        float TurretExposureSeconds(int sourceId, int targetId, int teamId) {
            float seconds = 0f;
            foreach (BaseState turret in bases)
                seconds += TurretCoverageSeconds(turret, sourceId, targetId, teamId);
            return seconds;
        }

        // Seconds a single troop spends inside one hostile turret's radius while
        // crossing this leg at the team's real speed. Zero for friendly turrets.
        float TurretCoverageSeconds(BaseState turret, int sourceId, int targetId, int teamId) {
            if (!turret.isTurret || turret.teamId >= 0 && turret.teamId == teamId)
                return 0f;
            Vector2 start = bases[sourceId].position;
            Vector2 delta = bases[targetId].position - start;
            float length = delta.magnitude;
            if (length < 0.001f)
                return 0f;
            Vector2 direction = delta / length;
            Vector2 offset = turret.position - start;
            float center = Vector2.Dot(offset, direction);
            float perpendicularSquared = Mathf.Max(0f, offset.sqrMagnitude - center * center);
            float radiusSquared = turretRange * turretRange;
            if (perpendicularSquared >= radiusSquared)
                return 0f;
            float halfChord = Mathf.Sqrt(radiusSquared - perpendicularSquared);
            float enter = Mathf.Max(0f, center - halfChord);
            float leave = Mathf.Min(length, center + halfChord);
            return leave <= enter ? 0f : (leave - enter) / TeamMoveSpeed(teamId);
        }

        void TryConsolidateNpcTroops(int teamId, float intelligence) {
            int bestSource = -1, bestTarget = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < bases.Count; i++) {
                BaseState source = bases[i];
                if (source.teamId != teamId || source.pendingTroops > 0 || HasOutgoingTroops(i) ||
                    source.isSuperProducer || source.troops <= NpcTroopReserve + 2)
                    continue;
                foreach (Vector2Int link in links) {
                    int targetId = link.x == i ? link.y : link.y == i ? link.x : -1;
                    if (targetId < 0)
                        continue;
                    BaseState target = bases[targetId];
                    // Pool toward a stable front, never toward whichever garrison
                    // happens to be largest this tick (which can reverse every wave).
                    if (target.teamId != teamId || target.isTurret || target.pendingTroops > 0 ||
                        target.isSuperProducer && target.uninterruptedSeconds > 0f ||
                        CountIncomingTroops(i, teamId) > 0)
                        continue;
                    float sourceFront = DistanceToNpcFront(i, teamId);
                    float targetFront = DistanceToNpcFront(targetId, teamId);
                    if (targetFront > sourceFront || Mathf.Approximately(targetFront, sourceFront) && targetId > i)
                        continue;
                    int sending = source.troops - NpcReserveForBase(i, teamId);
                    if (sending <= 0)
                        continue;
                    int losses = EstimateTurretLosses(i, targetId, teamId, sending, sendInterval);
                    // A capable team will not pay a quarter of a garrison just to shuffle
                    // it one base closer to the front.
                    if (losses > Mathf.Max(1, Mathf.RoundToInt(sending * Mathf.Lerp(0.5f, 0.15f, intelligence))))
                        continue;
                    bool onFront = false;
                    foreach (Vector2Int targetLink in links) {
                        int enemy = targetLink.x == targetId ? targetLink.y : targetLink.y == targetId ? targetLink.x : -1;
                        if (enemy >= 0 && bases[enemy].teamId != teamId) { onFront = true; break; }
                    }
                    if (!onFront)
                        continue;
                    float score = sending + (target.isSuperProducer ? 30f : 0f) +
                        (target.isSpeedBase ? 12f : 0f) + (target.isFortress ? 10f : 0f) - losses * 4f -
                        Vector2.Distance(source.position, target.position) * 0.1f;
                    if (score > bestScore) { bestScore = score; bestSource = i; bestTarget = targetId; }
                }
            }
            if (bestSource >= 0)
                QueueSend(bases[bestSource], bestTarget,
                    bases[bestSource].troops - NpcReserveForBase(bestSource, teamId), sendInterval);
        }

        float DistanceToNpcFront(int baseId, int teamId) {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < bases.Count; i++)
                if (bases[i].teamId != teamId)
                    nearest = Mathf.Min(nearest, (bases[i].position - bases[baseId].position).sqrMagnitude);
            return nearest;
        }
    }
}
#endif
