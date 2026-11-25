using RyanAssets.GameFeel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// One key of the ability bar: icon, key cap, name, a radial recharge sweep with the seconds left, a
    /// ring while the ability is running, and a lock with the unlock level until the player reaches it.
    /// Built by <c>HS_HudAuthoring</c>; <see cref="HS_AbilityBar"/> feeds it.
    /// </summary>
    public sealed class HS_AbilitySlotView : MonoBehaviour {
        [SerializeField] Image icon;
        [Tooltip("Radial overlay that empties as the ability recharges.")]
        [SerializeField] Image cooldownFill;
        [Tooltip("Shown while the ability is running; its fill empties with the time left.")]
        [SerializeField] Image activeRing;
        [SerializeField] TMP_Text keyLabel, nameLabel, timerLabel;
        [Tooltip("Covers the slot until the ability is unlocked: a lock and the level that unlocks it.")]
        [SerializeField] GameObject lockOverlay;
        [SerializeField] TMP_Text lockLabel;
        [Tooltip("Scaled for the 'ready' and 'not yet' punches.")]
        [SerializeField] RectTransform punchTarget;

        [Header("Look")]
        [Tooltip("The ability's icon under the lock: dim, but still readable, so the player sees what is coming.")]
        [SerializeField, Range(0f, 1f)] float lockedIconAlpha = 0.45f;
        [Tooltip("Dim while recharging, so the seconds on top read clearly.")]
        [SerializeField, Range(0f, 1f)] float rechargingIconAlpha = 0.22f;
        [SerializeField, Range(0f, 0.5f)] float readyPunch = 0.18f;
        [SerializeField, Range(0f, 0.5f)] float deniedPunch = 0.12f;

        HS_AbilitySlot slot;
        float boundAt;
        int shownSeconds = -1;
        bool wasReady = true, bound;
        Color iconColor = Color.white;

        public HS_AbilitySlot Slot => slot;
        public bool Bound => bound;

        /// <summary>Shows <paramref name="data"/>, as received at <paramref name="receivedAt"/> (unscaled time).</summary>
        public void Bind(HS_AbilitySlot data, Sprite sprite, string key, float receivedAt) {
            bool sameAbility = bound && slot.ability == data.ability;
            slot = data;
            boundAt = receivedAt;
            bound = true;
            if (sprite != null) icon.sprite = sprite;
            keyLabel.text = key;
            HS_AbilityInfo info = HS_Abilities.Info(data.ability);
            nameLabel.text = data.upgraded ? info.Name + "+" : info.Name;
            lockOverlay.SetActive(!data.unlocked);
            if (!data.unlocked) lockLabel.text = $"LV {data.unlockLevel}";
            shownSeconds = -1;
            // A new ability in the slot starts out quietly; the same one keeps its ready state so a refresh does not punch.
            if (!sameAbility) wasReady = ReadyIn(receivedAt) <= 0f;
            Refresh(receivedAt);
        }

        public float ReadyIn(float now) => Mathf.Max(0f, slot.readyIn - (now - boundAt));
        public float ActiveLeft(float now) => Mathf.Max(0f, slot.activeLeft - (now - boundAt));
        public bool IsReady(float now) => bound && slot.unlocked && ReadyIn(now) <= 0f;

        /// <summary>Called every frame by the bar: sweeps the recharge, counts seconds and punches when ready.</summary>
        public void Refresh(float now) {
            if (!bound) return;
            float readyIn = ReadyIn(now), activeLeft = ActiveLeft(now);
            bool ready = slot.unlocked && readyIn <= 0f;
            float fill = slot.unlocked && slot.cooldown > 0f ? Mathf.Clamp01(readyIn / slot.cooldown) : 0f;
            if (cooldownFill.fillAmount != fill) cooldownFill.fillAmount = fill;
            bool active = activeLeft > 0f && slot.duration > 0f;
            if (activeRing.gameObject.activeSelf != active) activeRing.gameObject.SetActive(active);
            if (active) activeRing.fillAmount = activeLeft / slot.duration;
            iconColor.a = !slot.unlocked ? lockedIconAlpha : ready || active ? 1f : rechargingIconAlpha;
            if (icon.color != iconColor) icon.color = iconColor;
            int seconds = slot.unlocked && !ready && !active ? Mathf.CeilToInt(readyIn) : 0;
            if (seconds != shownSeconds) {
                shownSeconds = seconds;
                timerLabel.text = seconds > 0 ? seconds.ToString() : "";
            }
            if (ready && !wasReady) Punch.Scale(punchTarget, readyPunch);
            wasReady = ready;
        }

        /// <summary>Starts the recharge locally the moment the player presses (the server's answer then corrects it).</summary>
        public void Predict(float now) {
            if (!bound) return;
            slot.readyIn = slot.cooldown + (now - boundAt);
            slot.activeLeft = slot.duration + (now - boundAt);
            wasReady = false;
        }

        public void Deny() => Punch.Squash(punchTarget, deniedPunch);
    }
}
