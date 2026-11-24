using System.Collections.Generic;
using FishNet.Managing.Client;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Shows every item of the night where the server says it is, and never makes one vanish: lying in the world (glowing
    /// faintly), in your own hand at the edge of the view, in a teammate's hand, or set down where it belongs. Also marks
    /// the places items go, and lights a singer's grave once their belonging rests on it.
    /// </summary>
    /// <remarks>
    /// A teammate's item follows their hand from <see cref="LateUpdate"/> instead of being parented to their rig, so it
    /// survives their character despawning (it stays where they were until the server says where it fell), keeps its own
    /// scale, and is unaffected by the controller hiding a teammate's renderers in a wardrobe.
    /// </remarks>
    public sealed class CH_ItemPresenter : MonoBehaviour {
        [Header("In your hand")]
        [SerializeField] Vector3 heldOffset = new(-0.32f, -0.34f, 0.62f);
        [SerializeField] Vector3 heldEuler = new(8f, 24f, -6f);
        [SerializeField, Range(0.1f, 2f)] float heldScale = 0.7f;
        [Tooltip("How much the held item sways behind the camera's turns.")]
        [SerializeField, Range(0f, 1f)] float heldSway = 0.35f;

        [Header("In a teammate's hand")]
        [Tooltip("From the left hand bone, in the carrier's facing.")]
        [SerializeField] Vector3 handOffset = new(0f, -0.08f, 0.12f);
        [Tooltip("In front of the body, for a rig without a humanoid hand.")]
        [SerializeField] Vector3 bodyFallbackOffset = new(-0.35f, 1.05f, 0.35f);
        [Tooltip("Seconds between attempts to find a carrier whose character has not spawned here yet.")]
        [SerializeField, Min(0.1f)] float carrierLookupInterval = 0.5f;

        [Header("Resting")]
        [Tooltip("Items are set on whatever surface is below their point within this distance.")]
        [SerializeField, Min(0f)] float groundProbe = 3f;
        [SerializeField] Color pickUpBurst = new(1f, 0.78f, 0.45f);
        [SerializeField] Color placeBurst = new(0.7f, 0.85f, 1f);

        sealed class View {
            public GameObject root;
            public CH_ItemGlow glow;
            public CH_ItemState state = CH_ItemState.Hidden;
            public int carrierId = -1;
            public Transform carrier, hand;
            public float nextCarrierLookup;
            public Vector3 restScale;
            public Vector3 position;
        }

        readonly Dictionary<int, View> views = new();
        readonly Dictionary<int, GameObject> markers = new();
        readonly Dictionary<int, GameObject> candles = new();
        readonly HashSet<int> hiddenCharacters = new();
        CH_Map map;
        ClientManager manager;
        int visibilityMask, localCharacterId = -1;
        bool heldVisible = true, glowsAllowed = true;
        Quaternion swayRotation = Quaternion.identity, lastCameraRotation = Quaternion.identity;

        public void Bind(CH_Map owner, ClientManager client) {
            map = owner;
            manager = client;
            visibilityMask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
        }

        public void ResetNight() {
            foreach (View view in views.Values) if (view.root != null) Destroy(view.root);
            foreach (GameObject marker in markers.Values) if (marker != null) Destroy(marker);
            foreach (GameObject candle in candles.Values) if (candle != null) Destroy(candle);
            views.Clear();
            markers.Clear();
            candles.Clear();
        }

        void OnDestroy() => ResetNight();

        /// <summary>The glow on a resting item, for focus and prompt placement.</summary>
        public CH_ItemGlow GlowOf(int itemId) => views.TryGetValue(itemId, out View view) && view.state == CH_ItemState.Resting ? view.glow : null;

        /// <summary>Your own held item is put away while a cutscene plays or you are hiding.</summary>
        public void SetLocalHeldVisible(bool visible) => heldVisible = visible;

        /// <summary>Resting items lose their outline while a cutscene plays: the scene is not a search.</summary>
        public void SetGlowsAllowed(bool allowed) {
            if (glowsAllowed == allowed) return;
            glowsAllowed = allowed;
            foreach (View view in views.Values)
                if (view.glow != null && view.state == CH_ItemState.Resting) view.glow.enabled = allowed;
        }

        /// <summary>Teammates inside a hiding place: what they carry is inside with them.</summary>
        public void SetHiddenCharacters(HashSet<int> characterIds) {
            hiddenCharacters.Clear();
            hiddenCharacters.UnionWith(characterIds);
        }

        public void Apply(CH_StateBroadcast state, int localId, CH_ItemKind? localCarried, bool fresh) {
            localCharacterId = localId;
            foreach (CH_ItemView item in state.items) ApplyItem(item, fresh);
            foreach (CH_SlotView slot in state.slots) ApplySlot(slot, state, localCarried);
        }

        void ApplyItem(CH_ItemView item, bool fresh) {
            if (!views.TryGetValue(item.id, out View view)) {
                if (item.state == CH_ItemState.Hidden) return;
                view = Create(item);
                views.Add(item.id, view);
            } else if (view.root == null) {
                // Destroyed from outside (a scene change): build it again and re-apply its state.
                Debug.LogWarning($"[Drowned Choir] item {item.id} ({item.kind}) lost its view; rebuilding it.");
                views[item.id] = view = Create(item);
            }
            bool changed = view.state != item.state || view.carrierId != item.carrierId
                || (item.state == CH_ItemState.Resting && (item.position - view.position).sqrMagnitude > 0.01f);
            if (!changed) return;
            view.position = item.position;
            CH_ItemState previous = view.state;
            view.state = item.state;
            view.carrierId = item.carrierId;
            view.carrier = view.hand = null;
            view.nextCarrierLookup = 0f;
            GameObject root = view.root;
            root.SetActive(item.state != CH_ItemState.Hidden);
            switch (item.state) {
                case CH_ItemState.Resting:
                    root.transform.SetParent(map.transform, true);
                    root.transform.SetPositionAndRotation(GroundBelow(item.position), Quaternion.Euler(0f, item.yaw, 0f));
                    root.transform.localScale = view.restScale;
                    SetShadows(root, true);
                    if (view.glow != null) view.glow.enabled = glowsAllowed;
                    break;
                case CH_ItemState.Carried:
                    if (view.glow != null) { view.glow.SetFocused(false); view.glow.enabled = false; }
                    if (item.carrierId == localCharacterId) {
                        // Attached to the view now, or by LateUpdate once there is a camera.
                        if (Camera.main != null) HoldInView(view, Camera.main);
                        SetShadows(root, false);
                    } else {
                        // Stays where it was until LateUpdate finds the carrier's hand.
                        root.transform.SetParent(map.transform, true);
                        root.transform.localScale = view.restScale * heldScale;
                        ResolveCarrier(view);
                        FollowCarrier(view);
                        SetShadows(root, true);
                    }
                    if (!fresh && previous == CH_ItemState.Resting) GameFx.Play(FxKind.Sparks, root.transform.position, pickUpBurst, 0.35f);
                    break;
                case CH_ItemState.Placed:
                    root.transform.SetParent(map.transform, true);
                    // Exactly where the slot is authored: in the boat, on the grave, on Lily's step.
                    root.transform.SetPositionAndRotation(item.position, Quaternion.Euler(0f, item.yaw, 0f));
                    root.transform.localScale = view.restScale;
                    SetShadows(root, true);
                    if (view.glow != null) { view.glow.SetFocused(false); view.glow.enabled = false; }
                    if (!fresh && previous != CH_ItemState.Placed) GameFx.Play(FxKind.Sparks, root.transform.position + Vector3.up * 0.3f, placeBurst, 0.6f);
                    break;
            }
        }

        View Create(CH_ItemView item) {
            int kind = (int)item.kind;
            GameObject prefab = kind < map.itemPrefabs.Length ? map.itemPrefabs[kind] : null;
            if (prefab == null) {
                Debug.LogError($"[Drowned Choir] no item prefab for {item.kind}; assign CH_Map.itemPrefabs.");
                prefab = new GameObject(item.kind.ToString());
            }
            GameObject root = Instantiate(prefab, map.transform);
            root.name = $"{item.title} (item {item.id})";
            return new View { root = root, glow = root.GetComponent<CH_ItemGlow>(), restScale = root.transform.localScale };
        }

        void ApplySlot(CH_SlotView slot, CH_StateBroadcast state, CH_ItemKind? localCarried) {
            // Where a part goes is always marked in its act; a grave only once you hold a relic; Lily's marker only for her music box.
            bool show = slot.open && slot.kind switch {
                CH_SlotKind.Grave => localCarried is CH_ItemKind.Hymnal or CH_ItemKind.Photograph or CH_ItemKind.Candle,
                CH_SlotKind.LilyMarker => localCarried == CH_ItemKind.MusicBox,
                _ => true
            };
            if (show && !markers.ContainsKey(slot.id) && map.slotMarkerPrefab != null) {
                GameObject marker = Instantiate(map.slotMarkerPrefab, map.transform);
                marker.name = $"Marker: {slot.title}";
                // Aboard, the marker sits on the boards as authored: a ray from above would stop on the gunwale or pass under the hull.
                Vector3 at = CH_Night.IsAboard(slot.kind) ? slot.position : GroundBelow(slot.position + Vector3.up * 0.5f);
                marker.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, slot.yaw, 0f));
                markers.Add(slot.id, marker);
            }
            if (markers.TryGetValue(slot.id, out GameObject existing) && existing.activeSelf != show) existing.SetActive(show);
            if (slot.kind == CH_SlotKind.Grave && slot.filled && !candles.ContainsKey(slot.id) && map.graveCandlesPrefab != null) {
                GameObject lit = Instantiate(map.graveCandlesPrefab, map.transform);
                lit.transform.SetPositionAndRotation(GroundBelow(slot.position + Vector3.up * 0.5f), Quaternion.Euler(0f, slot.yaw, 0f));
                candles.Add(slot.id, lit);
            }
        }

        void HoldInView(View view, Camera camera) {
            Transform root = view.root.transform;
            root.SetParent(camera.transform, false);
            root.localPosition = heldOffset;
            root.localRotation = Quaternion.Euler(heldEuler);
            root.localScale = view.restScale * heldScale;
        }

        /// <summary>Finds the carrier's character and, on a humanoid rig, its left hand; retried while it has not spawned here.</summary>
        void ResolveCarrier(View view) {
            view.nextCarrierLookup = Time.unscaledTime + carrierLookupInterval;
            if (manager == null || !manager.Objects.Spawned.TryGetValue(view.carrierId, out var character) || character == null) return;
            view.carrier = character.transform;
            Animator animator = character.GetComponentInChildren<Animator>();
            view.hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        }

        /// <summary>Upright in the carrier's facing, at their hand (or in front of them); scale-free, unlike a bone parent.</summary>
        void FollowCarrier(View view) {
            if (view.carrier == null) return;
            Quaternion facing = Quaternion.Euler(0f, view.carrier.eulerAngles.y, 0f);
            Vector3 position = view.hand != null ? view.hand.position + facing * handOffset : view.carrier.position + facing * bodyFallbackOffset;
            view.root.transform.SetPositionAndRotation(position, facing);
        }

        void LateUpdate() {
            Camera camera = Camera.main;
            if (camera != null) {
                // Your held item lags a little behind the view, so it feels like it is in a hand.
                Quaternion target = Quaternion.Inverse(camera.transform.rotation) * lastCameraRotation;
                swayRotation = Quaternion.Slerp(swayRotation, Quaternion.Slerp(Quaternion.identity, target, heldSway), 12f * Time.deltaTime);
                lastCameraRotation = Quaternion.Slerp(lastCameraRotation, camera.transform.rotation, 10f * Time.deltaTime);
            }
            foreach (View view in views.Values) {
                if (view.state != CH_ItemState.Carried || view.root == null) continue;
                if (view.carrierId == localCharacterId) {
                    if (view.root.activeSelf != heldVisible) view.root.SetActive(heldVisible);
                    if (camera == null) continue;
                    if (view.root.transform.parent != camera.transform) HoldInView(view, camera);
                    view.root.transform.localRotation = swayRotation * Quaternion.Euler(heldEuler);
                    continue;
                }
                bool inside = hiddenCharacters.Contains(view.carrierId);
                if (view.root.activeSelf == inside) view.root.SetActive(!inside);
                if (inside) continue;
                if (view.carrier == null && Time.unscaledTime >= view.nextCarrierLookup) ResolveCarrier(view);
                FollowCarrier(view);
            }
        }

        Vector3 GroundBelow(Vector3 point) =>
            Physics.Raycast(point + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, groundProbe, visibilityMask, QueryTriggerInteraction.Ignore) ? hit.point : point;

        static void SetShadows(GameObject root, bool cast) {
            foreach (Renderer part in root.GetComponentsInChildren<Renderer>(true))
                part.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
