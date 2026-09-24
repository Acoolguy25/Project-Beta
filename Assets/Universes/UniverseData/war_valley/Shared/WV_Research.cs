using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    public enum WV_ResearchPhase : byte {
        None = 0,
        Researching = 1,
        Complete = 2
    }

    /// <summary>
    /// The replicated state of one side's project on one technology.
    /// <para>
    /// Progress is sent as a sample and a rate rather than as a value streamed every frame: every
    /// client extrapolates <see cref="progress"/> from <see cref="sampleTime"/> at
    /// <see cref="progressPerSecond"/>, and the server only re-sends when the rate changes - a
    /// project starting or finishing, a station coming up or going down. The field order is part of
    /// the replicated state, so fields may be appended but not reordered.
    /// </para>
    /// </summary>
    public struct WV_ResearchProject {
        /// <summary>A <see cref="WV_ResearchPhase"/> value.</summary>
        public byte phase;
        /// <summary>0 to 1 progress at <see cref="sampleTime"/>.</summary>
        public float progress;
        public float progressPerSecond;
        /// <summary><see cref="NetworkHelper.ServerTime"/> at which <see cref="progress"/> was measured.</summary>
        public float sampleTime;
        /// <summary>The commander who paid for the project, and the only one who may cancel it.</summary>
        public int starterClientId;
        /// <summary>What the starter paid, refunded in full on a cancel.</summary>
        public int paid;
    }

    /// <summary>
    /// War Valley's research ledger: which technologies each side has, and how far along the ones
    /// in progress are.
    /// <para>
    /// Research belongs to a side (a real team), not to one commander. Allies share every unlock and
    /// every research station, which is what makes an ally's lab worth protecting; each project is
    /// still paid for, and can only be cancelled, by the commander who started it. It lives on the
    /// match economy's networked object, beside the funds ledger, and is rebuilt with it every round.
    /// </para>
    /// </summary>
    public sealed class WV_Research : NetworkBehaviour {
        public static WV_Research Instance { get; private set; }

        /// <summary>Raised on the client whenever any side's research changes, so menus and locks can refresh.</summary>
        public static event Action Changed;

        readonly SyncDictionary<int, WV_ResearchProject> projects = new();
        /// <summary>Debug: every technology counts as researched for every side.</summary>
        readonly SyncVar<bool> everythingResearched = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            Instance = null;
            Changed = null;
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            Instance = this;
            projects.OnChange += HandleProjectsChanged;
            everythingResearched.OnChange += HandleEverythingResearchedChanged;
            Changed?.Invoke();
        }

        public override void OnStopNetwork() {
            projects.OnChange -= HandleProjectsChanged;
            everythingResearched.OnChange -= HandleEverythingResearchedChanged;
            if (Instance == this)
                Instance = null;
            Changed?.Invoke();
            base.OnStopNetwork();
        }

        void HandleProjectsChanged(SyncDictionaryOperation op, int key, WV_ResearchProject value, bool asServer) {
            if (!asServer)
                Changed?.Invoke();
        }

        void HandleEverythingResearchedChanged(bool previous, bool next, bool asServer) {
            if (!asServer)
                Changed?.Invoke();
        }

        static int Key(TeamColor side, WV_Tech tech) => ((int)side << 8) | (byte)tech;

        static TeamColor SideOf(int key) => (TeamColor)(key >> 8);

        static WV_Tech TechOf(int key) => (WV_Tech)(key & 0xFF);

        public WV_ResearchPhase GetPhase(TeamColor side, WV_Tech tech) {
            if (tech == WV_Tech.None || everythingResearched.Value)
                return WV_ResearchPhase.Complete;
            return projects.TryGetValue(Key(side, tech), out WV_ResearchProject project)
                ? (WV_ResearchPhase)project.phase
                : WV_ResearchPhase.None;
        }

        public bool IsResearched(TeamColor side, WV_Tech tech) => GetPhase(side, tech) == WV_ResearchPhase.Complete;

        /// <summary>0 to 1 progress, extrapolated to the current server time.</summary>
        public float GetProgress(TeamColor side, WV_Tech tech) {
            WV_ResearchPhase phase = GetPhase(side, tech);
            if (phase == WV_ResearchPhase.Complete)
                return 1f;
            if (phase != WV_ResearchPhase.Researching || !projects.TryGetValue(Key(side, tech), out WV_ResearchProject project))
                return 0f;
            return Extrapolate(project, NetworkHelper.ServerTime);
        }

        /// <summary>
        /// Seconds until a project in progress finishes at its current rate, or a negative value when
        /// it is stalled - every station on the side destroyed or still under construction.
        /// </summary>
        public float GetSecondsRemaining(TeamColor side, WV_Tech tech) {
            if (!projects.TryGetValue(Key(side, tech), out WV_ResearchProject project)
                || project.phase != (byte)WV_ResearchPhase.Researching)
                return 0f;
            if (project.progressPerSecond <= 0f)
                return -1f;
            return (1f - Extrapolate(project, NetworkHelper.ServerTime)) / project.progressPerSecond;
        }

        /// <summary>The commander who started a project, or <see cref="WV_Owned.NoOwner"/>.</summary>
        public int GetStarter(TeamColor side, WV_Tech tech) =>
            projects.TryGetValue(Key(side, tech), out WV_ResearchProject project)
                ? project.starterClientId
                : WV_Owned.NoOwner;

        /// <summary>How many projects <paramref name="side"/> is running at once.</summary>
        public int CountResearching(TeamColor side) {
            int count = 0;
            foreach (KeyValuePair<int, WV_ResearchProject> entry in projects) {
                if (SideOf(entry.Key) == side && entry.Value.phase == (byte)WV_ResearchPhase.Researching)
                    count++;
            }
            return count;
        }

        public bool ArePrerequisitesMet(TeamColor side, WV_TechDefinition definition) {
            if (definition == null)
                return false;
            foreach (WV_Tech prerequisite in definition.Prerequisites) {
                if (!IsResearched(side, prerequisite))
                    return false;
            }
            return true;
        }

        static float Extrapolate(WV_ResearchProject project, float now) =>
            Mathf.Clamp01(project.progress + project.progressPerSecond * Mathf.Max(0f, now - project.sampleTime));

