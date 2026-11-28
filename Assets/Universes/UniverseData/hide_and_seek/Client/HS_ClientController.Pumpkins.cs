using System.Collections.Generic;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// HUD controller, the pumpkin hunt (<see cref="HS_Pumpkins"/>): a glowing jack-o'-lantern
    /// (<see cref="HS_PumpkinView"/>) for every pumpkin out, the toast when the first ones light up, and every
    /// client's side of a take: the grabber's chime and bag count, the crunch everyone hears where it happened,
    /// and for the seekers a ring and a "!" over the spot. The live pumpkins also ride on the round state, so a
    /// late joiner (or a missed message) sees the same ones as everybody else.
    /// </summary>
    public sealed partial class HS_ClientController {
        [Header("Pumpkin hunt")]
        public Sprite pumpkinIcon;
        [Tooltip("The grabber hears this in 2D; everyone else hears it in 3D where it happened (give it a long Max Distance: the risk is that seekers hear it).")]
        public SfxSound pumpkinCollectSound = new();
        [Tooltip("A seeker snuffs a pumpkin out (3D).")]
        public SfxSound pumpkinSnuffSound = new();
        [Tooltip("A pumpkin lights up (3D, quiet: it carries a room or so).")]
        public SfxSound pumpkinAppearSound = new();
        public Color pumpkinColor = new(1f, 0.55f, 0.12f, 1f);
        [Tooltip("Height of the take burst above the pumpkin's feet point (world units).")]
        public float pumpkinBurstHeight = 1.4f;
        [Min(0.1f)] public float pumpkinBurstScale = 1f;
        [Tooltip("What the seekers see where a hider grabbed one: a ring on the floor and a '!' above it, like a taunt.")]
        [Min(0.1f)] public float pumpkinRingScale = 2f;
        [Min(0.1f)] public float pumpkinMarkSeconds = 2.5f;

        readonly Dictionary<int, HS_PumpkinView> pumpkinViews = new();
        readonly List<int> pumpkinScratch = new();
        bool pumpkinsAnnounced;

        void OnPumpkin(HS_PumpkinBroadcast message, Channel channel) {
            if (message.take == HS_PumpkinTake.None) {
                if (ShowPumpkin(message.id, message.position)) {
                    GameSfx.PlayAt(pumpkinAppearSound, message.position);
                    AnnouncePumpkins();
                }
                return;
            }
            RemovePumpkin(message.id);
            // The server puts every pumpkin out at the end of a round with no taker: no fanfare for that.
            if (message.clientId < 0) return;
            Vector3 burst = message.position + Vector3.up * pumpkinBurstHeight;
            bool mine = message.clientId == MyId;
            if (message.take == HS_PumpkinTake.Snuffed) {
                GameSfx.PlayAt(pumpkinSnuffSound, message.position);
                GameFx.Play(FxKind.Smoke, burst, pumpkinColor, pumpkinBurstScale);
                return;
            }
            GameFx.Play(FxKind.CoinBurst, burst, pumpkinColor, pumpkinBurstScale);
            if (mine) {
                GameSfx.Play(pumpkinCollectSound);
                CameraShake.Kick(0.12f, 0.2f);
                return;
            }
            GameSfx.PlayAt(pumpkinCollectSound, message.position);
            // A grab gives the hider away: the seekers see where, as they would a taunt.
            if (!Seeks(MyRole)) return;
            GameFx.Play(FxKind.ShockwaveRing, message.position, pumpkinColor, pumpkinRingScale);
            FloatingText.Callout(message.position + Vector3.up * tauntMarkHeight, "!", pumpkinColor, 1f, pumpkinMarkSeconds);
        }

        /// <summary>The first pumpkins of a round: one toast saying what they are for your side.</summary>
        void AnnouncePumpkins() {
            if (pumpkinsAnnounced || !hasState || state.phase != HS_Phase.Seeking) return;
            pumpkinsAnnounced = true;
            HS_Role me = MyRole;
            if (me == HS_Role.None) return;
            Toast(me == HS_Role.Hider
                    ? "Glowing pumpkins lit up! Grab them for points, but the seekers hear it."
                    : "Glowing pumpkins lit up! Snuff them out, and listen for hiders grabbing them.",
                pumpkinIcon, null, ToastLong);
        }

        /// <summary>Adds a view for pumpkin <paramref name="id"/>; false when it is already shown (or there is no prefab).</summary>
        bool ShowPumpkin(int id, Vector3 position) {
            if (pumpkinViews.ContainsKey(id) || map == null || map.pumpkinPrefab == null) return false;
            var view = Instantiate(map.pumpkinPrefab, position, Quaternion.identity, map.transform).GetComponent<HS_PumpkinView>();
            if (view == null) {
                Debug.LogError("[HideAndSeek] The map's pumpkin prefab has no HS_PumpkinView; rebuild the client assets.");
                return false;
            }
            view.Place(id, position);
            pumpkinViews[id] = view;
            return true;
        }

        void RemovePumpkin(int id) {
            if (!pumpkinViews.Remove(id, out HS_PumpkinView view)) return;
            if (view != null) view.End();
        }

        /// <summary>Matches the views to the round state's live pumpkins.</summary>
        void SyncPumpkins(HS_Pumpkin[] live) {
            pumpkinScratch.Clear();
            foreach (int id in pumpkinViews.Keys) {
                bool found = false;
                if (live != null) foreach (HS_Pumpkin p in live) if (p.id == id) { found = true; break; }
                if (!found) pumpkinScratch.Add(id);
            }
            foreach (int id in pumpkinScratch) RemovePumpkin(id);
            if (live == null) return;
            foreach (HS_Pumpkin p in live) ShowPumpkin(p.id, p.position);
        }

        void ClearPumpkins() {
            foreach (HS_PumpkinView view in pumpkinViews.Values) if (view != null) Destroy(view.gameObject);
            pumpkinViews.Clear();
            pumpkinsAnnounced = false;
        }
    }
}
