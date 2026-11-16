using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// The manor's background sound: wind that is loud in the yards and muffled indoors, a grandfather
    /// clock ticking in the foyer, and now and then a floorboard or door creaking somewhere near you
    /// while you are inside.
    /// </summary>
    public sealed class HS_Ambience : MonoBehaviour {
        [Header("Wind (2D loop)")]
        [SerializeField] AudioSource wind;
        [SerializeField, Range(0f, 1f)] float windOutdoors = 0.5f;
        [SerializeField, Range(0f, 1f)] float windIndoors = 0.12f;
        [Tooltip("Seconds to fade between the indoor and outdoor wind level.")]
        [SerializeField, Min(0.05f)] float windFadeSeconds = 1.5f;

        [Header("Clock (3D loop, moved to HS_Map.clockPosition)")]
        [SerializeField] AudioSource clock;

        [Header("Creaks (indoors only)")]
        [SerializeField] SfxSound creaks = new();
        [Tooltip("Seconds between creaks: a random value in this range.")]
        [SerializeField] Vector2 creakInterval = new(6f, 16f);
        [Tooltip("How far from you a creak happens (world units): a random value in this range.")]
        [SerializeField] Vector2 creakDistance = new(8f, 28f);

        [Tooltip("How often to check whether you are indoors (seconds).")]
        [SerializeField, Min(0.05f)] float areaCheckInterval = 0.5f;

        HS_Map map;
        bool indoors;
        float nextAreaCheck, nextCreak;

        void Start() {
            map = GetComponentInParent<HS_Map>();
            // The HUD canvas moves and scales with the screen; the clock belongs to the house.
            clock.transform.SetParent(map.transform, false);
            clock.transform.position = map.clockPosition;
            clock.Play();
            wind.volume = windOutdoors;
            wind.Play();
            ScheduleCreak();
        }

        void OnDestroy() {
            if (clock != null) Destroy(clock.gameObject);
        }

        void Update() {
            Vector3? listener = ListenerPosition();
            if (Time.unscaledTime >= nextAreaCheck) {
                nextAreaCheck = Time.unscaledTime + areaCheckInterval;
                indoors = listener.HasValue && map.IsIndoors(listener.Value);
            }
            wind.volume = Mathf.MoveTowards(wind.volume, indoors ? windIndoors : windOutdoors, Mathf.Abs(windOutdoors - windIndoors) * Time.deltaTime / windFadeSeconds);
            if (Time.unscaledTime < nextCreak) return;
            ScheduleCreak();
            if (indoors && listener.HasValue) {
                Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(creakDistance.x, creakDistance.y);
                GameSfx.PlayAt(creaks, listener.Value + new Vector3(dir.x, 0f, dir.y));
            }
        }

        void ScheduleCreak() => nextCreak = Time.unscaledTime + Random.Range(creakInterval.x, creakInterval.y);

        static Vector3? ListenerPosition() {
            if (LocalPlayer.Character != null) return LocalPlayer.Character.transform.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : null;
        }
    }
}
