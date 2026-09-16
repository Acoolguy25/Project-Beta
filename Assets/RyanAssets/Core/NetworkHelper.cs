using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using FishNet.Connection;
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

    public static class NetworkHelper {
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
                    GetMotionClock(manager.TimeManager)?.NetworkUpdate();
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

        /// <summary>
        /// One wall-time-based server epoch per manager. Timestamp exchanges correct small
        /// client drift; delayed replies cannot wind the clock back after either peer stalls.
        /// Physics discontinuities are marked so consumers place rather than sweep bodies.
        /// </summary>
        public sealed class MotionClock {
            private const double RequestInterval = 0.25d;
            private const double RequestLifetime = 2d;
            public TimeManager Manager { get; }
            public double Time { get; private set; }
            public bool IsReady { get; private set; }
            public bool IsResynchronizing { get; private set; }
            public double ResynchronizeThreshold { get; set; } = 0.25d;
            public float CorrectionRate {
                get => _correctionRate;
                set => _correctionRate = Mathf.Clamp(value, 0.01f, 0.5f);
            }
            public event Action<double, double> PhysicsStep;

            private float _correctionRate = 0.1f;
            private double _sampleTime, _sampleLocalTime;
            private bool _hasSample, _initialized;
            private double _lastUnityFixedTime = double.NegativeInfinity;
            private bool _serverEpochSet, _hasReference;
            private double _serverEpochTime, _serverEpochLocalTime;
            private double _referenceTime, _referenceLocalTime;
            private double _nextRequest, _bestRoundTrip = double.PositiveInfinity;
            private uint _sequence, _lastAcceptedSequence;
            private readonly Dictionary<uint, double> _requests = new();
            private readonly Queue<uint> _requestOrder = new();

            internal MotionClock(TimeManager manager) {
                Manager = manager;
                manager.OnLateUpdate += SampleTime;
                manager.OnPrePhysicsSimulation += BeforePhysics;
                manager.NetworkManager.ServerManager.RegisterBroadcast<MotionTimeRequest>(ReplyWithTime);
                manager.NetworkManager.ClientManager.RegisterBroadcast<MotionTimeReply>(ReceiveTime);
            }

            internal void Dispose() {
                if (Manager != null) {
                    Manager.OnLateUpdate -= SampleTime;
                    Manager.OnPrePhysicsSimulation -= BeforePhysics;
                    if (Manager.NetworkManager != null) {
                        Manager.NetworkManager.ServerManager.UnregisterBroadcast<MotionTimeRequest>(ReplyWithTime);
                        Manager.NetworkManager.ClientManager.UnregisterBroadcast<MotionTimeReply>(ReceiveTime);
                    }
                }
                PhysicsStep = null;
            }

            private bool HasNetworkTime() => Manager != null && Manager.NetworkManager != null &&
                (Manager.NetworkManager.IsServerStarted ||
                 (Manager.NetworkManager.IsClientStarted && Manager.NetworkManager.ClientManager.Connection.IsAuthenticated));

            private double ServerTimeAt(double localTime) {
                if (!_serverEpochSet) {
                    _serverEpochTime = Manager.TicksToTime(Manager.GetPreciseTick(TickType.Tick));
                    _serverEpochLocalTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                    _serverEpochSet = true;
                }
                return _serverEpochTime + localTime - _serverEpochLocalTime;
            }

            internal void NetworkUpdate() {
                if (!HasNetworkTime()) {
                    _hasSample = _initialized = _hasReference = _serverEpochSet = IsReady = false;
                    _requests.Clear();
                    _requestOrder.Clear();
                    _nextRequest = 0d;
                    _bestRoundTrip = double.PositiveInfinity;
                    return;
                }
                double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                if (Manager.NetworkManager.IsServerStarted) {
                    ServerTimeAt(now);
                    return;
                }
                _serverEpochSet = false;
                if (now < _nextRequest)
                    return;
                _nextRequest = now + RequestInterval;
                while (_requestOrder.Count > 0) {
                    uint oldest = _requestOrder.Peek();
                    if (_requests.TryGetValue(oldest, out double sent) && now - sent < RequestLifetime)
                        break;
                    _requests.Remove(_requestOrder.Dequeue());
                }
                uint sequence = ++_sequence;
                _requests.Add(sequence, now);
                _requestOrder.Enqueue(sequence);
                Manager.NetworkManager.ClientManager.Broadcast(new MotionTimeRequest { Sequence = sequence }, Channel.Unreliable);
            }

            private void ReplyWithTime(NetworkConnection connection, MotionTimeRequest request, Channel channel) {
                double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                Manager.NetworkManager.ServerManager.Broadcast(connection,
                    new MotionTimeReply { Sequence = request.Sequence, ServerTime = ServerTimeAt(now) },
                    true, Channel.Unreliable);
            }

            private void ReceiveTime(MotionTimeReply reply, Channel channel) {
                if (Manager.NetworkManager.IsServerStarted || !_requests.TryGetValue(reply.Sequence, out double sent))
                    return;
                _requests.Remove(reply.Sequence);
                if (_hasReference && unchecked((int)(reply.Sequence - _lastAcceptedSequence)) <= 0)
                    return;
                double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                double roundTrip = now - sent;
                // A request queued during a freeze measures the stall as network latency.
                // Prefer low-delay exchanges; never add half that stall to server time.
                // Relearn the delay floor after a sustained change in the network route.
                if (_hasReference && now - _referenceLocalTime > 10d)
                    _bestRoundTrip = double.PositiveInfinity;
                if (!AcceptMotionTimeSample(roundTrip, _bestRoundTrip) ||
                    double.IsNaN(reply.ServerTime) || double.IsInfinity(reply.ServerTime))
                    return;
                _bestRoundTrip = Math.Min(_bestRoundTrip, roundTrip);
                _referenceTime = reply.ServerTime + roundTrip * 0.5d;
                _referenceLocalTime = now;
                _lastAcceptedSequence = reply.Sequence;
                _hasReference = true;
            }

            private void SampleTime() {
                if (!HasNetworkTime() || (!Manager.NetworkManager.IsServerStarted && !_hasReference)) {
                    IsReady = _hasSample = false;
                    return;
                }
                double localTime = UnityEngine.Time.unscaledTimeAsDouble;
                _sampleTime = Manager.NetworkManager.IsServerStarted
                    ? ServerTimeAt(localTime)
                    : _referenceTime + localTime - _referenceLocalTime;
                _sampleLocalTime = localTime;
                _hasSample = true;
            }

            public void UnityFixedUpdate() {
                if (Manager == null || Manager.PhysicsMode != PhysicsMode.Unity)
                    return;
                double fixedTime = UnityEngine.Time.fixedTimeAsDouble;
                if (fixedTime == _lastUnityFixedTime)
                    return;
                _lastUnityFixedTime = fixedTime;
                AdvancePhysics(UnityEngine.Time.fixedDeltaTime);
            }

            private void BeforePhysics(float scaledDeltaTime) {
                if (Manager.PhysicsMode == PhysicsMode.TimeManager)
                    AdvancePhysics(scaledDeltaTime);
            }

            private void AdvancePhysics(float scaledDeltaTime) {
                if (!HasNetworkTime() || !_hasSample) {
                    IsReady = false;
                    return;
                }
                double deltaTime, localTime;
                switch (Manager.PhysicsMode) {
                    case PhysicsMode.Unity:
                        // The unscaled timestamp may jump after discarded physics time,
                        // but the duration actually simulated remains fixedDeltaTime.
                        deltaTime = scaledDeltaTime / Math.Max(UnityEngine.Time.timeScale, 0.0001f);
                        localTime = UnityEngine.Time.fixedUnscaledTimeAsDouble;
                        break;
                    case PhysicsMode.TimeManager:
                        deltaTime = Manager.TickDelta;
                        // End of this simulation within the current tick batch. Tick is
                        // deliberately not used as an absolute time source.
                        localTime = UnityEngine.Time.unscaledTimeAsDouble - Manager.GetTickElapsedAsDouble() + deltaTime;
                        break;
                    default:
                        return;
                }
                double targetTime = _sampleTime + localTime - _sampleLocalTime;
                IsResynchronizing = !_initialized || Math.Abs(targetTime - (Time + deltaTime)) > ResynchronizeThreshold;
                Time = IsResynchronizing ? Math.Max(0d, targetTime) : AdvanceMotionTime(Time, targetTime, deltaTime, CorrectionRate);
                _initialized = IsReady = true;
                PhysicsStep?.Invoke(Time, deltaTime);
            }
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
