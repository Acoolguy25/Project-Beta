using FishNet.Object.Synchronizing;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.PlayerList {
    /// <summary>One player in the player list: team marker, name and their leaderboard stats.</summary>
    public class PlayerListRow : MonoBehaviour {
        [SerializeField] TMP_Text nameText;
        [SerializeField] PlayerListStatColumns stats;
        [Tooltip("Small marker painted with the player's team colour; hidden when they have no team.")]
        [SerializeField] Image teamMarker;
        [Tooltip("Row background; tinted for the local player.")]
        [SerializeField] Image background;

        PlayerData player;

        public PlayerData Player => player;

        public void Init(PlayerData data) {
            Unbind();
            player = data;
            if (isActiveAndEnabled)
                Bind();
        }

        void OnEnable() => Bind();

        void OnDisable() => Unbind();

        void Bind() {
            if (player == null)
                return;
            player.leaderboard.OnChange += OnLeaderboardChanged;
            player.username.OnChange += OnUsernameChanged;
            player.team.OnChange += OnTeamChanged;
            RefreshIdentity();
            RefreshStats();
        }

        void Unbind() {
            if (player == null)
                return;
            player.leaderboard.OnChange -= OnLeaderboardChanged;
            player.username.OnChange -= OnUsernameChanged;
            player.team.OnChange -= OnTeamChanged;
        }

        void OnLeaderboardChanged(SyncListOperation op, int index, int oldItem, int newItem, bool asServer) {
            if (op == SyncListOperation.Set && index < stats.Count)
                stats.Set(index, newItem);
            else if (op == SyncListOperation.Complete)
                RefreshStats();
        }

        void OnUsernameChanged(string oldValue, string newValue, bool asServer) => RefreshIdentity();

        void OnTeamChanged(TeamConfig oldValue, TeamConfig newValue, bool asServer) => RefreshIdentity();

        void RefreshStats() {
            stats.SetCount(player.leaderboard.Count);
            for (int i = 0; i < player.leaderboard.Count; i++)
                stats.Set(i, player.leaderboard[i]);
        }

        void RefreshIdentity() {
            UITheme theme = UITheme.Active;
            nameText.text = player.username.Value;
            nameText.color = player.IsOwner ? theme.textPrimary : theme.textSecondary;
            nameText.fontStyle = player.IsOwner ? FontStyles.Bold : FontStyles.Normal;

            // Players see their own real team; everyone else's is what the game chooses to show.
            TeamColor team = player.IsOwner ? player.team.Value.realTeam : player.team.Value.displayTeam;
            bool hasTeam = team != TeamColor.None && team != TeamColor.White;
            if (teamMarker != null) {
                teamMarker.enabled = hasTeam;
                teamMarker.color = TeamConfig.TeamToColor(team);
            }
            if (background != null) {
                Color tint = player.IsOwner ? theme.surfaceHover : theme.surfaceInset;
                background.color = tint;
            }
        }
    }
}
