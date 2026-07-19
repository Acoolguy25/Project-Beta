using Cysharp.Threading.Tasks;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Core;
using RyanAssets.Shared.Player;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
namespace RyanAssets.Client.ClientAudio {
    [System.Serializable]
    public class AudioClipList
    {
        public AudioMixerGroup audioMixerGroup;
        public List<AudioClip> Tracks = new();
    }
    public class MusicService: MonoBehaviour {
        public MusicTracks activeTrack { get; private set; }
        [SerializeField]
        private List<AudioClipList> trackList;
        [SerializeField]
        private AudioMixer audioMixer;

        private CancellationTokenSource musicCTS;
        private AudioSource audioSource;
        int currentTrackIndex;
        void Start(){
            audioSource = GetComponent<AudioSource>();
            activeTrack = MusicTracks.None;
            InitTracks();
            SettingsInit();
            InstanceNotReady();
            SharedGlobalEvents.BindInstanceReady(InstanceReady, true);
            SharedGlobalEvents.OnInstanceRemoved += InstanceNotReady;
        }
        void InstanceReady() {
            SetActiveTrack(SharedGlobalEvents.Instance.MusicTrack.Value);
            SharedGlobalEvents.Instance.MusicTrack.OnChange += (prev, next, _) => {
                SetActiveTrack(next);
            };
        }
        void InstanceNotReady() {
            SetActiveTrack(MusicTracks.MenuMusic);
        }
        void InitTracks() {
            foreach (var activetrack in trackList) {
#if UNITY_EDITOR
                if (activetrack.Tracks.ToHashSet().Count() != activetrack.Tracks.Count()){
                    Debug.LogError($"MusicService: Duplicate tracks found in {activetrack}");
                }
                Debug.Assert(activetrack.Tracks.Count > 0, $"MusicService: No tracks found in {activetrack}");
#endif
            }
        }
        void SettingsInit() {
            foreach (KeyValuePair<string, GameSettingsInstance> setting in GameSettingsClient.gameSettingsConfigUI) {
                if (setting.Value.category == GameSettingCategory.Audio) {
                    IntGameSetting intGameSetting = (IntGameSetting)setting.Value;
                    intGameSetting.on_update += (val) => UpdateMusicVolume(setting.Value.name, val);
                    UpdateMusicVolume(setting.Value.name, intGameSetting.value);
                }
            }
        }
        void UpdateMusicVolume(string musicType, int volume) {
            float db = volume <= 0 ? -80f : Mathf.Log10(volume / 200f) * 20f;
            audioMixer.SetFloat(musicType + "Volume", db);

            //TweenService.TweenComponents.TweenAudioMixerGroup.FadeMixerVolume(audioMixer, musicType + "Volume", db, 0.25f, owner: musicType);
        }
        public void SetActiveTrack(MusicTracks track){
            activeTrack = track;
            MathHelper.Shuffle(trackList[((int)track)].Tracks);
            currentTrackIndex = -1;
            musicCTS?.Cancel();
            musicCTS?.Dispose();
            musicCTS = new CancellationTokenSource();
            PlayLoop(musicCTS.Token).Forget();
        }
        public void SwitchToNextTrack() {
            AudioClipList clipList = trackList[(int)activeTrack];
            List<AudioClip> audioClipLists = clipList.Tracks;
            //if (audioClipLists.Count == 0)
            //    return;
            // unnecessary check, because we always have at least one track in the list
            currentTrackIndex = (currentTrackIndex+1) % audioClipLists.Count;
            audioSource.Stop();
            audioSource.outputAudioMixerGroup = clipList.audioMixerGroup;
            audioSource.clip = audioClipLists[currentTrackIndex];
            audioSource.Play();
        }
        public async UniTask PlayLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                SwitchToNextTrack();

                //await UniTask.WaitForSeconds(audioSource.clip.length, cancellationToken: token);
                await UniTask.WaitUntil(() => !audioSource.isPlaying, cancellationToken: token);
            }
        }
        void OnDestroy() {
            musicCTS?.Cancel();
            musicCTS?.Dispose();
            SharedGlobalEvents.OnInstanceReadyPersistent -= InstanceReady;
            SharedGlobalEvents.OnInstanceRemoved -= InstanceNotReady;
        }
        //void Update() {
        //    if (!audioSource.isPlaying) {
        //        SwitchToNextTrack();
        //    }
        //}
    }
}