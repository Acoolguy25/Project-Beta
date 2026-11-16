using System;
using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// Seekers stomp: on every footstep of a seeker (the animation event every client already plays), a
    /// heavy, far-carrying 3D step plays on top of the robot's own footstep, so hiders hear them coming.
    /// </summary>
    public sealed class HS_SeekerFootsteps : MonoBehaviour {
        [SerializeField] HS_ClientController controller;
        [Tooltip("3D step; give it a long Max Distance so it carries through the house.")]
        [SerializeField] SfxSound step = new();
        [Tooltip("How often the set of seekers is re-checked (seconds); characters respawn and roles change.")]
        [SerializeField, Min(0.1f)] float refreshInterval = 0.5f;

        sealed class Tracked {
            public CharacterAnimator animator;
            public Action handler;
        }

        readonly Dictionary<int, Tracked> tracked = new();
        readonly List<int> stale = new();
        float nextRefresh;

        void OnDisable() {
            foreach (var t in tracked.Values) Unsubscribe(t);
            tracked.Clear();
        }

        void Update() {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            bool live = controller.HasState && controller.Phase is HS_Phase.Hiding or HS_Phase.Seeking;
            stale.Clear();
            foreach (var pair in tracked)
                if (!live || !Stomps(pair.Key) || pair.Value.animator == null) stale.Add(pair.Key);
            foreach (int id in stale) { Unsubscribe(tracked[id]); tracked.Remove(id); }
            if (!live) return;
            foreach (var pair in controller.RoleTable) {
                if (!Stomps(pair.Key) || tracked.ContainsKey(pair.Key)) continue;
                var character = CharacterLookup.ByClientId(pair.Key);
                if (character == null || !character.TryGetComponent(out CharacterAnimator animator)) continue;
                Transform body = character.transform;
                var t = new Tracked { animator = animator, handler = () => { if (body != null) GameSfx.PlayAt(step, body.position); } };
                animator.Footstep += t.handler;
                tracked[pair.Key] = t;
            }
        }

        /// <summary>Other players who seek; your own steps already sound loud enough to you.</summary>
        bool Stomps(int clientId) =>
            clientId != controller.LocalClientId && controller.RoleTable.TryGetValue(clientId, out HS_Role role) && HS_ClientController.Seeks(role);

        static void Unsubscribe(Tracked t) {
            if (t.animator != null) t.animator.Footstep -= t.handler;
        }
    }
}
