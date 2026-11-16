using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// A running effect started with <see cref="GameFx.Start"/> or <see cref="GameFx.Attach"/>. Keep it
    /// and call <see cref="Stop"/> when done. A handle is a small value: copying it is fine, a
    /// default one does nothing, and it goes stale (all calls no-op) once its effect stopped.
    /// </summary>
    public readonly struct FxHandle {
        internal readonly FxKind kind;
        internal readonly int index;
        internal readonly int generation;

        internal FxHandle(FxKind kind, int index, int generation) {
            this.kind = kind;
            this.index = index;
            this.generation = generation;
        }

        /// <summary>True while the effect this handle started is still running.</summary>
        public bool IsPlaying => generation != 0 && GameFeelRunner.Existing != null && GameFeelRunner.Existing.Fx.IsPlaying(this);

        /// <summary>Stops emitting; live particles fade out, or vanish at once with <paramref name="clear"/>.</summary>
        public void Stop(bool clear = false) {
            if (generation != 0)
                GameFeelRunner.Existing?.Fx.Stop(this, clear);
        }

        /// <summary>Moves the effect (ignored while it follows a transform).</summary>
        public void SetPosition(Vector3 position) {
            if (generation != 0)
                GameFeelRunner.Existing?.Fx.SetPosition(this, position);
        }

        /// <summary>Changes the effect's tint.</summary>
        public void SetTint(Color tint) {
            if (generation != 0)
                GameFeelRunner.Existing?.Fx.SetTint(this, tint);
        }
    }
}
