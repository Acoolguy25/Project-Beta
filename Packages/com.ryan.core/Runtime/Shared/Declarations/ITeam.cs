namespace RyanAssets.Shared.Declarations {
    public interface ITeam {
        TeamConfig GetTeam();
#if UNITY_SERVER
        void SetTeam(TeamConfig teamConfig);
#endif
    }

    public static class TeamExtensions {
#if UNITY_SERVER
        public static void SetRealColor(this ITeam team, TeamColor realColor) {
            team.SetTeam(new TeamConfig(realColor, team.GetTeam().displayTeam));
        }

        public static void SetDisplayColor(this ITeam team, TeamColor displayColor) {
            team.SetTeam(new TeamConfig(team.GetTeam().realTeam, displayColor));
        }
#endif
    }
}