#if UNITY_SERVER
        /// <summary>Debug: a project completes the moment it is started. Set once by the runner.</summary>
        public static bool InstantResearch { get; set; }

        /// <summary>Debug: every technology begins the round already researched. Set once by the runner.</summary>
        public static bool StartFullyResearched { get; set; }

        /// <summary>
        /// How often station counts are re-checked. Stations finish building and get destroyed at
        /// arbitrary moments, and a rate change is cheap to detect but worth replicating only when
        /// it actually happens.
        /// </summary>
        const float RateCheckInterval = 0.5f;

        readonly Dictionary<TeamColor, float> sideRates = new();
        readonly List<int> keyScratch = new();
        readonly HashSet<TeamColor> sideScratch = new();
        float nextRateCheck;

        public override void OnStartServer() {
            base.OnStartServer();
            everythingResearched.Value = StartFullyResearched;
        }

        /// <summary>Why <paramref name="side"/> may not start <paramref name="tech"/> right now, or None.</summary>
        [Server]
        public WV_ResearchRefusal GetStartRefusal(TeamColor side, WV_Tech tech) {
            WV_TechDefinition definition = WV_TechTree.Get(tech);
            if (definition == null)
                return WV_ResearchRefusal.Unavailable;

            switch (GetPhase(side, tech)) {
                case WV_ResearchPhase.Complete:
                    return WV_ResearchRefusal.AlreadyResearched;
                case WV_ResearchPhase.Researching:
                    return WV_ResearchRefusal.AlreadyResearching;
            }

            if (!ArePrerequisitesMet(side, definition))
                return WV_ResearchRefusal.MissingPrerequisite;
            if (WV_ResearchBuilding.GetResearchRate(side) <= 0f)
                return WV_ResearchRefusal.NoResearchStation;
            return WV_ResearchRefusal.None;
        }

        /// <summary>Starts a project the caller has already validated and charged for.</summary>
        [Server]
        public void Begin(TeamColor side, WV_Tech tech, int starterClientId, int paid) {
            float now = NetworkHelper.ServerTime;
            projects[Key(side, tech)] = new WV_ResearchProject {
                phase = (byte)(InstantResearch ? WV_ResearchPhase.Complete : WV_ResearchPhase.Researching),
                progress = InstantResearch ? 1f : 0f,
                progressPerSecond = 0f,
                sampleTime = now,
                starterClientId = starterClientId,
                paid = paid
            };

            // Every other project on the side slows down to share the stations with this one.
            if (!InstantResearch)
                Rebalance(side, now);
        }

        /// <summary>
        /// Cancels a project in progress. Only the commander who paid for it may cancel it; they get
        /// back exactly what they paid, and the side's other projects speed back up.
        /// </summary>
        [Server]
        public WV_ResearchRefusal TryCancel(TeamColor side, WV_Tech tech, int clientId, out int refund) {
            refund = 0;
            int key = Key(side, tech);
            if (!projects.TryGetValue(key, out WV_ResearchProject project)
                || project.phase != (byte)WV_ResearchPhase.Researching)
                return WV_ResearchRefusal.Unavailable;
            if (project.starterClientId != clientId)
                return WV_ResearchRefusal.NotPermitted;

            refund = project.paid;
            projects.Remove(key);
            Rebalance(side, NetworkHelper.ServerTime);
            return WV_ResearchRefusal.None;
        }

        void Update() {
            if (!IsServerStarted || projects.Count == 0)
                return;

            float now = NetworkHelper.ServerTime;
            CompleteFinishedProjects(now);

            if (now < nextRateCheck)
                return;
            nextRateCheck = now + RateCheckInterval;
            RebalanceSidesWhoseStationsChanged(now);
        }

        void CompleteFinishedProjects(float now) {
            keyScratch.Clear();
            foreach (KeyValuePair<int, WV_ResearchProject> entry in projects) {
                if (entry.Value.phase == (byte)WV_ResearchPhase.Researching && Extrapolate(entry.Value, now) >= 1f)
                    keyScratch.Add(entry.Key);
            }
            if (keyScratch.Count == 0)
                return;

            sideScratch.Clear();
            foreach (int key in keyScratch) {
                WV_ResearchProject project = projects[key];
                project.phase = (byte)WV_ResearchPhase.Complete;
                project.progress = 1f;
                project.progressPerSecond = 0f;
                project.sampleTime = now;
                projects[key] = project;
                sideScratch.Add(SideOf(key));
            }

            // The finished projects' share of the stations passes to whatever is still running.
            foreach (TeamColor side in sideScratch)
                Rebalance(side, now);
        }

        void RebalanceSidesWhoseStationsChanged(float now) {
            sideScratch.Clear();
            foreach (KeyValuePair<int, WV_ResearchProject> entry in projects) {
                if (entry.Value.phase == (byte)WV_ResearchPhase.Researching)
                    sideScratch.Add(SideOf(entry.Key));
            }

            foreach (TeamColor side in sideScratch) {
                float rate = WV_ResearchBuilding.GetResearchRate(side);
                if (!sideRates.TryGetValue(side, out float previous) || !Mathf.Approximately(previous, rate))
                    Rebalance(side, now);
            }
        }

        /// <summary>
        /// Re-divides <paramref name="side"/>'s station throughput across its running projects. Each
        /// project's progress is banked at <paramref name="now"/> first, so a rate change never
        /// rewrites how far a project had already got.
        /// </summary>
        void Rebalance(TeamColor side, float now) {
            float rate = WV_ResearchBuilding.GetResearchRate(side);
            sideRates[side] = rate;
            int active = CountResearching(side);

            keyScratch.Clear();
            foreach (KeyValuePair<int, WV_ResearchProject> entry in projects) {
                if (SideOf(entry.Key) == side && entry.Value.phase == (byte)WV_ResearchPhase.Researching)
                    keyScratch.Add(entry.Key);
            }

            foreach (int key in keyScratch) {
                WV_ResearchProject project = projects[key];
                project.progress = Extrapolate(project, now);
                project.progressPerSecond = WV_TechTree.GetProgressPerSecond(WV_TechTree.Get(TechOf(key)), rate, active);
                project.sampleTime = now;
                projects[key] = project;
            }
        }
#endif
    }
}
