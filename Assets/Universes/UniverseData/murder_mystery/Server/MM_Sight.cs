using System;
using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using UnityEngine;

namespace Universes.murder_mystery.Server {
    /// <summary>What an NPC can see: line of sight between two characters and whether one is in front of the other.</summary>
    public static class MM_Sight {
        /// <summary>Eyes and chest of the robots (about 5.6 m tall), measured from the feet.</summary>
        public const float EyeHeight = 4.4f;
        public const float BodyCentreHeight = 3f;

        const int MaxHits = 12;
        static readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        public static Vector3 Eye(GameCharacter character) => character.transform.position + Vector3.up * EyeHeight;
        public static Vector3 Chest(GameCharacter character) => character.transform.position + Vector3.up * BodyCentreHeight;

        /// <summary>Whether <paramref name="looker"/> has a clear line from its eyes to <paramref name="target"/>'s chest.</summary>
        public static bool HasLineOfSight(GameCharacter looker, GameCharacter target) {
            if (looker == null || target == null)
                return false;
            Vector3 from = Eye(looker);
            Vector3 to = Chest(target);
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f)
                return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++) {
                Transform hit = hits[i].transform;
                if (hit == null || hit.IsChildOf(looker.transform))
                    continue;
                if (hit.IsChildOf(target.transform))
                    return true;
                // Another character in the way blocks the view just like a wall.
                return false;
            }
            return true;
        }

        /// <summary>Whether <paramref name="target"/> is within <paramref name="halfAngle"/> degrees of where <paramref name="looker"/> faces.</summary>
        public static bool InView(GameCharacter looker, GameCharacter target, float halfAngle) {
            Vector3 toTarget = target.transform.position - looker.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f)
                return true;
            Vector3 forward = looker.transform.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, toTarget) <= halfAngle;
        }
    }
}
