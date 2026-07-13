using System.Collections;
using UnityEngine;

namespace RyanAssets.DataService {
    [System.Serializable]
    public struct LocalPlayerData {
        public string username;
        public ulong xp;
        public ulong gold;
    };
}