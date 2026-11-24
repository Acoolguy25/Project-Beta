using RyanAssets.Characters.Client;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.GameHud;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// The aim point: four ticks and a dot at the centre of the screen, shown while the local player is
    /// alive and holds a weapon, with a thin ring that drains while the weapon cools down or reloads.
    /// The hit marker (<see cref="CTF_CombatFeedback"/>) sits on the same point. The ticks open with the
    /// held gun's spread (a scattergun's wide, a marksman rifle's tight), from <see cref="CTF_GunFeel.LiveStats"/>.
    /// Lives on FlagWarsHUD.
    /// </summary>
    public sealed class CTF_Crosshair : MonoBehaviour {
        public HudPopup crosshair;
        [Tooltip("Radial-filled ring around the crosshair: full right after a shot, empty when you can fire again.")]
        public Image cooldownRing;
        [Tooltip("The four ticks (up, down, left, right), each moved out from the centre along its own direction.")]
        public RectTransform[] ticks = new RectTransform[0];
        public CTF_GunFeel gunFeel;
        [Tooltip("Gap from the centre to a tick's middle with a perfectly accurate gun (pixels).")]
        [Min(0f)] public float baseGap = 10.5f;
        [Tooltip("Extra gap per degree of the gun's worst spread (pixels).")]
        [Min(0f)] public float pixelsPerSpreadDegree = 1.8f;
        [Tooltip("How fast the ticks move to a new gun's gap (per second).")]
        [Min(0.1f)] public float gapFollow = 12f;

        ToolBaseShared held;
        ToolBaseClient heldClient;
        float cooldownStart, cooldownStop;
        bool shown;
        float gap = -1f;
        Vector2[] directions;

        void OnEnable() {
            ToolBaseShared.equippedStaticEvent += OnEquipped;
            ToolBaseShared.unequippedStaticEvent += OnUnequipped;
        }

        void OnDisable() {
            ToolBaseShared.equippedStaticEvent -= OnEquipped;
            ToolBaseShared.unequippedStaticEvent -= OnUnequipped;
            Release();
        }

        void OnEquipped(ToolBaseShared tool) {
            if (tool == null || !tool.IsOwner) return;
            Release();
            held = tool;
            heldClient = tool.GetComponent<ToolBaseClient>();
            if (heldClient != null) heldClient.onCooldownChangeEvent += OnCooldown;
        }

        void OnUnequipped(ToolBaseShared tool) {
            if (tool == held) Release();
        }

        void Release() {
            if (heldClient != null) heldClient.onCooldownChangeEvent -= OnCooldown;
            held = null;
            heldClient = null;
            cooldownStop = 0f;
        }

        void OnCooldown(float start, float stop) {
            cooldownStart = start;
            cooldownStop = stop;
        }

        void Update() {
            var character = LocalPlayer.Character;
            bool show = held != null && character != null && !character.IsDead;
            if (show != shown) {
                shown = show;
                crosshair.SetVisible(show);
            }
            float left = cooldownStop - Time.time;
            float fill = show && left > 0f && cooldownStop > cooldownStart ? left / (cooldownStop - cooldownStart) : 0f;
            if (!Mathf.Approximately(cooldownRing.fillAmount, fill)) cooldownRing.fillAmount = fill;
            if (show) Spread();
        }

        void Spread() {
            if (ticks == null || ticks.Length == 0) return;
            if (directions == null || directions.Length != ticks.Length) {
                directions = new Vector2[ticks.Length];
                for (int i = 0; i < ticks.Length; i++)
                    directions[i] = ticks[i] != null && ticks[i].anchoredPosition.sqrMagnitude > 0f ? ticks[i].anchoredPosition.normalized : Vector2.up;
            }
            bool gun = held is ToolGunShared;
            float spread = gun && gunFeel != null && gunFeel.HasLiveStats ? gunFeel.LiveStats.WorstSpreadDegrees : 0f;
            float target = baseGap + spread * pixelsPerSpreadDegree;
            float next = gap < 0f ? target : Mathf.Lerp(gap, target, 1f - Mathf.Exp(-gapFollow * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - gap) < 0.01f) return;
            gap = next;
            for (int i = 0; i < ticks.Length; i++)
                if (ticks[i] != null) ticks[i].anchoredPosition = directions[i] * gap;
        }
    }
}
