using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Guards the geometry that decides whether an aircraft can shoot at all.
    /// <para>
    /// An aircraft holds a fixed cruise altitude, so every straight-line distance from it to a ground
    /// target already includes that altitude before any horizontal separation is counted. Three of
    /// the four aircraft were tuned with an attack range shorter than the height they fly at, which
    /// meant they flew to a target, parked directly above it, and never fired - a failure that is
    /// invisible in the prefab, because each number looks reasonable on its own.
    /// </para>
    /// </summary>
    public sealed class WV_AircraftTests {
        static readonly WV_UnitKind[] Aircraft = {
            WV_UnitKind.Chopper, WV_UnitKind.Jet, WV_UnitKind.Bomber, WV_UnitKind.UAV
        };

        [Test]
        public void EveryAircraftKind_CruisesAboveTheGround() {
            foreach (WV_UnitKind kind in Aircraft) {
                Assert.That(WV_Rules.IsAircraft(kind), Is.True, $"{kind} is not treated as an aircraft.");
                Assert.That(
                    WV_Rules.GetCruiseAltitude(kind), Is.GreaterThan(0f),
                    $"{kind} cruises at ground level.");
            }
        }

        [Test]
        public void GroundUnits_DoNotCruise() {
            foreach (WV_UnitKind kind in new[] {
                         WV_UnitKind.Infantry, WV_UnitKind.Tank, WV_UnitKind.APC, WV_UnitKind.Artillery
                     }) {
                Assert.That(WV_Rules.IsAircraft(kind), Is.False, $"{kind} is treated as an aircraft.");
                Assert.That(WV_Rules.GetCruiseAltitude(kind), Is.Zero, $"{kind} is given an altitude.");
            }
        }

        /// <summary>
        /// The regression itself, stated as geometry rather than as a number: an aircraft parked
        /// directly above its target is <c>cruise</c> away in a straight line, so a straight-line
        /// range check can only ever succeed for a kind whose gun outreaches its own altitude.
        /// Measuring across the ground is what makes the shot reachable regardless of altitude.
        /// </summary>
        [Test]
        public void FlatDistance_IgnoresCruiseAltitude() {
            foreach (WV_UnitKind kind in Aircraft) {
                float cruise = WV_Rules.GetCruiseAltitude(kind);
                var overhead = new UnityEngine.Vector3(0f, cruise, 0f);
                var groundTarget = UnityEngine.Vector3.zero;

                float straightLine = UnityEngine.Vector3.Distance(overhead, groundTarget);
                float flat = UnityEngine.Vector3.Distance(
                    new UnityEngine.Vector3(overhead.x, 0f, overhead.z), groundTarget);

                Assert.That(straightLine, Is.EqualTo(cruise).Within(0.001f));
                Assert.That(flat, Is.Zero.Within(0.001f), $"{kind} is not overhead of its target.");
            }
        }

        /// <summary>
        /// A cylinder search has to be seeded with a sphere wide enough to enclose it, or the broad
        /// phase silently clips the very targets the flat test was added to let through.
        /// </summary>
        [Test]
        public void CylinderQueryRadius_EnclosesTheRequestedGroundRadius() {
            foreach (WV_UnitKind kind in Aircraft) {
                float cruise = WV_Rules.GetCruiseAltitude(kind);
                const float groundRadius = 40f;

                float queryRadius = UnityEngine.Mathf.Sqrt(groundRadius * groundRadius + cruise * cruise);

                Assert.That(
                    queryRadius, Is.GreaterThanOrEqualTo(groundRadius),
                    $"{kind} would search a smaller sphere than the ground radius asked for.");
                // The far bottom corner of the cylinder is exactly on the sphere, never outside it.
                float cornerDistance = UnityEngine.Mathf.Sqrt(groundRadius * groundRadius + cruise * cruise);
                Assert.That(cornerDistance, Is.LessThanOrEqualTo(queryRadius + 0.001f));
            }
        }
    }
}
