using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>Per-kind pools of the generated particle prefabs, tinted through property blocks.</summary>
    internal sealed class FxPool {
        sealed class Instance {
            public ParticleSystem system;
            public Transform transform;
            public ParticleSystemRenderer[] renderers;
            public Color tint;
            public int generation;
            public bool busy;
            public bool held;          // started through a handle; never stolen
            public float startedAt;
            public float stopAt;       // auto-stop for looping kinds played as one-shots
            public Transform follow;
            public Vector3 followOffset;
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly Transform parent;
        readonly GameFxLibrary library;
        readonly List<Instance>[] pools;
        readonly MaterialPropertyBlock block = new();
        readonly bool[] warned;
        int nextGeneration = 1;

        public FxPool(Transform parent, GameFxLibrary library) {
            this.parent = parent;
            this.library = library;
            int kinds = System.Enum.GetValues(typeof(FxKind)).Length;
            pools = new List<Instance>[kinds];
            warned = new bool[kinds];
        }

        public FxHandle Play(FxKind kind, Vector3 position, Quaternion rotation, Color tint, float scale, bool hold, Transform follow, Vector3 followOffset) {
            GameFxLibrary.Entry entry = library != null ? library.Get(kind) : null;
            if (entry == null || entry.prefab == null) {
                int k = (int)kind;
                if (k >= 0 && k < warned.Length && !warned[k]) {
                    warned[k] = true;
                    Debug.LogError($"GameFeel: no prefab for effect {kind}. Run Ryan > Game Feel > Rebuild.");
                }
                return default;
            }
            List<Instance> pool = pools[(int)kind] ??= new List<Instance>(entry.poolSize);
            int index = Take(pool, entry, hold);
            if (index < 0)
                return default;

            Instance instance = pool[index];
            instance.busy = true;
            instance.held = hold;
            instance.generation = nextGeneration++;
            instance.startedAt = Time.time;
            instance.follow = follow;
            instance.followOffset = followOffset;
            bool loops = instance.system.main.loop;
            instance.stopAt = loops && !hold ? Time.time + library.loopingPlaySeconds : float.PositiveInfinity;

            Vector3 at = follow != null ? follow.TransformPoint(followOffset) : position;
            instance.transform.SetPositionAndRotation(at, rotation);
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            ApplyTint(instance, tint);
            if (!instance.system.gameObject.activeSelf)
                instance.system.gameObject.SetActive(true);
            // Stop-and-clear rewinds the system, so its time-zero bursts fire again.
            instance.system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.system.Play(true);
            return new FxHandle(kind, index, instance.generation);
        }

        int Take(List<Instance> pool, GameFxLibrary.Entry entry, bool hold) {
            int oldest = -1;
            for (int i = 0; i < pool.Count; i++) {
                Instance instance = pool[i];
                if (instance.system == null)
                    continue;
                if (!instance.busy)
                    return i;
                if (!instance.held && (oldest < 0 || instance.startedAt < pool[oldest].startedAt))
                    oldest = i;
            }
            // Held effects (beacons, trails) must not be stolen, so they may grow past the pool size.
            if (pool.Count < entry.poolSize || hold || oldest < 0) {
                ParticleSystem system = Object.Instantiate(entry.prefab, parent);
                system.name = entry.prefab.name;
                pool.Add(new Instance {
                    system = system,
                    transform = system.transform,
                    renderers = system.GetComponentsInChildren<ParticleSystemRenderer>(true),
                    tint = new Color(-1f, -1f, -1f, -1f),
                });
                return pool.Count - 1;
            }
            return oldest;
        }

        void ApplyTint(Instance instance, Color tint) {
            if (instance.tint == tint)
                return;
            instance.tint = tint;
            block.Clear();
            block.SetColor(BaseColorId, tint);
            foreach (ParticleSystemRenderer renderer in instance.renderers)
                if (renderer != null)
                    renderer.SetPropertyBlock(block);
        }

        Instance Resolve(in FxHandle handle) {
            int kind = (int)handle.kind;
            if (kind < 0 || kind >= pools.Length || pools[kind] == null || handle.index < 0 || handle.index >= pools[kind].Count)
                return null;
            Instance instance = pools[kind][handle.index];
            return instance.busy && instance.generation == handle.generation && instance.system != null ? instance : null;
        }

        public bool IsPlaying(in FxHandle handle) => Resolve(handle) != null;

        public void Stop(in FxHandle handle, bool clear) {
            Instance instance = Resolve(handle);
            if (instance == null)
                return;
            instance.held = false;
            instance.follow = null;
            instance.stopAt = float.PositiveInfinity;
            instance.system.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        public void SetPosition(in FxHandle handle, Vector3 position) {
            Instance instance = Resolve(handle);
            if (instance != null && instance.follow == null)
                instance.transform.position = position;
        }

        public void SetTint(in FxHandle handle, Color tint) {
            Instance instance = Resolve(handle);
            if (instance != null)
                ApplyTint(instance, tint);
        }

        public void Update() {
            float now = Time.time;
            for (int k = 0; k < pools.Length; k++) {
                List<Instance> pool = pools[k];
                if (pool == null)
                    continue;
                for (int i = 0; i < pool.Count; i++) {
                    Instance instance = pool[i];
                    if (!instance.busy)
                        continue;
                    if (instance.system == null) {
                        instance.busy = false;
                        continue;
                    }
                    if (instance.held && instance.follow != null) {
                        instance.transform.position = instance.follow.TransformPoint(instance.followOffset);
                    } else if (instance.held && instance.follow is not null) {
                        // The followed object was destroyed: let the effect fade out where it is.
                        instance.held = false;
                        instance.follow = null;
                        instance.system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    if (now >= instance.stopAt) {
                        instance.stopAt = float.PositiveInfinity;
                        instance.system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    if (!instance.held && !instance.system.IsAlive(true))
                        instance.busy = false;
                }
            }
        }
    }
}
