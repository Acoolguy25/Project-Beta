using System.Globalization;
using RyanAssets.Client.ClientCore;
using RyanAssets.DataService;
using RyanAssets.Input;
using RyanAssets.Shared.Global;
using RyanAssets.UI;
using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Topbar {
    /// <summary>
    /// The strip of floating controls along the top of the screen: menu, chat and player list
    /// buttons (<see cref="TopbarPanelButton"/>), the local player's coins, the game's status line
    /// and the player count. It also maps the keyboard shortcuts for those panels.
    /// </summary>
    public class ClientTopbar : MonoBehaviour {
        [Header("Status")]
        [Tooltip("Pill in the middle of the bar; hidden while there is nothing to say.")]
        [SerializeField] GameObject statusPill;
        [SerializeField] TMP_Text statusText;
        [Tooltip("Show the game's name while the server has no status message.")]
        [SerializeField] bool showGameNameWhenIdle = true;

        [Header("Coins")]
        [SerializeField] GameObject coinPill;
        [SerializeField] TMP_Text coinsText;

        [Header("Players")]
        [SerializeField] TMP_Text playerCountText;

        PlayerData observedPlayerData;

        void OnEnable() {
            TopbarControls.menuToggledEvent += OnMenuShortcut;
            TopbarControls.playerListEvent += OnPlayerListShortcut;
            SharedGlobalEvents.TopMessageChanged += OnTopMessageChanged;
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved += OnMyPlayerRemoved;
            PlayerData.OnPlayerAdded += OnPlayerCountChanged;
            PlayerData.OnPlayerRemoved += OnPlayerCountChanged;

            OnTopMessageChanged(SharedGlobalEvents.Instance != null ? SharedGlobalEvents.Instance.TopMessage : null);
            RefreshCoins(PlayerData.localData != null ? PlayerData.localData.gold.Value : 0);
            RefreshPlayerCount();
        }

        void OnDisable() {
            TopbarControls.menuToggledEvent -= OnMenuShortcut;
            TopbarControls.playerListEvent -= OnPlayerListShortcut;
            SharedGlobalEvents.TopMessageChanged -= OnTopMessageChanged;
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved -= OnMyPlayerRemoved;
            PlayerData.OnPlayerAdded -= OnPlayerCountChanged;
            PlayerData.OnPlayerRemoved -= OnPlayerCountChanged;
            StopObservingCoins();
        }

        // --- Shortcuts ----------------------------------------------------------

        void OnMenuShortcut() => HudPanel.Toggle(HudPanelId.GameMenu);

        void OnPlayerListShortcut() => HudPanel.Toggle(HudPanelId.PlayerList);

        // --- Status ---------------------------------------------------------------

        void OnTopMessageChanged(string message) {
            string text = string.IsNullOrEmpty(message) && showGameNameWhenIdle
                ? FormatGameName(ClientConnector.joinUniverseId)
                : message;
            if (statusText != null)
                statusText.text = text ?? string.Empty;
            if (statusPill != null)
                statusPill.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>"war_valley" -> "War Valley".</summary>
        static string FormatGameName(string universeId) {
            if (string.IsNullOrEmpty(universeId))
                return string.Empty;
            string spaced = universeId.Replace('_', ' ').Replace('-', ' ');
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
        }

        // --- Coins ----------------------------------------------------------------

        void OnMyPlayerAdded(PlayerData playerData) {
            StopObservingCoins();
            observedPlayerData = playerData;
            playerData.gold.OnChange += OnGoldChanged;
            RefreshCoins(playerData.gold.Value);
        }

        void OnMyPlayerRemoved(PlayerData playerData) {
            if (observedPlayerData == playerData)
                StopObservingCoins();
            RefreshCoins(0);
        }

        void StopObservingCoins() {
            if (observedPlayerData == null)
                return;
            observedPlayerData.gold.OnChange -= OnGoldChanged;
            observedPlayerData = null;
        }

        void OnGoldChanged(ulong oldValue, ulong newValue, bool asServer) => RefreshCoins(newValue);

        void RefreshCoins(ulong coins) {
            if (coinsText != null)
                coinsText.text = coins.ToString("N0", CultureInfo.InvariantCulture);
        }

        // --- Player count ---------------------------------------------------------

        void OnPlayerCountChanged(PlayerData _) => RefreshPlayerCount();

        void RefreshPlayerCount() {
            if (playerCountText == null)
                return;
            int count = PlayerData.Players?.Count ?? 0;
            playerCountText.text = count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
