using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    public class DI_ProductionTests {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        GameObject root;
        DI_ServerRunner runner;
        IList bases;
        IList dots;
        IList links;
        IList npcTeams;

        [SetUp]
        public void SetUp() {
            // Adding a runner invokes Unity's Reset message, which expects live
            // server services. Load the authored prefab into an isolated preview
            // scene instead; networking Awake/Start and Reset never run here.
            root = UnityEditor.PrefabUtility.LoadPrefabContents(
                "Assets/Universes/UniverseData/dot_invaders/Server/DI_ServerRunner.prefab");
            runner = root.GetComponent<DI_ServerRunner>();
            bases = (IList)Get(runner, "bases");
            dots = (IList)Get(runner, "dots");
            links = (IList)Get(runner, "links");
            npcTeams = (IList)Get(runner, "npcTeams");
        }

        [TearDown]
        public void TearDown() => UnityEditor.PrefabUtility.UnloadPrefabContents(root);

        static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        object Call(string method, params object[] args) => typeof(DI_ServerRunner).GetMethod(method, Members).Invoke(runner, args);

        object AddBase(bool super = false, int troops = 0, int team = 0, Vector2? position = null,
            bool turret = false, bool speed = false) {
            object state = Activator.CreateInstance(typeof(DI_ServerRunner).GetNestedType("BaseState", Members), true);
            Set(state, "isSuperProducer", super);
            Set(state, "isTurret", turret);
            Set(state, "isSpeedBase", speed);
            Set(state, "teamId", team);
            Set(state, "troops", troops);
            Set(state, "position", position ?? new Vector2(bases.Count * 10f, 0f));
            bases.Add(state);
            return state;
        }

        object AddDot(int origin, int source, int target, int team = 0, int[] route = null) {
            object dot = Activator.CreateInstance(typeof(DI_ServerRunner).GetNestedType("DotState", Members), true);
            Set(dot, "originBaseId", origin);
            Set(dot, "sourceBaseId", source);
            Set(dot, "targetBaseId", target);
            Set(dot, "teamId", team);
            Set(dot, "route", route);
            Set(dot, "routeIndex", 1);
            dots.Add(dot);
            return dot;
        }

        void AddLink(int source, int target) => links.Add(new Vector2Int(source, target));

        void AddNpcTeam(int team) {
            object state = Activator.CreateInstance(typeof(DI_ServerRunner).GetNestedType("NpcTeamState", Members), true);
            Set(state, "teamId", team);
            npcTeams.Add(state);
        }

        void Advance(float seconds, float step = 0.1f) {
            int frames = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < frames; i++) Call("UpdateBases", step);
        }

        [Test]
        public void SuperBaseChargesWithoutConsolidatingThenSendsOneLargeWave() {
            Set(runner, "npcIntelligence", 100f);
            object source = AddBase(super: true, troops: 60, team: 100);
            AddBase(troops: 70, team: 100);
            AddBase(troops: 1, team: -1);
            AddLink(0, 1);
            AddLink(1, 2);
            AddNpcTeam(100);
            Call("UpdateNpcTeams", 1f);
            Assert.That(Get(source, "pendingTroops"), Is.EqualTo(0));
            Set(bases[1], "pendingTroops", 0);
            Set(bases[1], "pendingRoute", null);
            Set(bases[1], "troops", 1);
            Set(source, "troops", 180);
            Call("UpdateNpcTeams", 1f);
            Assert.That((int)Get(source, "pendingTroops"), Is.GreaterThan(140));
        }

        [Test]
        public void LowCapacitySuperBaseCanStillDispatchAndDoesNotDeadlockOnReserve() {
            Set(runner, "npcIntelligence", 100f);
            Set(runner, "npcAggression", 100f);
            Set(runner, "superMaxCapacity", 5);
            object source = AddBase(super: true, troops: 5, team: 100);
            AddBase(troops: 0, team: -1);
            AddLink(0, 1);
            AddNpcTeam(100);
            Call("UpdateNpcTeams", 1f);
            Assert.That(Get(source, "pendingTroops"), Is.EqualTo(1));
        }

        [Test]
        public void ThreatenedNpcCancelsItsDepartureBeforeItEmptiesItsGarrison() {
            Set(runner, "npcIntelligence", 100f);
            object source = AddBase(troops: 30, team: 100);
            object enemy = AddBase(troops: 20, team: 1);
            AddLink(0, 1);
            Call("QueueSend", source, 1, 28, 0.4f);
            Call("QueueSend", enemy, 0, 10, 0.4f);
            AddNpcTeam(100);
            Call("UpdateNpcTeams", 1f);
            Assert.That(Get(source, "pendingTroops"), Is.Zero);
            Assert.That(Get(source, "troops"), Is.EqualTo(30));
        }

        [Test]
        public void NpcDoesNotSendAnotherWaveToAnAlreadyCoveredTarget() {
            Set(runner, "npcIntelligence", 100f);
            object source = AddBase(troops: 40, team: 100);
            object donor = AddBase(troops: 20, team: 100);
            AddBase(troops: 1, team: -1);
            AddLink(0, 2);
            AddLink(1, 2);
            Set(donor, "pendingTarget", 2);
            Set(donor, "pendingTroops", 15);
            AddNpcTeam(100);
            Call("UpdateNpcTeams", 1f);
            Assert.That(Get(source, "pendingTroops"), Is.EqualTo(0));
        }

        [Test]
        public void RoutesThreatenTheFirstHostileWaypointBeforeTheirFinalDestination() {
            AddBase(team: 1);
            AddBase(team: 100);
            AddBase(team: 100);
            AddDot(0, 0, 1, team: 1, route: new[] { 0, 1, 2 });
            Assert.That(Call("CountHostileIncomingTroops", 1, 100), Is.EqualTo(1));
            Assert.That(Call("CountHostileIncomingTroops", 2, 100), Is.EqualTo(0));
        }

        [Test]
        public void MovementCarriesTimeThroughMultipleWaypointsAtAnyFrameSize() {
            AddBase(position: Vector2.zero);
            AddBase(position: Vector2.right);
            AddBase(position: Vector2.right * 2f);
            AddBase(position: Vector2.right * 10f);
            object dot = AddDot(0, 0, 1, route: new[] { 0, 1, 2, 3 });
            Call("UpdateDots", 0.5f);
            Assert.That(((Vector2)Call("GetDotPosition", dot)).x, Is.EqualTo(6f).Within(0.001f));
            dots.Clear();
            dot = AddDot(0, 0, 1, route: new[] { 0, 1, 2, 3 });
            for (int i = 0; i < 50; i++) Call("UpdateDots", 0.01f);
            Assert.That(((Vector2)Call("GetDotPosition", dot)).x, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void FastOpposingTroopsCannotTunnelThroughEachOther() {
            Set(runner, "moveSpeedMultiplier", 500f / DI_Rules.DefaultMoveSpeed);
            AddBase(position: Vector2.zero);
            AddBase(team: 1, position: new Vector2(10f, 0f));
            AddDot(0, 0, 1);
            AddDot(1, 1, 0, team: 1);
            Call("UpdateDots", 0.02f);
            Assert.That(dots.Count, Is.Zero);
        }

        [Test]
        public void FastTroopCannotSkipATurretBetweenSnapshots() {
            Set(runner, "moveSpeedMultiplier", 10000f / DI_Rules.DefaultMoveSpeed);
            Set(runner, "turretRange", 5f);
            AddBase(position: Vector2.zero);
            AddBase(position: new Vector2(100f, 0f));
            AddBase(team: -1, turret: true, position: new Vector2(50f, 0f));
            AddDot(0, 0, 1);
            Call("UpdateDots", 0.02f);
            Assert.That(dots.Count, Is.Zero);
            Assert.That(Get(bases[1], "troops"), Is.Zero);
        }

        [Test]
        public void CapturedWaypointStopsAnExistingRouteWithoutTeleportingOrSkippingCombat() {
            AddBase(position: Vector2.zero);
            object target = AddBase(team: 1, troops: 4, position: Vector2.right);
            AddBase(position: Vector2.right * 10f);
            AddDot(0, 0, 1, route: new[] { 0, 1, 2 });
            Call("UpdateDots", 1f);
            Assert.That(dots.Count, Is.Zero);
            Assert.That(Get(target, "troops"), Is.EqualTo(3));
            Assert.That(Get(bases[2], "troops"), Is.EqualTo(0));
        }

        [Test]
        public void NeutralBasesDoNotPreventVictoryButHostileMovingTroopsDo() {
            AddBase(team: -1);
            AddBase(team: 100);
            Assert.That(Call("FindSoleSurvivingTeam"), Is.EqualTo(100));
            AddDot(0, 0, 1, team: 101);
            Assert.That(Call("FindSoleSurvivingTeam"), Is.EqualTo(-2));
            dots.Clear();
            Set(bases[1], "teamId", -1);
            Assert.That(Call("FindSoleSurvivingTeam"), Is.EqualTo(-1));
        }

        [Test]
        public void NpcTeamsContinueCompetingAfterHumansAreEliminated() {
            AddBase(team: 100);
            AddBase(team: 101);
            Assert.That(Call("FindSoleSurvivingTeam"), Is.EqualTo(-2));
        }

        void EnablePlayer(int clientId) {
            Set(runner, "matchInProgress", true);
            ((System.Collections.Generic.Dictionary<int, int>)Get(runner, "playerTeams"))[clientId] = 0;
        }

        [Test]
        public void DamageBuffAccumulatesFractionsAndResetRestoresNormalCombat() {
            EnablePlayer(7);
            AddBase();
            object target = AddBase(troops: 10, team: 1);
            object dot = AddDot(0, 0, 1);
            Set(dot, "ownerClientId", 7);
            Assert.That(Call("SetPlayerMultiplier", 7, 1.5f, true), Is.True);
            Call("ResolveArrival", dot);
            Call("ResolveArrival", dot);
            Assert.That(Get(target, "troops"), Is.EqualTo(7));
            Call("SetPlayerMultiplier", 7, 1f, true);
            Call("ResolveArrival", dot);
            Assert.That(Get(target, "troops"), Is.EqualTo(6));
        }

        [Test]
        public void BuffedTroopSurvivesAnEqualUnbuffedTroopAndRetainsCombatDamage() {
            EnablePlayer(7);
            Call("SetPlayerMultiplier", 7, 2f, true);
            AddBase();
            AddBase(team: 1);
            object buffed = AddDot(0, 0, 1);
            Set(buffed, "ownerClientId", 7);
            Set(buffed, "progress", 0.5f);
            object other = AddDot(1, 1, 0, team: 1);
            Set(other, "progress", 0.5f);
            Call("UpdateDots", 0.01f);
            Assert.That(dots.Count, Is.EqualTo(1));
            Assert.That(dots[0], Is.SameAs(buffed));
            Assert.That((float)Get(buffed, "health"), Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void ProductionBuffIsPrivatePerOwnerAndRejectsNonFiniteValues() {
            EnablePlayer(7);
            object buffed = AddBase();
            Set(buffed, "ownerClientId", 7);
            object normal = AddBase();
            Assert.That(Call("SetPlayerMultiplier", 7, 2f, false), Is.True);
            Assert.That(Call("SetPlayerMultiplier", 7, float.NaN, false), Is.False);
            Assert.That(Call("SetPlayerMultiplier", 99, 2f, false), Is.False);
            Advance(4f);
            Assert.That((int)Get(buffed, "troops"), Is.EqualTo(10).Within(1));
            Assert.That((int)Get(normal, "troops"), Is.EqualTo(5).Within(1));
            var snapshot = (DI_StateBroadcast)Call("CreateState");
            Assert.That(snapshot.baseProductionRates[0], Is.EqualTo(snapshot.baseProductionRates[1]));
        }

        [Test]
        public void SuperBasesAccelerateAndStopAtTwoHundredWhileNormalBasesStopAtFifty() {
            object super = AddBase(true);
            object normal = AddBase();
            Advance(5f);
            int early = (int)Get(super, "troops");
            Advance(5f);
            Assert.That((int)Get(super, "troops") - early, Is.GreaterThan(early));
            Advance(100f);
            Assert.That(Get(super, "troops"), Is.EqualTo(200));
            Assert.That(Get(normal, "troops"), Is.EqualTo(50));
        }

        [Test]
        public void FriendlyWaypointKeepsChargeAndProductionWhileOriginalSenderStaysBlocked() {
            object sender = AddBase(true);
            object waypoint = AddBase(true);
            AddBase();
            Set(waypoint, "uninterruptedSeconds", 10f);
            object dot = AddDot(0, 0, 1, route: new[] { 0, 1, 2 });
            Assert.That(Call("ContinueRoute", dot), Is.True);
            Advance(1f);
            Assert.That(Get(sender, "troops"), Is.EqualTo(0));
            Assert.That((int)Get(waypoint, "troops"), Is.GreaterThan(0));
            Assert.That((float)Get(waypoint, "uninterruptedSeconds"), Is.GreaterThan(10f));
        }

        [Test]
        public void ReinforcementResetsChargeAndCanExceedProductionCap() {
            AddBase();
            object receiver = AddBase(true, 200);
            Set(receiver, "uninterruptedSeconds", 20f);
            object dot = AddDot(0, 0, 1);
            Call("ResolveArrival", dot);
            Assert.That(Get(receiver, "troops"), Is.EqualTo(201));
            Assert.That(Get(receiver, "uninterruptedSeconds"), Is.EqualTo(0f));
            Assert.That(Get(receiver, "productionDelay"), Is.EqualTo(2f));
        }

        [Test]
        public void SendingAndHostileCaptureResetCharge() {
            object sender = AddBase(true, 20);
            object target = AddBase(true, 0, 1);
            Set(sender, "uninterruptedSeconds", 20f);
            Call("QueueSend", sender, 1, 10, 0.4f);
            Assert.That(Get(sender, "uninterruptedSeconds"), Is.EqualTo(0f));
            Set(target, "uninterruptedSeconds", 20f);
            Call("ResolveArrival", AddDot(0, 0, 1));
            Assert.That(Get(target, "teamId"), Is.EqualTo(0));
            Assert.That(Get(target, "uninterruptedSeconds"), Is.EqualTo(0f));
        }

        [Test]
        public void AlliedArrivalsDoNotResetAnActiveDispatchTimer() {
            object receiver = AddBase(true, 10);
            AddBase();
            Call("QueueSend", receiver, 1, 5, 0.4f);
            Set(receiver, "actionTimer", 0.3f);
            Call("ResolveArrival", AddDot(1, 1, 0));
            Assert.That(Get(receiver, "actionTimer"), Is.EqualTo(0.3f));
        }

        [Test]
        public void ProductionIsConsistentAcrossFrameSizesAndConfiguredCapacity() {
            Set(runner, "superMaxCapacity", 1000);
            object state = AddBase(true);
            Advance(30f, 0.1f);
            int fine = (int)Get(state, "troops");
            bases.Clear();
            state = AddBase(true);
            Advance(30f, 0.25f);
            Assert.That((int)Get(state, "troops"), Is.EqualTo(fine).Within(1));
            Assert.That(fine, Is.GreaterThan(200));
        }

        [Test]
        public void NpcEstimatesChargingSuperBaseGrowthInsteadOfPeakProduction() {
            AddBase();
            object defender = AddBase(super: true, troops: 10, team: 1);
            int charging = (int)Call("EstimateDefenderGrowth", 1, 5f);
            Set(defender, "uninterruptedSeconds", 20f);
            int charged = (int)Call("EstimateDefenderGrowth", 1, 5f);
            Assert.That(charging, Is.GreaterThan(0));
            Assert.That(charging, Is.LessThan(charged));
            Assert.That(charged, Is.EqualTo(63));
        }

        [Test]
        public void NpcCountsHostileWavesByFinalDestination() {
            AddBase(team: 1);
            AddBase(team: 1);
            AddBase(team: 100);
            AddDot(1, 1, 0, team: 1, route: new[] { 1, 0, 2 });
            object enemy = bases[1];
            Set(enemy, "pendingTarget", 0);
            Set(enemy, "pendingTroops", 4);
            Set(enemy, "pendingRoute", new[] { 1, 0, 2 });
            Assert.That(Call("CountHostileIncomingTroops", 2, 100), Is.EqualTo(5));
        }

        [Test]
        public void NpcRoutesThroughFriendlyBasesToCaptureAHighValueSuperBase() {
            Set(runner, "npcIntelligence", 100f);
            object source = AddBase(troops: 40, team: 100, position: Vector2.zero);
            AddBase(troops: 8, team: 100, position: new Vector2(10f, 0f));
            AddBase(super: true, troops: 2, team: -1, position: new Vector2(20f, 0f));
            AddBase(troops: 2, team: -1, position: new Vector2(0f, 18f));
            AddLink(0, 1);
            AddLink(1, 2);
            AddLink(0, 3);
            AddNpcTeam(100);

            Call("UpdateNpcTeams", 1f);

            Assert.That(Get(source, "pendingTarget"), Is.EqualTo(1));
            Assert.That((int[])Get(source, "pendingRoute"), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That((int)Get(source, "pendingTroops"), Is.GreaterThan(0));
        }

        [Test]
        public void NpcReinforcesAThreatenedSuperBaseBeforeAttacking() {
            Set(runner, "npcIntelligence", 100f);
            object threatened = AddBase(super: true, troops: 3, team: 100, position: Vector2.zero);
            object donor = AddBase(troops: 30, team: 100, position: new Vector2(10f, 0f));
            object enemy = AddBase(troops: 20, team: 1, position: new Vector2(-10f, 0f));
            Set(enemy, "pendingTarget", 0);
            Set(enemy, "pendingTroops", 15);
            AddLink(0, 1);
            AddLink(0, 2);
            AddNpcTeam(100);

            Call("UpdateNpcTeams", 1f);

            Assert.That(Get(donor, "pendingTarget"), Is.EqualTo(0));
            Assert.That((int)Get(donor, "pendingTroops"), Is.GreaterThan(0));
            Assert.That(Get(threatened, "pendingTroops"), Is.EqualTo(0));
        }

        [Test]
        public void TeamMoveSpeedFollowsCapturedSpeedBases() {
            AddBase(team: 100, speed: true);
            AddBase(team: 100, speed: true);
            AddBase(team: 1, speed: true);
            Call("RefreshTeamSpeedBases");

            Assert.That((float)Call("TeamMoveSpeed", 100), Is.EqualTo(DI_Rules.DefaultMoveSpeed * 1.5f).Within(0.001f));
            Assert.That((float)Call("TeamMoveSpeed", 1), Is.EqualTo(DI_Rules.DefaultMoveSpeed * 1.25f).Within(0.001f));
            Assert.That((float)Call("TeamMoveSpeed", -1), Is.EqualTo(DI_Rules.DefaultMoveSpeed).Within(0.001f));
        }

        [Test]
        public void TurretExposureShrinksWhenTheAttackingTeamOwnsSpeedBases() {
            AddBase(position: Vector2.zero, team: 100);
            AddBase(position: new Vector2(40f, 0f), team: 100);
            AddBase(position: new Vector2(20f, 0f), team: 1, turret: true);
            float slow = (float)Call("TurretExposureSeconds", 0, 1, 100);

            AddBase(position: new Vector2(0f, 40f), team: 100, speed: true);
            Call("RefreshTeamSpeedBases");
            float fast = (float)Call("TurretExposureSeconds", 0, 1, 100);

            Assert.That(slow, Is.GreaterThan(0f));
            Assert.That(fast, Is.EqualTo(slow / 1.25f).Within(0.001f));
        }

        [Test]
        public void SmartNpcsRouteAroundAHostileTurretThatADumbOneWalksInto() {
            // 0 -> 1 -> 2 is the short line, but a hostile turret sits beside base 1.
            // 0 -> 3 -> 2 is 40 units longer and stays clear of the turret entirely.
            Set(runner, "turretRange", 12f);
            AddBase(position: Vector2.zero, team: 100);
            AddBase(position: new Vector2(30f, 0f), team: 100);
            AddBase(position: new Vector2(60f, 0f), team: -1);
            AddBase(position: new Vector2(30f, 40f), team: 100);
            AddBase(position: new Vector2(30f, 3f), team: 1, turret: true);
            AddLink(0, 1);
            AddLink(1, 2);
            AddLink(0, 3);
            AddLink(3, 2);

            Assert.That((int[])Call("FindNpcRoute", 0, 2, 100, false, 0f), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That((int[])Call("FindNpcRoute", 0, 2, 100, false, 1f), Is.EqualTo(new[] { 0, 3, 2 }));
        }

        [Test]
        public void NpcsValueAnUnclaimedSpeedBaseAndValueTheSecondOneLess() {
            AddBase(position: Vector2.zero, team: 100);
            AddBase(position: new Vector2(20f, 0f), team: -1, speed: true);
            AddBase(position: new Vector2(40f, 0f), team: -1);
            Call("RefreshTeamSpeedBases");
            float plain = (float)Call("NpcTargetPriority", 2, 100);
            float firstSpeedBase = (float)Call("NpcTargetPriority", 1, 100);

            AddBase(position: new Vector2(0f, 20f), team: 100, speed: true);
            Call("RefreshTeamSpeedBases");
            float secondSpeedBase = (float)Call("NpcTargetPriority", 1, 100);

            Assert.That(firstSpeedBase, Is.GreaterThan(plain));
            Assert.That(secondSpeedBase, Is.LessThan(firstSpeedBase));
            Assert.That(secondSpeedBase, Is.GreaterThan(plain));
        }

        [Test]
        public void AggressiveNpcTeamsKeepSmallerGarrisonsThanCautiousOnes() {
            AddBase(team: 100, troops: 40);
            AddBase(team: 100, troops: 40);
            AddLink(0, 1);

            Set(runner, "npcAggression", 0f);
            int cautious = (int)Call("NpcReserveForBase", 0, 100);
            Set(runner, "npcAggression", 100f);
            int aggressive = (int)Call("NpcReserveForBase", 0, 100);

            Assert.That(aggressive, Is.LessThan(cautious));
        }

        [Test]
        public void ProductionSpeedCommandChangesHowFastNormalBasesTrain() {
            object state = AddBase();
            Advance(10f);
            int standard = (int)Get(state, "troops");

            bases.Clear();
            Set(runner, "productionSpeed", DI_Rules.DefaultProductionSpeed * 2f);
            state = AddBase();
            Advance(10f);

            Assert.That(standard, Is.EqualTo(12));
            Assert.That((int)Get(state, "troops"), Is.EqualTo(25));
        }

        [Test]
        public void SmarterNpcTeamsThinkMoreQuicklyThanDullOnes() {
            object source = AddBase(troops: 40, team: 100);
            AddBase(troops: 1, team: -1);
            AddLink(0, 1);
            AddNpcTeam(100);

            Set(runner, "npcIntelligence", 0f);
            Call("UpdateNpcTeams", 1f);
            Assert.That(Get(source, "pendingTroops"), Is.Zero,
                "a dull team has not finished thinking one second in");

            Set(runner, "npcIntelligence", 100f);
            Set(npcTeams[0], "thinkTimer", 0f);
            Call("UpdateNpcTeams", 1f);
            Assert.That((int)Get(source, "pendingTroops"), Is.GreaterThan(0));
        }

        [Test]
        public void NpcBasesDispatchAtTheConfiguredPlayerSendInterval() {
            Set(runner, "npcIntelligence", 100f);
            Set(runner, "sendIntervalMultiplier", 0.9f / DI_Rules.DefaultSendInterval);
            object source = AddBase(troops: 40, team: 100);
            AddBase(troops: 1, team: -1);
            AddLink(0, 1);
            AddNpcTeam(100);

            Call("UpdateNpcTeams", 1f);

            Assert.That((int)Get(source, "pendingTroops"), Is.GreaterThan(0));
            Assert.That((float)Get(source, "sendInterval"), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void NpcReinforcementWavesAlsoUseThePlayerSendInterval() {
            Set(runner, "npcIntelligence", 100f);
            Set(runner, "sendIntervalMultiplier", 0.9f / DI_Rules.DefaultSendInterval);
            AddBase(super: true, troops: 3, team: 100, position: Vector2.zero);
            object donor = AddBase(troops: 30, team: 100, position: new Vector2(10f, 0f));
            object enemy = AddBase(troops: 20, team: 1, position: new Vector2(-10f, 0f));
            Set(enemy, "pendingTarget", 0);
            Set(enemy, "pendingTroops", 15);
            AddLink(0, 1);
            AddLink(0, 2);
            AddNpcTeam(100);

            Call("UpdateNpcTeams", 1f);

            Assert.That((int)Get(donor, "pendingTroops"), Is.GreaterThan(0));
            Assert.That((float)Get(donor, "sendInterval"), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void NpcForecastsReinforcementsTheDefenderCanWalkInBeforeTheWaveLands() {
            // Base 2 is six units from the target it defends, so it can always answer
            // a wave that is still ten seconds out and never one already arriving.
            AddBase(position: Vector2.zero, team: 100);
            AddBase(position: new Vector2(30f, 0f), team: 1, troops: 5);
            AddBase(position: new Vector2(36f, 0f), team: 1, troops: 20);
            AddLink(1, 2);

            Assert.That((int)Call("EstimateDefenderReinforcements", 1, 10f), Is.EqualTo(17));
            Assert.That((int)Call("EstimateDefenderReinforcements", 1, 0.1f), Is.Zero);
        }

        [Test]
        public void NpcCountsARivalWaveThatLandsOnItsTargetFirst() {
            AddBase(position: Vector2.zero, team: 100);
            object target = AddBase(position: new Vector2(30f, 0f), team: 1, troops: 4);
            AddBase(position: new Vector2(30f, 30f), team: 2);
            // Two troops of a third team are halfway down a thirty unit leg, so 1.25s out.
            Set(AddDot(2, 2, 1, team: 2), "progress", 0.5f);
            Set(AddDot(2, 2, 1, team: 2), "progress", 0.5f);

            Assert.That((int)Call("EstimateThirdPartyDamage", 1, 100, 5f), Is.EqualTo(2));
            Assert.That((int)Call("EstimateThirdPartyDamage", 1, 100, 1f), Is.Zero,
                "a wave about to land gains nothing from a fight that has not happened yet");

            Set(target, "troops", 1);
            Assert.That((int)Call("EstimateThirdPartyDamage", 1, 100, 5f), Is.EqualTo(1),
                "never plan on more than the defenders actually present");
        }

        [Test]
        public void NpcCountsAHostileStreamComingTheOtherWayDownItsRoute() {
            AddBase(position: Vector2.zero, team: 100);
            AddBase(position: new Vector2(30f, 0f), team: 1);
            AddLink(0, 1);
            AddDot(1, 1, 0, team: 1);
            AddDot(1, 1, 0, team: 1);
            AddDot(0, 0, 1, team: 100);

            Assert.That((int)Call("EstimateInterceptionLosses", new[] { 0, 1 }, 100), Is.EqualTo(2));
            Assert.That((int)Call("EstimateInterceptionLosses", new[] { 1, 0 }, 1), Is.EqualTo(1));
        }

        [Test]
        public void SmartNpcTimesASecondBaseOntoTheSameTarget() {
            // Both garrisons sit thirty units from the target, so their waves land together.
            Set(runner, "npcIntelligence", 100f);
            object left = AddBase(position: new Vector2(-30f, 0f), team: 100, troops: 40);
            object right = AddBase(position: new Vector2(30f, 0f), team: 100, troops: 40);
            AddBase(position: Vector2.zero, team: 1, troops: 6);
            AddLink(0, 2);
            AddLink(1, 2);
            AddNpcTeam(100);

            Call("UpdateNpcTeams", 1f);

            Assert.That((int)Get(left, "pendingTroops"), Is.GreaterThan(0));
            Assert.That((int)Get(right, "pendingTroops"), Is.GreaterThan(0));
            Assert.That(Get(left, "pendingTarget"), Is.EqualTo(2));
            Assert.That(Get(right, "pendingTarget"), Is.EqualTo(2));
        }

        [Test]
        public void DullNpcStillAttacksWithOneBaseAtATime() {
            Set(runner, "npcIntelligence", 40f);
            object left = AddBase(position: new Vector2(-30f, 0f), team: 100, troops: 40);
            object right = AddBase(position: new Vector2(30f, 0f), team: 100, troops: 40);
            AddBase(position: Vector2.zero, team: 1, troops: 6);
            AddLink(0, 2);
            AddLink(1, 2);
            AddNpcTeam(100);

            Call("UpdateNpcTeams", 3f);

            int attacking = ((int)Get(left, "pendingTroops") > 0 ? 1 : 0) +
                ((int)Get(right, "pendingTroops") > 0 ? 1 : 0);
            Assert.That(attacking, Is.EqualTo(1));
        }

        [Test]
        public void NpcAbandonsAWaveThatCanNoLongerTakeTheBaseItWasSentFor() {
            object source = AddBase(position: Vector2.zero, team: 100, troops: 30);
            AddBase(position: new Vector2(30f, 0f), team: 1, troops: 60);
            AddLink(0, 1);
            Call("QueueSend", source, 1, 10, 0.4f);
            Set(source, "pendingRoute", new[] { 0, 1 });

            Assert.That((bool)Call("TryAbandonHopelessAttack", 100, 1f), Is.True);
            Assert.That(Get(source, "pendingTroops"), Is.Zero);
            Assert.That(Get(source, "troops"), Is.EqualTo(30));

            // A wave that can still win is left alone.
            Set(bases[1], "troops", 8);
            Call("QueueSend", source, 1, 20, 0.4f);
            Set(source, "pendingRoute", new[] { 0, 1 });

            Assert.That((bool)Call("TryAbandonHopelessAttack", 100, 1f), Is.False);
            Assert.That(Get(source, "pendingTroops"), Is.EqualTo(20));
        }

        [Test]
        public void NpcHoldsMoreAtABaseBesideALargeEnemyStack() {
            Set(runner, "npcIntelligence", 100f);
            AddBase(team: 100, troops: 40);
            object neighbor = AddBase(team: 1, troops: 2);
            AddLink(0, 1);
            int quiet = (int)Call("NpcReserveForBase", 0, 100);

            Set(neighbor, "troops", 30);
            int threatened = (int)Call("NpcReserveForBase", 0, 100);

            Assert.That(threatened, Is.GreaterThan(quiet));

            // A team that cannot read the board garrisons the same either way.
            Set(runner, "npcIntelligence", 0f);
            int dullAgainstStack = (int)Call("NpcReserveForBase", 0, 100);
            Set(neighbor, "troops", 2);

            Assert.That((int)Call("NpcReserveForBase", 0, 100), Is.EqualTo(dullAgainstStack));
            Assert.That(dullAgainstStack, Is.LessThan(threatened));
        }
    }
}
