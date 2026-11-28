using System;
using RyanAssets.Cameras;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>How far the commander's camera may pull back in War Valley.</summary>
    [Serializable]
    public sealed class WV_CommanderCameraTuning {
        [Tooltip("Farthest the camera may pull back, in metres. The shared camera stops at 45 m, which " +
                 "frames one soldier but not a base of 20-30 m buildings around the flag.")]
        [Min(10f)] public float maxZoom = 140f;
        [Tooltip("Distance the camera starts at when the commander arrives, in metres.")]
        [Min(5f)] public float startZoom = 70f;
    }

    /// <summary>
    /// Widens the shared third-person camera's zoom range for the battlefield's scale, and puts the
    /// camera back as it found it when the commander leaves. The camera outlives the match scene,
    /// so the range is restored rather than left for the next game.
    /// </summary>
    public sealed class WV_CommanderCamera {
        readonly WV_CommanderCameraTuning tuning;
        ZoomComponent zoom;
        float previousMax;
        Camera candidate;

        public WV_CommanderCamera(WV_CommanderCameraTuning tuning) => this.tuning = tuning;

        /// <summary>Called every frame by the client controller: adopts the camera once it exists.</summary>
        public void Tick() {
            Camera main = Camera.main;
            if (main == null || (zoom != null && zoom.gameObject == main.gameObject))
                return;
            // A camera spawned this frame still has its Start ahead of it, and the shared camera's
            // Start resets the zoom to its orbit radius. Adopting it a frame later keeps our range.
            if (main != candidate) {
                candidate = main;
                return;
            }
            Release();
            if (!main.TryGetComponent(out zoom))
                return;
            previousMax = zoom.MaxZoom;
            zoom.MaxZoom = Mathf.Max(zoom.MinZoom, tuning.maxZoom);
            zoom.SetZoom(tuning.startZoom);
        }

        /// <summary>Gives the camera its own zoom range back.</summary>
        public void Release() {
            if (zoom != null) {
                zoom.MaxZoom = previousMax;
                zoom.SetZoom(zoom.DesiredZoom);
            }
            zoom = null;
        }
    }
}
