using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Retention.Client;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Standing goals beside the challenges panel (J): wins, MVPs, best streak, captures and each gun's
    /// mastery, from the server's <see cref="CTF_CareerBroadcast"/>. Daily challenges run out for the
    /// day and the level track ends at 30; these never do, so a capped player always has a next number.
    /// Opens and closes with the panel.
    /// </summary>
    public sealed class CTF_CareerCard : MonoBehaviour {
        public HudPopup card;
        public TMP_Text header, rows;
        public Color doneColor = new(1f, 0.8f, 0.25f);

        ClientManager manager;
        RetentionHud retention;
        CTF_CareerBroadcast career;
        bool hasCareer;
        string doneHex;
        readonly List<CTF_Goal> goals = new();
        readonly StringBuilder text = new();

        void Awake() => doneHex = "#" + ColorUtility.ToHtmlStringRGB(doneColor);

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<CTF_CareerBroadcast>(OnCareer);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_CareerBroadcast>(OnCareer);
            }
            if (retention == null) retention = GetComponentInChildren<RetentionHud>(true);
            if (card == null) return;
            bool show = hasCareer && retention != null && retention.ChallengesOpen && !retention.Hidden;
            if (show == card.Visible) return;
            if (show) {
                Draw();
                card.Show();
            } else card.Hide();
        }

        void OnCareer(CTF_CareerBroadcast message, Channel channel) {
            career = message;
            hasCareer = true;
            if (card != null && card.Visible) Draw();
        }

        void Draw() {
            header.text = CTF_Goals.CardHeader(career);
            rows.text = CTF_Goals.CardLines(career, goals, doneHex, text);
        }
    }
}
