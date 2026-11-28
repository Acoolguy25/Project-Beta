using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Medals, from <see cref="CTF_MedalBroadcast"/>: your own pop up above the crosshair one after
    /// another (icon, name, +XP, a sound and a punch, bigger for the major ones); other players' and
    /// bots' major medals (a killing spree, an MVP) go to the flag toasts so everyone hears about them.
    /// Decides nothing.
    /// </summary>
    public sealed class CTF_MedalFeed : MonoBehaviour {
        [Header("Own medals")]
        public HudPopup popup;
        public Image disc, icon;
        public TMP_Text title, xp;
        [Tooltip("Icon per medal, in CTF_Medal order (baked by the HUD authoring).")]
        public Sprite[] medalIcons = new Sprite[0];
        [Tooltip("Seconds each medal stays up; several earned at once are shown one after another.")]
        [Min(0.2f)] public float secondsEach = 1.3f;
        [Tooltip("Most medals waiting their turn; older ones are dropped (they were still paid).")]
        [Min(1)] public int maxQueued = 4;
        public Color minorColor = new(0.98f, 0.8f, 0.3f);
        public Color majorColor = new(1f, 0.45f, 0.2f);
        [Min(0f)] public float minorPunch = 0.12f;
        [Min(0f)] public float majorPunch = 0.3f;
        [Range(0f, 1f)] public float minorVolume = 0.7f;
        [Range(0f, 1f)] public float majorShake = 0.12f;

        [Header("Others' major medals")]
        public HudToastFeed toasts;
        [Min(0.5f)] public float toastSeconds = 3.5f;

        ClientManager manager;
        readonly Queue<CTF_MedalBroadcast> queue = new();
        float nextAt;

        int MyId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<CTF_MedalBroadcast>(OnMedal);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_MedalBroadcast>(OnMedal);
            }
            if (Time.unscaledTime < nextAt) return;
            if (queue.Count > 0) ShowNext();
            else if (popup != null && popup.Visible) popup.Hide();
        }

        void OnMedal(CTF_MedalBroadcast message, Channel channel) {
            var definition = CTF_MedalTracker.Get(message.medal);
            if (message.actorId == MyId) {
                while (queue.Count >= maxQueued) queue.Dequeue();
                queue.Enqueue(message);
                return;
            }
            if (!definition.announce || toasts == null) return;
            string who = $"<color={CTF_Rules.Hex(message.team)}>{CTF_Rules.PlayerLabel(message.name, false, false)}</color>";
            toasts.Push(Icon(message.medal), CTF_Rules.Color(message.team), $"{who}: <b>{definition.title}</b>", toastSeconds);
        }

        void ShowNext() {
            var message = queue.Dequeue();
            var definition = CTF_MedalTracker.Get(message.medal);
            bool major = definition.major;
            Color color = major ? majorColor : minorColor;
            icon.sprite = Icon(message.medal);
            disc.color = color;
            title.text = definition.title;
            title.color = Color.Lerp(color, Color.white, 0.35f);
            xp.text = message.xp > 0 ? $"+{message.xp} XP" : "";
            popup.Show();
            nextAt = Time.unscaledTime + secondsEach;
            Punch.Scale(popup.transform, major ? majorPunch : minorPunch, secondsEach * 0.3f);
            if (major) {
                GameSfx.Play(SfxCue.LevelUp);
                CameraShake.Kick(majorShake);
            } else {
                GameSfx.Play(SfxCue.Coin, minorVolume);
            }
        }

        Sprite Icon(CTF_Medal medal) {
            int i = (int)medal;
            return medalIcons != null && i >= 0 && i < medalIcons.Length ? medalIcons[i] : null;
        }
    }
}
