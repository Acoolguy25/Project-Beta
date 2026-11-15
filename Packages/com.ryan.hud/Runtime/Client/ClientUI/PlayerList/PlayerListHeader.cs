using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Global;
using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.PlayerList {
    /// <summary>
    /// The player list's header: title and the names of the game's leaderboard stats, which the
    /// server publishes in <see cref="SharedGlobalEvents.LeaderboardHeaders"/>.
    /// </summary>
    public class PlayerListHeader : MonoBehaviour {
        [SerializeField] TMP_Text titleText;
        [SerializeField] string title = "Players";
        [SerializeField] PlayerListStatColumns columns;

        SharedGlobalEvents boundInstance;

        void OnEnable() {
            if (titleText != null)
                titleText.text = title;
            // Persistent: a new server session spawns a new instance, and the header must follow it.
            SharedGlobalEvents.BindInstanceReady(OnInstanceReady, persistent: true);
        }

        void OnDisable() {
            SharedGlobalEvents.UnbindInstanceReady(OnInstanceReady);
            if (boundInstance != null)
                boundInstance.LeaderboardHeaders.OnChange -= OnHeadersChanged;
            boundInstance = null;
        }

        void OnInstanceReady() {
            if (!isActiveAndEnabled || SharedGlobalEvents.Instance == null || boundInstance == SharedGlobalEvents.Instance)
                return;
            if (boundInstance != null)
                boundInstance.LeaderboardHeaders.OnChange -= OnHeadersChanged;
            boundInstance = SharedGlobalEvents.Instance;
            boundInstance.LeaderboardHeaders.OnChange += OnHeadersChanged;
            Rebuild();
        }

        void OnHeadersChanged(SyncListOperation op, int index, string oldItem, string newItem, bool asServer) {
            if (op == SyncListOperation.Complete)
                Rebuild();
        }

        void Rebuild() {
            var headers = boundInstance.LeaderboardHeaders;
            columns.SetCount(headers.Count);
            for (int i = 0; i < headers.Count; i++)
                columns.Set(i, headers[i]);
        }
    }
}
