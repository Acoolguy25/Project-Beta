using System;
using System.Collections.Generic;
using FishNet;
using RyanAssets.Characters.Shared;
using UnityEngine;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// Thrown knives, simulated by the server. A knife flies in a straight line at
    /// <see cref="MM_ClassicTuning.throwSpeed"/>, swept as a sphere each frame, and stops in the first
    /// living character (who it kills, through <see cref="KnifeHit"/>) or the first wall it meets.
    /// Clients only draw it: they get <see cref="MM_KnifeThrownBroadcast"/> when it leaves the hand and
    /// <see cref="MM_KnifeLandedBroadcast"/> when it stops.
    /// </summary>
    public sealed class MM_KnifeThrows {
        sealed class Flight {
            public int id;
            public GameCharacter thrower;
            public Vector3 position, direction;
            public float travelled;
        }

        // Plenty for a sphere a little wider than a robot sweeping a metre or two per frame.
        const int MaxHitsPerStep = 16;

        readonly MM_ClassicTuning tuning;
        readonly List<Flight> flights = new();
        readonly RaycastHit[] hits = new RaycastHit[MaxHitsPerStep];
        int nextId;

        /// <summary>A knife struck a living character. Return true if it killed them (the knife stops there either way).</summary>
        public Func<GameCharacter, GameCharacter, bool> KnifeHit;

        public MM_KnifeThrows(MM_ClassicTuning tuning) {
            this.tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        }

        public int InFlight => flights.Count;

        /// <summary>Throws <paramref name="thrower"/>'s knife at <paramref name="target"/>. The caller has checked the thrower may.</summary>
        public void Launch(GameCharacter thrower, Vector3 target, byte skin) {
            Vector3 origin = Origin(thrower);
            Vector3 direction = target - origin;
            if (direction.sqrMagnitude < 0.01f)
                direction = thrower.transform.forward;
            direction.Normalize();
            Flight flight = new() {
                id = nextId++,
                thrower = thrower,
                position = origin,
                direction = direction,
            };
            flights.Add(flight);
            InstanceFinder.ServerManager.Broadcast(new MM_KnifeThrownBroadcast {
                throwId = flight.id,
                origin = origin,
                direction = direction,
                speed = tuning.throwSpeed,
                maxDistance = tuning.throwRange,
                skin = skin,
            });
        }

        /// <summary>Where a knife leaves <paramref name="thrower"/>'s hand.</summary>
        public Vector3 Origin(GameCharacter thrower) => thrower.transform.position + Vector3.up * tuning.throwHeight;

        /// <summary>Moves every knife on by <paramref name="deltaTime"/>.</summary>
        public void Tick(float deltaTime) {
            for (int i = flights.Count - 1; i >= 0; i--) {
                Flight flight = flights[i];
                float step = Mathf.Min(tuning.throwSpeed * deltaTime, tuning.throwRange - flight.travelled);
                if (Advance(flight, step, out Vector3 landedAt, out bool hitCharacter) || flight.travelled >= tuning.throwRange) {
                    flights.RemoveAt(i);
                    InstanceFinder.ServerManager.Broadcast(new MM_KnifeLandedBroadcast {
                        throwId = flight.id,
                        position = landedAt,
                        hitCharacter = hitCharacter,
                    });
                }
            }
        }

        /// <summary>Drops every knife in the air (the round ended).</summary>
        public void Clear() => flights.Clear();

        /// <summary>Sweeps one step. True when the knife stopped (in someone or in a wall).</summary>
        bool Advance(Flight flight, float step, out Vector3 landedAt, out bool hitCharacter) {
            landedAt = flight.position;
            hitCharacter = false;
            if (step <= 0f)
                return true;
            int count = Physics.SphereCastNonAlloc(flight.position, tuning.throwRadius, flight.direction, hits, step,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, 0, count, HitDistanceComparer.Instance);
            for (int h = 0; h < count; h++) {
                RaycastHit hit = hits[h];
                if (hit.collider == null || IsPartOf(hit.collider, flight.thrower))
                    continue;
                // A sweep that starts inside a collider reports distance 0 and no point.
                Vector3 point = hit.distance <= 0f ? flight.position : hit.point;
                GameCharacter character = hit.collider.GetComponentInParent<GameCharacter>();
                if (character != null) {
                    // Corpses and their ragdolls do not stop a knife.
                    if (character.IsDead)
                        continue;
                    landedAt = point;
                    hitCharacter = true;
                    KnifeHit?.Invoke(flight.thrower, character);
                    return true;
                }
                landedAt = point;
                return true;
            }
            flight.position += flight.direction * step;
            flight.travelled += step;
            landedAt = flight.position;
            return false;
        }

        static bool IsPartOf(Collider collider, GameCharacter character) =>
            character != null && collider.transform.IsChildOf(character.transform);

        sealed class HitDistanceComparer : IComparer<RaycastHit> {
            public static readonly HitDistanceComparer Instance = new();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
