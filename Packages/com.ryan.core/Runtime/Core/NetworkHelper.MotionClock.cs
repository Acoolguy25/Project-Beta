using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using UnityEngine;

namespace RyanAssets.Core {
    public static partial class NetworkHelper {
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
            private double _physicsTimeOffset;
            private bool _physicsTimeOffsetInitialized;
            private bool _serverEpochSet, _hasReference;
            private double _serverEpochTime, _serverEpochLocalTime;
            private double _referenceTime, _referenceLocalTime;
            private double _nextRequest, _bestRoundTrip = double.PositiveInfinity;
            private uint _sequence, _lastAcceptedSequence;
            private readonly Dictionary<uint, double> _requests = new();
            private readonly Queue<uint> _requestOrder = new();

            internal MotionClock(TimeManager manager) {
                Manager = manager;
                manager.OnUpdate += NetworkUpdate;
                manager.OnLateUpdate += AlignPhysicsTime;
                manager.OnPrePhysicsSimulation += AdvancePhysics;
                AlignPhysicsTime();
                manager.NetworkManager.ServerManager.RegisterBroadcast<MotionTimeRequest>(ReplyWithTime);
                manager.NetworkManager.ClientManager.RegisterBroadcast<MotionTimeReply>(ReceiveTime);
            }

            internal void Dispose() {
                if (Manager != null) {
                    Manager.OnUpdate -= NetworkUpdate;
                    Manager.OnLateUpdate -= AlignPhysicsTime;
                    Manager.OnPrePhysicsSimulation -= AdvancePhysics;
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
                    _hasReference = _serverEpochSet = IsReady = false;
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

            private void AlignPhysicsTime() {
                // Map Unity's frame/physics timeline to wall time once per frame.
                // Reading wall time in every catch-up step would give all those steps
                // the same target, causing a second, backwards snap after a freeze.
                // Keep the mapping fixed during ordinary frames so frame workload
                // cannot modulate the motion phase. Rebase after a discarded time gap.
                double offset = UnityEngine.Time.realtimeSinceStartupAsDouble - UnityEngine.Time.unscaledTimeAsDouble;
                if (!_physicsTimeOffsetInitialized || Math.Abs(offset - _physicsTimeOffset) > ResynchronizeThreshold) {
                    _physicsTimeOffset = offset;
                    _physicsTimeOffsetInitialized = true;
                }
            }

            // FishNet raises this for both Unity and manually simulated physics.
            private void AdvancePhysics(float scaledDeltaTime) {
                if (!HasNetworkTime() || (!Manager.NetworkManager.IsServerStarted && !_hasReference)) {
                    IsReady = false;
                    return;
                }
                double deltaTime, localTime;
                switch (Manager.PhysicsMode) {
                    case PhysicsMode.Unity:
                        // The unscaled timestamp may jump after discarded physics time,
                        // but the duration actually simulated remains fixedDeltaTime.
                        deltaTime = scaledDeltaTime / Math.Max(UnityEngine.Time.timeScale, 0.0001f);
                        localTime = UnityEngine.Time.fixedUnscaledTimeAsDouble + _physicsTimeOffset;
                        break;
                    case PhysicsMode.TimeManager:
                        deltaTime = Manager.TickDelta;
                        // End of this simulation within the current tick batch. Tick is
                        // deliberately not used as an absolute time source.
                        localTime = UnityEngine.Time.unscaledTimeAsDouble + _physicsTimeOffset - Manager.GetTickElapsedAsDouble() + deltaTime;
                        break;
                    default:
                        return;
                }
                double targetTime = Manager.NetworkManager.IsServerStarted
                    ? ServerTimeAt(localTime)
                    : _referenceTime + localTime - _referenceLocalTime;
                IsResynchronizing = !IsReady || Math.Abs(targetTime - (Time + deltaTime)) > ResynchronizeThreshold;
                Time = IsResynchronizing ? Math.Max(0d, targetTime) : AdvanceMotionTime(Time, targetTime, deltaTime, CorrectionRate);
                IsReady = true;
                PhysicsStep?.Invoke(Time, deltaTime);
            }
        }
    }
}
