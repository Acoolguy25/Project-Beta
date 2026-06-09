using UnityEngine;
using System.Collections.Generic;
namespace RyanAssets.Client.ClientAudio {
    public enum MusicTracks: ushort {
        MenuMusic
    };
    [System.Serializable]
    public class AudioClipList
    {
        public List<AudioClip> Tracks = new();
    }
    public class MusicService: MonoBehaviour {
        public MusicTracks activeTrack  {get; private set;}
        [SerializeField]
        private List<AudioClipList> trackList;
        void Start(){
            SetActiveTrack(MusicTracks.MenuMusic);
        }
        public void SetActiveTrack(MusicTracks track){
            activeTrack = track;
        }
    }
}