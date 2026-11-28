using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.UI;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// The full board: both teams, bots and players, with flags, eliminations and deaths (from the
    /// roster, <see cref="CTF_RosterBroadcast"/>). It takes the shared player list's place: the list
    /// only knows players, so in a match of one player and seven bots it showed one row. Tab (or the
    /// top bar's players button) opens this board instead; it also opens by itself at the result.
    /// </summary>
    public sealed class CTF_RosterBoard : MonoBehaviour {
        public HudPopup board;
        public TMP_Text redHeader, blueHeader, redRows, blueRows;
        [Min(1)] public int maxRows = 6;
        public Color highlight = new(1f, 0.85f, 0.3f);

        ClientManager manager;
        CTF_RosterBroadcast roster;
        bool hasRoster, autoShown;
        CTF_Phase phase;
        int redScore, blueScore;
        readonly List<CTF_BotView> rows = new();
        readonly StringBuilder text = new();

        void OnEnable() => HudPanel.VisibilityChanged += OnPanel;
        void OnDisable() => HudPanel.VisibilityChanged -= OnPanel;

        void OnDestroy() {
            if (manager == null) return;
            manager.UnregisterBroadcast<CTF_RosterBroadcast>(OnRoster);
            manager.UnregisterBroadcast<CTF_StateBroadcast>(OnState);
        }

        public bool IsOpen => board != null && board.Visible;

        int MyId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_RosterBroadcast>(OnRoster);
                manager.RegisterBroadcast<CTF_StateBroadcast>(OnState);
            }
            // The shared list opens with the HUD: this board stands in for it here.
            if (hasRoster && HudPanel.IsVisible(HudPanelId.PlayerList)) HudPanel.Hide(HudPanelId.PlayerList, true);
        }

        /// <summary>Tab or the players button toggled the shared list: toggle this board instead.</summary>
        void OnPanel(HudPanelId id, bool visible) {
            if (id != HudPanelId.PlayerList || !visible || !hasRoster) return;
            HudPanel.Hide(HudPanelId.PlayerList, true);
            autoShown = false;
            if (board.Visible) board.Hide();
            else Show();
        }

        void OnRoster(CTF_RosterBroadcast message, Channel channel) {
            roster = message;
            hasRoster = true;
            if (board.Visible) Draw();
        }

        void OnState(CTF_StateBroadcast message, Channel channel) {
            bool scoreChanged = message.redScore != redScore || message.blueScore != blueScore;
            redScore = message.redScore;
            blueScore = message.blueScore;
            var was = phase;
            phase = message.phase;
            if (phase == CTF_Phase.Results && was != CTF_Phase.Results && hasRoster) {
                // The result: everyone's line, beside the banner and the summary card.
                autoShown = true;
                Show();
            } else if (phase != CTF_Phase.Results && was == CTF_Phase.Results && autoShown) {
                autoShown = false;
                board.Hide();
            } else if (scoreChanged && board.Visible) Draw();
        }

        void Show() {
            Draw();
            board.Show();
        }

        void Draw() {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(highlight);
            redHeader.text = $"<color={CTF_Rules.Hex(CTF_Team.Red)}>RED</color>  {redScore}";
            blueHeader.text = $"<color={CTF_Rules.Hex(CTF_Team.Blue)}>BLUE</color>  {blueScore}";
            CTF_Board.Rows(roster.bots, roster.players, CTF_Team.Red, rows);
            redRows.text = CTF_Board.Lines(rows, MyId, maxRows, hex, text);
            CTF_Board.Rows(roster.bots, roster.players, CTF_Team.Blue, rows);
            blueRows.text = CTF_Board.Lines(rows, MyId, maxRows, hex, text);
        }
    }
}
