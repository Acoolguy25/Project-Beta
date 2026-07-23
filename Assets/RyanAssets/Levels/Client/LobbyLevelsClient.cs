using RyanAssets.Core;
using RyanAssets.DataService;
using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace RyanAssets.Levels.Client {
    public class LobbyLevelsClient : LevelsClient {
        public static Action<ulong> onXpChanged;
        public static ulong xp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            onXpChanged = null;
        }
        void Start() {
            UpdateLevel(xp);
            onXpChanged += OnXpChanged;
        }
        void OnXpChanged(ulong new_xp) {
            UpdateLevel(new_xp, true);
        }
        private void OnDestroy() {
            onXpChanged -= OnXpChanged;
        }
    }
}