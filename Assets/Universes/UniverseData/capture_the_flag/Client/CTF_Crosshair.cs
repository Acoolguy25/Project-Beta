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
    /// The hit marker (<see cref="CTF_CombatFeedback"/>) sits on the same point. Lives on FlagWarsHUD.
    /// </summary>
    public sealed class CTF_Crosshair : MonoBehaviour {
        public HudPopup crosshair;
        [Tooltip("Radial-filled ring around the crosshair: full right after a shot, empty when you can fire again.")]
        public Image cooldownRing;

        ToolBaseShared held;
        ToolBaseClient heldClient;
        float cooldownStart, cooldownStop;
        bool shown;

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
        }
    }
}
