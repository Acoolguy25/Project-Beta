using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Shared.Global;
#if !UNITY_SERVER
using RyanAssets.Client.ClientAudio;
#endif
#if UNITY_SERVER
using RyanAssets.Levels.Server;
#endif
using System;
using UnityEngine;

namespace RyanAssets.Items.Coin {
    public class CoinShared : CollectibleItem {
        [SerializeField]
        ulong coinValue = 2;

#if !UNITY_SERVER
        AudioSource audioSource;

        void Awake() {
            audioSource = GetComponent<AudioSource>();
        }

        protected override void OnCollectedClient() {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
                MusicService.CreateOneShot(audioSource);
        }
#endif

#if UNITY_SERVER
        /// <summary>
        /// Asked before a player picks a coin up; returning false leaves the coin lying (a game mode's full
        /// coin bag, for example). Null lets every player collect.
        /// </summary>
        public static Func<NetworkConnection, bool> CanCollect;

        /// <summary>A player picked up a coin: who, the gold it was worth, and where it lay.</summary>
        public static event Action<NetworkConnection, ulong, Vector3> Collected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            CanCollect = null;
            Collected = null;
        }

        protected override bool OnCollectServer(NetworkBehaviour collectObject, NetworkConnection conn) {
            if (conn == null)
                return true;
            if (CanCollect != null && !CanCollect(conn))
                return false;
            if (LevelsServer.AwardPlayerGold(conn, coinValue)) {
                Collected?.Invoke(conn, coinValue, transform.position);
                return true;
            }

            conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
            return false;
        }
#endif
    }
}
