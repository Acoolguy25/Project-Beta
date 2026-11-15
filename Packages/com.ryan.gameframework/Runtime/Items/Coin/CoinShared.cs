using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Shared.Global;
#if !UNITY_SERVER
using RyanAssets.Client.ClientAudio;
#endif
#if UNITY_SERVER
using RyanAssets.Levels.Server;
#endif
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
        protected override bool OnCollectServer(NetworkBehaviour collectObject, NetworkConnection conn) {
            if (conn == null)
                return true;
            if (LevelsServer.AwardPlayerGold(conn, coinValue))
                return true;

            conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
            return false;
        }
#endif
    }
}
