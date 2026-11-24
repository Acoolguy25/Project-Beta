using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.UI;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// The ability bar at the bottom of the screen: three keys (1, 2, 3) for the player's side, with
    /// recharge sweeps, running timers and locked slots that show the level that unlocks them. Keys go
    /// through the shared Tool input map (so chat and menus block them); the server decides every use
    /// and answers with <see cref="HS_AbilityStateBroadcast"/>. Pressing a running Blend In again ends it.
    /// <para>
    /// A short line above the bar carries the results of the player's own abilities ("Radar: 2 contacts
    /// nearby!", "Blinded!", a refusal), so the centre toast stays free for round messages and nothing
    /// covers what the player is looking at.
    /// </para>
    /// </summary>
    public sealed class HS_AbilityBar : MonoBehaviour {
        [SerializeField] HS_ClientController controller;
        [SerializeField] CanvasGroupController bar;
        [Tooltip("Slot views in key order.")]
        [SerializeField] HS_AbilitySlotView[] slots;
        [Tooltip("Tool hotkey (number row) of the first slot; the next slots use the following keys.")]
        [SerializeField, Range(0, 9)] int firstKey = 1;
        [SerializeField] SfxSound readySound = new();
        [Tooltip("Seconds a press waits for the server before another press of the same key is sent.")]
        [SerializeField, Min(0.05f)] float pressRetrySeconds = 0.5f;
        [Tooltip("The line above the bar for ability results.")]
        [SerializeField] TMP_Text messageLabel;
        [SerializeField, Min(0.05f)] float messageFadeSeconds = 0.3f;

        ClientManager manager;
        int round = -1, tauntAllowance = HS_Rules.TauntsPerRound;
        float messageUntil = -1f, messageShownAt;
        float[] pendingUntil;
        bool[] wasReady;
        bool visible;

        /// <summary>Whether the bar is on screen (the player has abilities this phase).</summary>
        public bool Visible => visible;

        /// <summary>Taunts the local player may make this round (from the server; more with Taunt Master).</summary>
        public int TauntAllowance => tauntAllowance;

        /// <summary>Whether the bar holds <paramref name="ability"/>, unlocked, right now.</summary>
        public bool Owns(HS_Ability ability) {
            foreach (HS_AbilitySlotView slot in slots)
                if (slot.Bound && slot.gameObject.activeSelf && slot.Slot.ability == ability) return slot.Slot.unlocked;
            return false;
        }

        /// <summary>Shows <paramref name="text"/> on the line above the bar for <paramref name="seconds"/>.</summary>
        public void Say(string text, float seconds) {
            if (messageLabel == null) return;
            messageLabel.text = text;
            messageLabel.alpha = 1f;
            messageLabel.gameObject.SetActive(true);
            messageShownAt = Time.unscaledTime;
            messageUntil = messageShownAt + Mathf.Max(0.5f, seconds);
            Punch.Scale(messageLabel.rectTransform, 0.08f);
        }

        void Awake() {
            pendingUntil = new float[slots.Length];
            wasReady = new bool[slots.Length];
            foreach (HS_AbilitySlotView slot in slots) slot.gameObject.SetActive(false);
            if (messageLabel != null) messageLabel.gameObject.SetActive(false);
            Show(false, 0f);
        }

        void OnEnable() => ToolControls.toolBarHotkeyPressed += OnHotkey;

        void OnDisable() => ToolControls.toolBarHotkeyPressed -= OnHotkey;

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<HS_AbilityStateBroadcast>(OnState);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<HS_AbilityStateBroadcast>(OnState);
            }
            bool inRound = controller.HasState && (controller.Phase is HS_Phase.Hiding or HS_Phase.Seeking) && HS_Abilities.HasBar(controller.LocalRole);
            if (!inRound && visible) Show(false);
            float now = Time.unscaledTime;
            RefreshMessage(now);
            if (!visible) return;
            for (int i = 0; i < slots.Length; i++) {
                if (!slots[i].Bound) continue;
                slots[i].Refresh(now);
                bool ready = slots[i].IsReady(now);
                if (ready && !wasReady[i] && controller.Phase == HS_Phase.Seeking) GameSfx.Play(readySound);
                wasReady[i] = ready;
            }
        }

        void RefreshMessage(float now) {
            if (messageUntil < 0f || messageLabel == null) return;
            if (!visible || now >= messageUntil + messageFadeSeconds) {
                messageUntil = -1f;
                messageLabel.gameObject.SetActive(false);
                return;
            }
            messageLabel.alpha = now <= messageUntil ? 1f : 1f - (now - messageUntil) / messageFadeSeconds;
        }

        void Show(bool show, float? seconds = null) {
            visible = show;
            if (bar != null && bar.isVisible != show) bar.TweenAlpha(show ? 1f : 0f, seconds ?? UITheme.Active.fadeDuration);
        }

        void OnState(HS_AbilityStateBroadcast message, Channel channel) {
            round = message.round;
            tauntAllowance = message.tauntAllowance > 0 ? message.tauntAllowance : HS_Rules.TauntsPerRound;
            int count = message.slots?.Length ?? 0;
            float now = Time.unscaledTime;
            for (int i = 0; i < slots.Length; i++) {
                bool used = i < count;
                slots[i].gameObject.SetActive(used);
                if (!used) continue;
                HS_AbilitySlot data = message.slots[i];
                slots[i].Bind(data, controller.AbilityIcon(data.ability), (firstKey + i).ToString(), now);
                wasReady[i] = slots[i].IsReady(now);
                pendingUntil[i] = 0f;
            }
            Show(count > 0);
        }

        void OnHotkey(int key) {
            int i = key - firstKey;
            if (i < 0 || i >= slots.Length || !visible || !slots[i].gameObject.activeSelf || manager == null) return;
            HS_AbilitySlotView slot = slots[i];
            float now = Time.unscaledTime;
            if (now < pendingUntil[i]) return;
            if (HS_Abilities.Cancels(controller.LocalRole, slot.Slot.ability, slot.ActiveLeft(now))) {
                // Ends the running blend; the server answers with the bar and the blend's end.
                pendingUntil[i] = now + pressRetrySeconds;
                manager.Broadcast(new HS_AbilityRequest { round = round, ability = slot.Slot.ability });
                return;
            }
            if (!slot.IsReady(now)) {
                // Locked or recharging: say no here; the server would refuse it anyway.
                slot.Deny();
                GameSfx.Play(SfxCue.Error);
                return;
            }
            pendingUntil[i] = now + pressRetrySeconds;
            Camera cam = Camera.main;
            Vector3 aim = cam != null ? cam.transform.forward : Vector3.zero;
            manager.Broadcast(new HS_AbilityRequest { round = round, ability = slot.Slot.ability, direction = aim });
            slot.Predict(now);
        }
    }
}
