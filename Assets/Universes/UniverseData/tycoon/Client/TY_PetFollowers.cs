using RyanAssets.GameFeel;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// Draws every tycoon owner's best pet trotting beside them (pets are cosmetic here: the server counts
    /// their income bonus). One follower per plot, rebuilt when the owner's best pet changes; it hops while
    /// its owner moves, idles with a bob, and an Epic or Legendary pet leaves a glowing trail in its rarity's
    /// colour. Added and fed by <see cref="TY_ClientController"/>.
    /// </summary>
    public sealed class TY_PetFollowers : MonoBehaviour {
        [Tooltip("Where the pet walks, relative to its owner: x to the side, z behind (negative).")]
        public Vector2 offset = new(1.5f, -1.7f);
        [Tooltip("Seconds the pet takes to catch up.")]
        [Min(0.01f)] public float followSeconds = 0.22f;
        [Tooltip("Height and speed of the hops while moving.")]
        [Min(0f)] public float hopHeight = 0.32f, hopsPerMetre = 0.75f;
        [Tooltip("Snap to the owner when further than this (a respawn or teleport).")]
        [Min(1f)] public float snapDistance = 18f;
        [Tooltip("Size of a level-1 pet, and the size it gains per level.")]
        [Min(0.1f)] public float baseScale = 1.8f;
        [Min(0f)] public float scalePerLevel = 0.1f;
        [Min(0f)] public float trailScale = 0.7f;

        sealed class Follower {
            public int pet = -1, level, ownerId = -2;
            public Transform t;
            public Vector3 velocity;
            public float hop;
            public FxHandle trail;
        }

        TY_Map map;
        TY_Feedback feedback;
        Follower[] followers;

        public void Setup(TY_Map map, TY_Feedback feedback) {
            this.map = map;
            this.feedback = feedback;
            followers = new Follower[map.PlotCount];
            for (int i = 0; i < followers.Length; i++) followers[i] = new Follower();
        }

        /// <summary>Shows plot <paramref name="plot"/>'s owner with <paramref name="pet"/> (-1: none).</summary>
        /// <param name="live">Pop the pet in with a puff (false while drawing the first state after joining).</param>
        public void Set(int plot, int ownerId, int pet, int level, bool live) {
            if (followers == null || plot < 0 || plot >= followers.Length) return;
            Follower f = followers[plot];
            if (ownerId < 0) pet = -1;
            bool changed = pet != f.pet || ownerId != f.ownerId;
            bool levelledUp = !changed && level > f.level;
            if (changed) {
                Clear(f);
                f.pet = pet;
                f.ownerId = ownerId;
                GameObject prefab = map.PetPrefab(pet);
                if (prefab != null) {
                    f.t = Instantiate(prefab, transform).transform;
                    f.t.name = $"Pet {TY_Pets.All[pet].name}";
                    Place(f, snap: true);
                    if (TY_Pets.All[pet].rarity >= TY_Rarity.Epic)
                        f.trail = GameFx.Attach(FxKind.Trail, f.t, Vector3.up * 0.4f, TY_Pets.RarityColor(TY_Pets.All[pet].rarity), trailScale);
                }
            }
            if (f.t == null) { f.level = level; return; }
            // Size first: the punches below spring back to the scale they find.
            if (changed || level != f.level) f.t.localScale = Vector3.one * (baseScale + scalePerLevel * Mathf.Max(0, level - 1));
            if (changed && live && f.t.gameObject.activeSelf) feedback.PetAppeared(f.t, TY_Pets.All[pet].rarity);
            else if (levelledUp) Punch.Scale(f.t, 0.35f, 0.4f);
            f.level = level;
        }

        void LateUpdate() {
            if (followers == null) return;
            foreach (Follower f in followers) if (f.t != null) Place(f, snap: false);
        }

        void Place(Follower f, bool snap) {
            var owner = CharacterLookup.ByClientId(f.ownerId);
            bool show = owner != null && !owner.IsDead;
            if (f.t.gameObject.activeSelf != show) { f.t.gameObject.SetActive(show); snap = true; }
            if (!show) return;
            Transform o = owner.transform;
            Vector3 flatForward = Vector3.ProjectOnPlane(o.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flatForward);
            Vector3 target = o.position + right * offset.x + flatForward * offset.y;
            Vector3 ground = new(f.t.position.x, target.y, f.t.position.z);
            if (snap || (ground - target).sqrMagnitude > snapDistance * snapDistance) {
                ground = target;
                f.velocity = Vector3.zero;
            } else {
                ground = Vector3.SmoothDamp(ground, target, ref f.velocity, followSeconds);
            }
            float speed = new Vector2(f.velocity.x, f.velocity.z).magnitude;
            float lift;
            if (speed > 0.6f) {
                f.hop += speed * hopsPerMetre * Time.deltaTime;
                lift = Mathf.Abs(Mathf.Sin(f.hop * Mathf.PI)) * hopHeight;
            } else {
                f.hop = 0f;
                lift = (Mathf.Sin(Time.time * 2.4f + f.ownerId) * 0.5f + 0.5f) * 0.06f;
            }
            f.t.position = ground + Vector3.up * lift;
            Vector3 face = speed > 0.6f ? new Vector3(f.velocity.x, 0f, f.velocity.z) : flatForward;
            if (face.sqrMagnitude > 0.0001f)
                f.t.rotation = snap ? Quaternion.LookRotation(face) : Quaternion.Slerp(f.t.rotation, Quaternion.LookRotation(face), 10f * Time.deltaTime);
        }

        void Clear(Follower f) {
            f.trail.Stop(true);
            f.trail = default;
            if (f.t != null) Destroy(f.t.gameObject);
            f.t = null;
            f.level = 0;
        }

        void OnDestroy() {
            if (followers != null) foreach (Follower f in followers) f.trail.Stop(true);
        }
    }
}
