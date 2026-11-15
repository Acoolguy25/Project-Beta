using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing.Timing;
using UnityEngine;

namespace RyanAssets.Core {
    // Motion time is separate from FishNet's simulation ticks: a stalled server may
    // discard ticks, but its motion epoch must continue advancing for every client.
    public struct MotionTimeRequest : IBroadcast {
        public uint Sequence;
    }

    public struct MotionTimeReply : IBroadcast {
        public uint Sequence;
        public double ServerTime;
    }

    public static partial class NetworkHelper {
        // Keep authoritative deadline/cooldown queries independent of presentation smoothing.
        public static float ServerTime => GetServerTime();
        public static float GetServerTime() {
            var manager = InstanceFinder.TimeManager;
            return manager == null ? 0f : (float)manager.TicksToTime(manager.Tick);
        }

        private static readonly Dictionary<TimeManager, MotionClock> MotionClocks = new();
        private static readonly List<TimeManager> ExpiredManagers = new();

        public static MotionClock GetMotionClock(TimeManager manager = null) {
            if (manager == null)
                manager = InstanceFinder.TimeManager;
            if (manager == null || manager.NetworkManager == null || !manager.NetworkManager.Initialized)
                return null;
            if (!MotionClocks.TryGetValue(manager, out var clock)) {
                clock = new MotionClock(manager);
                MotionClocks.Add(manager, clock);
            }
            return clock;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMotionClocks() {
            foreach (var clock in MotionClocks.Values)
                clock.Dispose();
            MotionClocks.Clear();
            ExpiredManagers.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartMotionClocks() {
            var driver = new GameObject("Network Motion Clock", typeof(NetworkMotionClockDriver));
            driver.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            UnityEngine.Object.DontDestroyOnLoad(driver);
        }

        internal static void UpdateMotionClocks() {
            // Servers must answer even when their scene has no moving platforms.
            foreach (var manager in FishNet.Managing.NetworkManager.Instances) {
                if (manager != null && manager.Initialized)
                    GetMotionClock(manager.TimeManager);
            }
            foreach (var pair in MotionClocks) {
                if (pair.Key == null || pair.Key.NetworkManager == null)
                    ExpiredManagers.Add(pair.Key);
            }
            foreach (var manager in ExpiredManagers) {
                MotionClocks[manager].Dispose();
                MotionClocks.Remove(manager);
            }
            ExpiredManagers.Clear();
        }

        public static bool AcceptMotionTimeSample(double roundTrip, double bestRoundTrip) =>
            roundTrip >= 0d && roundTrip < 2d && roundTrip <= Math.Max(0.25d, bestRoundTrip * 2d + 0.05d);

        public static double AdvanceMotionTime(double currentTime, double targetTime, double deltaTime, float correctionRate = 0.1f) {
            if (deltaTime <= 0d || double.IsNaN(deltaTime) || double.IsInfinity(deltaTime))
                return currentTime;
            double nextTime = currentTime + deltaTime;
            if (double.IsNaN(targetTime) || double.IsInfinity(targetTime))
                return nextTime;
            double maxCorrection = deltaTime * Mathf.Clamp(correctionRate, 0.01f, 0.5f);
            double correction = (targetTime - nextTime) * (1d - Math.Exp(-deltaTime / 0.5d));
            return nextTime + Math.Max(-maxCorrection, Math.Min(maxCorrection, correction));
        }
    }
}
