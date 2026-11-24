using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Server-side judge for hazards that are deadly only part of the time: laser gates (a
    /// <see cref="TimedToggleBlock"/> with a <see cref="HazardVolume"/>) and <see cref="Crusher"/>s.
    /// <para>
    /// A player's position reaches the server a little after the player saw it, and the server's
    /// clock may run slightly ahead of theirs, so judging these against the server's current physics
    /// state would send back a player who visibly cleared a beam or a slam. This judge evaluates
    /// each hazard analytically at the shared-clock moment the player saw (the caller passes it),
    /// and only while it is deadly for <c>grace</c> seconds either side of that moment, the same
    /// leniency at both ends of every deadly window. Their colliders still switch on the server
    /// (so a plain overlap query sees them); callers skip those volumes with <see cref="Contains"/>.
    /// </para>
    /// </summary>
    public sealed class TimedHazards {
        readonly struct Gate {
            public readonly TimedToggleBlock block;
            public readonly BoxCollider box;
            public Gate(TimedToggleBlock block, BoxCollider box) { this.block = block; this.box = box; }
        }

        readonly struct Slam {
            public readonly Crusher crusher;
            public readonly BoxCollider zone;
            public Slam(Crusher crusher, BoxCollider zone) { this.crusher = crusher; this.zone = zone; }
        }

        readonly List<Gate> gates = new();
        readonly List<Slam> slams = new();
        readonly HashSet<HazardVolume> volumes = new();

        public int Count => gates.Count + slams.Count;

        /// <summary>Finds the timed hazards under <paramref name="root"/> (call after the level is built).</summary>
        public void Collect(Transform root) {
            Clear();
            if (root == null) return;
            foreach (var block in root.GetComponentsInChildren<TimedToggleBlock>(true)) {
                var volume = block.GetComponentInParent<HazardVolume>(true);
                if (volume == null) continue; // a beat block: solid, not deadly
                volumes.Add(volume);
                foreach (var box in block.GetComponents<BoxCollider>()) gates.Add(new Gate(block, box));
            }
            foreach (var crusher in root.GetComponentsInChildren<Crusher>(true)) {
                if (crusher.crushZone is not BoxCollider zone) continue;
                var volume = zone.GetComponentInParent<HazardVolume>(true);
                if (volume != null) volumes.Add(volume);
                slams.Add(new Slam(crusher, zone));
            }
        }

        public void Clear() {
            gates.Clear();
            slams.Clear();
            volumes.Clear();
        }

        /// <summary>Whether <paramref name="volume"/> belongs to a timed hazard (judged here, not by a plain overlap).</summary>
        public bool Contains(HazardVolume volume) => volume != null && volumes.Contains(volume);

        /// <summary>
        /// True when the body box (centre, half extents, rotation) overlaps a timed hazard as it was at
        /// shared-clock <paramref name="time"/>, and that hazard is deadly from <paramref name="grace"/>
        /// seconds before to <paramref name="grace"/> seconds after it.
        /// </summary>
        public bool Hits(Vector3 center, Vector3 halfExtents, Quaternion orientation, double time, float grace) {
            foreach (var gate in gates) {
                if (gate.block == null || gate.box == null || !gate.block.isActiveAndEnabled) continue;
                if (!SolidThroughout(gate.block, time, grace)) continue;
                PlatformerQuery.GetBox(gate.box, 0f, out Vector3 c, out Vector3 h, out Quaternion r);
                if (PlatformerQuery.BoxesOverlap(center, halfExtents, orientation, c, h, r)) return true;
            }
            foreach (var slam in slams) {
                if (slam.crusher == null || slam.zone == null || !slam.crusher.isActiveAndEnabled) continue;
                if (!DeadlyThroughout(slam.crusher, time, grace)) continue;
                // The zone rides on the block: move it from where the block is now to where it was then.
                PlatformerQuery.GetBox(slam.zone, 0f, out Vector3 c, out Vector3 h, out Quaternion r);
                c += slam.crusher.PositionAt(time) - slam.crusher.transform.position;
                if (PlatformerQuery.BoxesOverlap(center, halfExtents, orientation, c, h, r)) return true;
            }
            return false;
        }

        /// <summary>Whether a gate is on at <paramref name="time"/> and stays on <paramref name="grace"/> seconds either side.</summary>
        public static bool SolidThroughout(TimedToggleBlock block, double time, float grace) =>
            block.IsSolidAt(time) && block.IsSolidAt(time - grace) && block.IsSolidAt(time + grace);

        /// <summary>Whether a crusher is slamming or down at <paramref name="time"/> and stays so <paramref name="grace"/> seconds either side.</summary>
        public static bool DeadlyThroughout(Crusher crusher, double time, float grace) =>
            crusher.IsDeadlyAt(time) && crusher.IsDeadlyAt(time - grace) && crusher.IsDeadlyAt(time + grace);
    }
}
