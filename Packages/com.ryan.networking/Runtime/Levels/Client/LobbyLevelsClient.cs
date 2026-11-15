using System;
using UnityEngine;

namespace RyanAssets.Levels.Client {
    /// <summary>Level badge in the main menu, fed by the backend's XP for the signed-in account.</summary>
    public class LobbyLevelsClient : LevelsClient {
        public static Action<ulong> onXpChanged;
        public static ulong xp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            onXpChanged = null;
            xp = 0;
        }

        void Start() {
            UpdateLevel(xp, instant: true);
            onXpChanged += OnXpChanged;
        }

        void OnDestroy() => onXpChanged -= OnXpChanged;

        void OnXpChanged(ulong newXp) => UpdateLevel(newXp);
    }
}
