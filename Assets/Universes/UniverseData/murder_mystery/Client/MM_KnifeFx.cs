using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Object;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Universes.UniverseData.murder_mystery.Client {
    /// <summary>
    /// Draws thrown knives and paints murderers' knives in their skins. The server simulates every throw;
    /// this moves an authored knife model along the announced line at the announced speed, sticks it where
    /// the server says it landed (a wall, or the victim, who the kill effects cover), and fades it out.
    /// Lives on the MM_HUD prefab next to <see cref="MM_ClientHUD"/>.
    /// </summary>
    public sealed class MM_KnifeFx : MonoBehaviour {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Skin broadcasts can arrive before the knife they name is spawned on this client; retry this often.
        const float PendingSkinRetrySeconds = 0.5f;

        [Tooltip("The knife that flies: a model pointing along +Z with a TrailRenderer.")]
        public GameObject thrownKnifePrefab;
        [Tooltip("A knife that hit a wall stays stuck in it this long before it is put away.")]
        [Min(0f)] public float stuckSeconds = 3f;
        [Tooltip("How far a wall-stuck knife sits back from the hit point along its flight, so the blade shows instead of sinking into the wall (metres; about half the knife's length).")]
        [Min(0f)] public float wallStickBackOff = 0.7f;
        [Tooltip("A knife that hit someone disappears this soon (the kill effects take over).")]
        [Min(0f)] public float hitHideSeconds = 0.05f;
        [Tooltip("A knife the server has not landed after its full range is put away after this grace.")]
        [Min(0f)] public float lostKnifeSeconds = 0.6f;
        [Tooltip("Spin of a flying knife around its flight line (degrees per second).")]
        public float rollDegreesPerSecond = 720f;
        [Header("Sounds")]
        [Range(0f, 1f)] public float throwVolume = 0.9f;
        [Range(0f, 1f)] public float thudVolume = 0.8f;
        [Tooltip("Dust where a knife sinks into a wall.")]
        [Min(0f)] public float thudDustScale = 0.6f;

        sealed class Flight {
            public GameObject view;
            public Vector3 origin, direction;
            public float speed, maxDistance, startedAt, removeAt;
            public bool landed;
        }

        ClientManager manager;
        readonly Dictionary<int, Flight> flights = new();
        readonly List<int> finished = new();
        readonly Stack<GameObject> pool = new();
        readonly Dictionary<int, byte> skins = new();
        readonly HashSet<int> pendingSkins = new();
        readonly List<int> appliedSkins = new();
        MaterialPropertyBlock block;
        float nextSkinRetry;

        void Awake() => block = new MaterialPropertyBlock();

        void OnDestroy() {
            if (manager != null) {
                manager.UnregisterBroadcast<MM_KnifeThrownBroadcast>(OnThrown);
                manager.UnregisterBroadcast<MM_KnifeLandedBroadcast>(OnLanded);
                manager.UnregisterBroadcast<MM_KnifeSkinsBroadcast>(OnSkins);
            }
            foreach (Flight flight in flights.Values)
                if (flight.view != null) Destroy(flight.view);
            foreach (GameObject view in pool)
                if (view != null) Destroy(view);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<MM_KnifeThrownBroadcast>(OnThrown);
                manager.RegisterBroadcast<MM_KnifeLandedBroadcast>(OnLanded);
                manager.RegisterBroadcast<MM_KnifeSkinsBroadcast>(OnSkins);
            }
            MoveFlights();
            if (pendingSkins.Count > 0 && Time.unscaledTime >= nextSkinRetry) {
                nextSkinRetry = Time.unscaledTime + PendingSkinRetrySeconds;
                ApplyPendingSkins();
            }
        }

        // ---------- thrown knives ----------

        void OnThrown(MM_KnifeThrownBroadcast message, Channel channel) {
            if (thrownKnifePrefab == null)
                return;
            GameObject view = pool.Count > 0 ? pool.Pop() : CreateView();
            view.transform.SetPositionAndRotation(message.origin, Quaternion.LookRotation(message.direction));
            Paint(view, message.skin, includeTrail: true);
            ResetTrail(view);
            view.SetActive(true);
            flights[message.throwId] = new Flight {
                view = view,
                origin = message.origin,
                direction = message.direction,
                speed = message.speed,
                maxDistance = message.maxDistance,
                startedAt = Time.time,
            };
            GameSfx.PlayAt(SfxCue.Whoosh, message.origin, throwVolume, GameSfx.Semitones(3f));
        }

        void OnLanded(MM_KnifeLandedBroadcast message, Channel channel) {
            if (!flights.TryGetValue(message.throwId, out Flight flight))
                return;
            flight.landed = true;
            if (message.hitCharacter) {
                flight.view.transform.position = message.position;
                flight.removeAt = Time.time + hitHideSeconds;
                return;
            }
            // The view is centred on the flight line, so only the tip goes into the wall.
            flight.view.transform.position = message.position - flight.direction * wallStickBackOff;
            flight.removeAt = Time.time + stuckSeconds;
            GameSfx.PlayAt(SfxCue.Hit, message.position, thudVolume, GameSfx.Semitones(-4f));
            if (thudDustScale > 0f)
                GameFx.Play(FxKind.DustPuff, message.position, thudDustScale);
        }

        void MoveFlights() {
            float now = Time.time;
            foreach (KeyValuePair<int, Flight> entry in flights) {
                Flight flight = entry.Value;
                if (flight.landed) {
                    if (now >= flight.removeAt)
                        finished.Add(entry.Key);
                    continue;
                }
                float distance = flight.speed * (now - flight.startedAt);
                if (distance >= flight.maxDistance) {
                    distance = flight.maxDistance;
                    if (now - flight.startedAt >= flight.maxDistance / Mathf.Max(0.01f, flight.speed) + lostKnifeSeconds)
                        finished.Add(entry.Key);
                }
                Transform view = flight.view.transform;
                view.position = flight.origin + flight.direction * distance;
                view.rotation = Quaternion.LookRotation(flight.direction) * Quaternion.Euler(0f, 0f, rollDegreesPerSecond * (now - flight.startedAt));
            }
            foreach (int id in finished) {
                Flight flight = flights[id];
                flights.Remove(id);
                flight.view.SetActive(false);
                pool.Push(flight.view);
            }
            finished.Clear();
        }

        GameObject CreateView() {
            GameObject view = Instantiate(thrownKnifePrefab);
            SceneManager.MoveGameObjectToScene(view, gameObject.scene);
            return view;
        }

        static void ResetTrail(GameObject view) {
            foreach (TrailRenderer trail in view.GetComponentsInChildren<TrailRenderer>(true))
                trail.Clear();
        }

        // ---------- skins ----------

        void OnSkins(MM_KnifeSkinsBroadcast message, Channel channel) {
            if (message.toolObjectIds == null || message.skins == null)
                return;
            int count = Mathf.Min(message.toolObjectIds.Length, message.skins.Length);
            for (int i = 0; i < count; i++) {
                skins[message.toolObjectIds[i]] = message.skins[i];
                pendingSkins.Add(message.toolObjectIds[i]);
            }
            ApplyPendingSkins();
        }

        void ApplyPendingSkins() {
            if (manager == null)
                return;
            foreach (int id in pendingSkins) {
                if (!manager.Objects.Spawned.TryGetValue(id, out NetworkObject tool) || tool == null)
                    continue;
                Paint(tool.gameObject, skins[id], includeTrail: false);
                appliedSkins.Add(id);
            }
            foreach (int id in appliedSkins)
                pendingSkins.Remove(id);
            appliedSkins.Clear();
        }

        /// <summary>Tints the knife's blade (and its trail, for a thrown one) in a skin's colours.</summary>
        void Paint(GameObject knife, byte skinIndex, bool includeTrail) {
            MM_KnifeSkins.Skin skin = MM_KnifeSkins.Get(skinIndex);
            foreach (MeshRenderer renderer in knife.GetComponentsInChildren<MeshRenderer>(true)) {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, skin.tint);
                renderer.SetPropertyBlock(block);
            }
            if (!includeTrail)
                return;
            Color glow = skinIndex == 0 ? new Color(1f, 1f, 1f, 0.55f) : Color.Lerp(skin.tint, Color.white, 0.15f);
            float intensity = 1f + skin.glow;
            foreach (TrailRenderer trail in knife.GetComponentsInChildren<TrailRenderer>(true)) {
                trail.startColor = new Color(glow.r * intensity, glow.g * intensity, glow.b * intensity, glow.a);
                trail.endColor = new Color(glow.r, glow.g, glow.b, 0f);
            }
        }
    }
}
