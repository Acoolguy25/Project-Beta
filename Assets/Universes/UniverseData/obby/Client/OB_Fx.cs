using System.Collections.Generic;
using RyanAssets.Characters.Client;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Plays Sky Obby's one-shot particle effects from authored prefabs: race feedback (coins, finish,
    /// respawn) on request, plus jump and landing puffs for the local player. Checkpoint pads carry
    /// their own celebration effect (<c>CheckpointMarker.reachedEffect</c>).
    /// Each effect keeps a small pool of instances, so effects never allocate during a race.
    /// </summary>
    public sealed class OB_Fx : MonoBehaviour {
        [Header("Effects (world-space, one-shot particle prefabs)")]
        public ParticleSystem coinBurst;
        public ParticleSystem finishBurst;
        public ParticleSystem respawnPoof;
        public ParticleSystem jumpPuff;
        public ParticleSystem landPuff;
        [Tooltip("Instances kept per effect; the oldest is reused when all are busy.")]
        [Range(1, 8)] public int poolSize = 3;

        [Header("Local player")]
        [Tooltip("Smallest fall speed (m/s, along gravity) that leaves a landing puff.")]
        [Min(0f)] public float landingSpeed = 7f;
        [Tooltip("Smallest take-off speed (m/s, against gravity) that counts as a jump.")]
        [Min(0f)] public float jumpSpeed = 4f;

        sealed class Pool {
            public readonly List<ParticleSystem> items = new();
            public int next;
        }

        readonly Dictionary<ParticleSystem, Pool> pools = new();
        CharacterMovement player;
        bool wasGrounded;
        float airborneSpeed;

        public void Coin(Vector3 position) => Play(coinBurst, position, Vector3.up);
        public void Finish(Vector3 position) => Play(finishBurst, position, Vector3.up);

        /// <summary>A poof where the local player is now, e.g. just before or after a respawn.</summary>
        public void RespawnAtPlayer() {
            if (CharacterMovement.Local != null)
                Play(respawnPoof, CharacterMovement.Local.Body.position, CharacterMovement.Local.Up);
        }

        /// <summary>Plays <paramref name="effect"/> at <paramref name="position"/> with its up axis along <paramref name="up"/>.</summary>
        public void Play(ParticleSystem effect, Vector3 position, Vector3 up) {
            if (effect == null) return;
            if (!pools.TryGetValue(effect, out Pool pool)) pools.Add(effect, pool = new Pool());
            ParticleSystem instance = null;
            foreach (var item in pool.items)
                if (!item.IsAlive(true)) { instance = item; break; }
            if (instance == null && pool.items.Count < poolSize) {
                instance = Instantiate(effect, transform);
                pool.items.Add(instance);
            }
            if (instance == null) {
                instance = pool.items[pool.next];
                pool.next = (pool.next + 1) % pool.items.Count;
            }
            instance.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, up));
            // Stop-and-clear rewinds the system, so its time-zero bursts fire again (Clear alone does not).
            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.Play(true);
        }

        void Update() {
            var local = CharacterMovement.Local;
            if (local != player) {
                player = local;
                wasGrounded = player != null && player.Grounded;
                airborneSpeed = 0f;
            }
            if (player == null || player.Body == null) return;

            bool grounded = player.Grounded;
            float along = Vector3.Dot(player.Body.linearVelocity, player.Up);
            if (!grounded) airborneSpeed = Mathf.Min(airborneSpeed, along);
            if (grounded && !wasGrounded && -airborneSpeed >= landingSpeed)
                Play(landPuff, player.Body.position, player.Up);
            if (!grounded && wasGrounded && along >= jumpSpeed)
                Play(jumpPuff, player.Body.position, player.Up);
            if (grounded) airborneSpeed = 0f;
            wasGrounded = grounded;
        }
    }
}
