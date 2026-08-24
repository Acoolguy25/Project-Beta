namespace RyanAssets.Shared.Declarations {
    public interface IEntity {
        string DisplayName { set; get; }
        TeamConfig Team { get; }
    }
}