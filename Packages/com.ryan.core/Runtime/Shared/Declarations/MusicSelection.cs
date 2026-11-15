using System;

namespace RyanAssets.Shared.Declarations {
    [Serializable]
    public enum MusicSelection : ushort {
        None = 0,
        MenuMusic,
        GameMusic,
        HorrorAmbientMusic,
        HorrorChaseMusic,
        VictoryMusic,
    }
}
