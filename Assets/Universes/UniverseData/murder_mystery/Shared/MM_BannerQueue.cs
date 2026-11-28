using System.Collections.Generic;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// The order banners are shown in. A banner stays at least <see cref="MinSeconds"/> before a newer one
    /// replaces it, so two events a second apart (the sheriff's mistake, then "no players left") are both read;
    /// newer ones wait their turn, and one that waited longer than <see cref="MaxWaitSeconds"/> is dropped because
    /// its moment has passed. Pure logic (the caller passes the clock and whether the banner is on screen), so the
    /// rules are unit-tested; <c>MM_ClientHUD</c> draws what it says.
    /// </summary>
    public sealed class MM_BannerQueue<T> {
        readonly Queue<(T item, float queuedAt)> pending = new();
        float shownAt = float.NegativeInfinity;

        public float MinSeconds { get; set; }
        public float MaxWaitSeconds { get; set; }
        public int Waiting => pending.Count;

        public MM_BannerQueue(float minSeconds, float maxWaitSeconds) {
            MinSeconds = minSeconds;
            MaxWaitSeconds = maxWaitSeconds;
        }

        /// <summary>
        /// A new banner. Returns true when it should be shown now; otherwise it waits behind the one on screen
        /// (or behind others already waiting, so the order is kept).
        /// </summary>
        public bool Offer(T item, float now, bool bannerVisible) {
            if (pending.Count > 0 || Held(now, bannerVisible)) {
                pending.Enqueue((item, now));
                return false;
            }
            shownAt = now;
            return true;
        }

        /// <summary>Per frame: the waiting banner to show now, if the one on screen has had its time.</summary>
        public bool TryNext(float now, bool bannerVisible, out T item) {
            while (pending.Count > 0 && !Held(now, bannerVisible)) {
                (T next, float queuedAt) = pending.Dequeue();
                if (now - queuedAt > MaxWaitSeconds)
                    continue;
                shownAt = now;
                item = next;
                return true;
            }
            item = default;
            return false;
        }

        /// <summary>Forgets every waiting banner (a new round, the results).</summary>
        public void Clear() => pending.Clear();

        bool Held(float now, bool bannerVisible) => bannerVisible && now - shownAt < MinSeconds;
    }
}
