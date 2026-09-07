using Cysharp.Threading.Tasks;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Core;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Declarations;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
namespace RyanAssets.Client.ClientAudio {
    [System.Serializable]
    public class AudioClipList {
        public MusicSelection musicSelection;
        [Tooltip("Resume this selection's track, playback time, and shuffled order when returning to it.")]
        public bool savePlaybackState;
        [Min(0f)] public float minDelay = 0f;
        [Min(0f)] public float maxDelay = 0f;
        public AudioMixerGroup audioMixerGroup;
        public List<AudioClip> Tracks = new();
    }
    public class MusicService : MonoBehaviour {
        private sealed class PlaybackState {
            public readonly List<AudioClip> playbackOrder;
            public int trackIndex = -1;
            public float trackTime;
            public bool resumeCurrentTrack;

            public PlaybackState(List<AudioClip> tracks) {
                playbackOrder = new List<AudioClip>(tracks);
                MathHelper.Shuffle(playbackOrder);
            }
        }

        public MusicSelection activeTrack { get; private set; }
        [SerializeField]
        private List<AudioClipList> trackList;
        [SerializeField]
        private AudioMixer audioMixer;
        [SerializeField, Min(0f)]
        private float fadeDuration = 1f;

        private CancellationTokenSource musicCTS;
        private CancellationTokenSource fadeCTS;
        private AudioSource audioSource;
        private AudioSource secondaryAudioSource;
        private float musicSourceVolume;
        private SharedGlobalEvents subscribedEvents;
        private readonly Dictionary<MusicSelection, AudioClipList> tracksBySelection = new();
        private readonly Dictionary<MusicSelection, PlaybackState> playbackStates = new();
        private MusicSelection playingSelection = MusicSelection.None;

        public static AudioSource CreateOneShot(AudioClip clip, Vector3 position) {
            if (clip == null)
                return null;
            return CreateOneShot(null, clip, position);
        }

        public static AudioSource CreateOneShot(AudioSource source, AudioClip clip = null) {
            if (source == null)
                return null;
            return CreateOneShot(source, clip != null ? clip : source.clip, source.transform.position);
        }

        static AudioSource CreateOneShot(AudioSource source, AudioClip clip, Vector3 position) {
            if (clip == null)
                return null;

            GameObject soundObject = new GameObject($"{clip.name} One Shot");
            soundObject.transform.position = position;
            AudioSource playbackSource = soundObject.AddComponent<AudioSource>();

            if (source != null)
                CopyAudioSettings(source, playbackSource);

            playbackSource.PlayOneShot(clip);
            float playbackDuration = clip.length / Mathf.Max(Mathf.Abs(playbackSource.pitch), 0.01f);
            Destroy(soundObject, playbackDuration + 0.1f);
            return playbackSource;
        }

        static void CopyAudioSettings(AudioSource source, AudioSource destination) {
            destination.outputAudioMixerGroup = source.outputAudioMixerGroup;
            destination.volume = source.volume;
            destination.pitch = source.pitch;
            destination.mute = source.mute;
            destination.priority = source.priority;
            destination.panStereo = source.panStereo;
            destination.spatialBlend = source.spatialBlend;
            destination.reverbZoneMix = source.reverbZoneMix;
            destination.spatialize = source.spatialize;
            destination.spatializePostEffects = source.spatializePostEffects;
            destination.dopplerLevel = source.dopplerLevel;
            destination.spread = source.spread;
            destination.rolloffMode = source.rolloffMode;
            destination.minDistance = source.minDistance;
            destination.maxDistance = source.maxDistance;
            destination.bypassEffects = source.bypassEffects;
            destination.bypassListenerEffects = source.bypassListenerEffects;
            destination.bypassReverbZones = source.bypassReverbZones;
            destination.ignoreListenerPause = source.ignoreListenerPause;
            destination.ignoreListenerVolume = source.ignoreListenerVolume;
            destination.velocityUpdateMode = source.velocityUpdateMode;
        }

        void Start() {
            audioSource = GetComponent<AudioSource>();
            musicSourceVolume = audioSource.volume;
            secondaryAudioSource = gameObject.AddComponent<AudioSource>();
            CopyAudioSettings(audioSource, secondaryAudioSource);
            secondaryAudioSource.playOnAwake = false;
            secondaryAudioSource.volume = 0f;
            activeTrack = MusicSelection.None;
            InitTracks();
            SettingsInit();
            InstanceNotReady();
            SharedGlobalEvents.BindInstanceReady(InstanceReady, true);
            SharedGlobalEvents.OnInstanceRemoved += InstanceNotReady;
        }
        void InstanceReady() {
            UnsubscribeFromMusicTrack();
            subscribedEvents = SharedGlobalEvents.Instance;
            SetActiveTrack(subscribedEvents.MusicTrack.Value);
            subscribedEvents.MusicTrack.OnChange += OnMusicTrackChanged;
        }
        void OnMusicTrackChanged(MusicSelection previous, MusicSelection next, bool asServer) => SetActiveTrack(next);
        void InstanceNotReady() {
            UnsubscribeFromMusicTrack();
            SetActiveTrack(MusicSelection.MenuMusic);
        }
        void UnsubscribeFromMusicTrack() {
            if (subscribedEvents == null)
                return;

            subscribedEvents.MusicTrack.OnChange -= OnMusicTrackChanged;
            subscribedEvents = null;
        }
        void InitTracks() {
            tracksBySelection.Clear();
            playbackStates.Clear();
            foreach (AudioClipList track in trackList) {
                if (!tracksBySelection.TryAdd(track.musicSelection, track)) {
                    Debug.LogError($"MusicService: Duplicate music selection {track.musicSelection}.", this);
                    continue;
                }
#if UNITY_EDITOR
                if (track.Tracks.ToHashSet().Count != track.Tracks.Count) {
                    Debug.LogError($"MusicService: Duplicate tracks found in {track.musicSelection}.", this);
                }
                Debug.Assert(track.Tracks.Count > 0, $"MusicService: No tracks found in {track.musicSelection}.", this);
                Debug.Assert(track.minDelay >= 0f, $"MusicService: Minimum delay cannot be negative for {track.musicSelection}.", this);
                Debug.Assert(track.maxDelay >= track.minDelay, $"MusicService: Maximum delay must be at least the minimum delay for {track.musicSelection}.", this);
#endif
                playbackStates.Add(track.musicSelection, new PlaybackState(track.Tracks));
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
        public void SetActiveTrack(MusicSelection track) {
            if (track == activeTrack && musicCTS != null)
                return;

            SaveOrResetActivePlaybackState();
            StopPlaybackLoop();
            activeTrack = track;

            if (track == MusicSelection.None) {
                FadeToSilence().Forget();
                return;
            }

            if (!tracksBySelection.TryGetValue(track, out AudioClipList clipList) || clipList.Tracks.Count == 0) {
                Debug.LogWarning($"MusicService: No tracks configured for {track}.", this);
                FadeToSilence().Forget();
                return;
            }

            musicCTS = new CancellationTokenSource();
            PlayLoop(clipList, playbackStates[track], musicCTS.Token).Forget();
        }
        public void SwitchToNextTrack() {
            if (!tracksBySelection.TryGetValue(activeTrack, out AudioClipList clipList) || clipList.Tracks.Count == 0)
                return;

            SaveActivePlaybackState();
            StopPlaybackLoop();
            PlaybackState state = playbackStates[activeTrack];
            state.resumeCurrentTrack = false;
            state.trackTime = 0f;
            musicCTS = new CancellationTokenSource();
            PlayLoop(clipList, state, musicCTS.Token).Forget();
        }
        async UniTask PlayLoop(AudioClipList clipList, PlaybackState state, CancellationToken token) {
            try {
                while (!token.IsCancellationRequested) {
                    if (!state.resumeCurrentTrack) {
                        state.trackIndex = (state.trackIndex + 1) % state.playbackOrder.Count;
                        state.trackTime = 0f;
                    }

                    state.resumeCurrentTrack = true;
                    AudioClip clip = state.playbackOrder[state.trackIndex];
                    float delay = Random.Range(
                        Mathf.Max(0f, clipList.minDelay),
                        Mathf.Max(clipList.minDelay, clipList.maxDelay));

                    await TransitionToTrack(clip, state.trackTime, clipList.audioMixerGroup, activeTrack, delay, token);

                    await UniTask.WaitUntil(() => !audioSource.isPlaying, cancellationToken: token);
                    state.trackTime = 0f;
                    state.resumeCurrentTrack = false;
                }
            } catch (System.OperationCanceledException) {
                // Track changes cancel the previous playback loop.
            }
        }
        async UniTask TransitionToTrack(AudioClip clip, float startTime, AudioMixerGroup mixerGroup,
            MusicSelection selection, float delay, CancellationToken loopToken) {
            CancelFade();

            AudioSource outgoingSource = audioSource;
            bool hasOutgoingTrack = outgoingSource.isPlaying;
            float fadeOutDuration = hasOutgoingTrack ? Mathf.Min(fadeDuration, delay * 0.5f) : 0f;
            float fadeInDuration = Mathf.Min(fadeDuration, delay - fadeOutDuration);
            float silenceDuration = Mathf.Max(0f, delay - fadeOutDuration - fadeInDuration);

            fadeCTS = CancellationTokenSource.CreateLinkedTokenSource(loopToken);
            CancellationToken token = fadeCTS.Token;

            if (hasOutgoingTrack)
                await FadeSource(outgoingSource, outgoingSource.volume, 0f, fadeOutDuration, token);

            outgoingSource.Stop();
            outgoingSource.volume = musicSourceVolume;

            if (silenceDuration > 0f)
                await UniTask.Delay(System.TimeSpan.FromSeconds(silenceDuration), ignoreTimeScale: true, cancellationToken: token);

            AudioSource incomingSource = object.ReferenceEquals(audioSource, secondaryAudioSource)
                ? GetComponent<AudioSource>()
                : secondaryAudioSource;

            incomingSource.Stop();
            incomingSource.outputAudioMixerGroup = mixerGroup;
            incomingSource.clip = clip;
            incomingSource.volume = 0f;
            incomingSource.time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, clip.length - 0.01f));
            incomingSource.Play();
            audioSource = incomingSource;
            playingSelection = selection;
            await FadeSource(incomingSource, 0f, musicSourceVolume, fadeInDuration, token);
        }
        static async UniTask FadeSource(AudioSource source, float from, float to, float duration, CancellationToken token) {
            if (duration <= 0f) {
                source.volume = to;
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration) {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            }

            source.volume = to;
        }
        void SaveActivePlaybackState() {
            if (activeTrack == MusicSelection.None || playingSelection != activeTrack || audioSource == null || audioSource.clip == null)
                return;

            if (!playbackStates.TryGetValue(activeTrack, out PlaybackState state) || state.trackIndex < 0)
                return;

            if (state.playbackOrder[state.trackIndex] != audioSource.clip)
                return;

            state.trackTime = audioSource.time;
            state.resumeCurrentTrack = true;
        }
        void SaveOrResetActivePlaybackState() {
            if (activeTrack == MusicSelection.None || !tracksBySelection.TryGetValue(activeTrack, out AudioClipList clipList))
                return;

            if (clipList.savePlaybackState) {
                SaveActivePlaybackState();
                return;
            }

            playbackStates[activeTrack] = new PlaybackState(clipList.Tracks);
        }
        async UniTask FadeToSilence() {
            CancelFade();
            AudioSource source = audioSource;
            AudioSource otherSource = object.ReferenceEquals(source, secondaryAudioSource)
                ? GetComponent<AudioSource>()
                : secondaryAudioSource;
            otherSource.Stop();
            otherSource.volume = musicSourceVolume;
            float startVolume = source.volume;
            fadeCTS = new CancellationTokenSource();
            CancellationToken token = fadeCTS.Token;

            try {
                if (fadeDuration > 0f) {
                    float elapsed = 0f;
                    while (elapsed < fadeDuration) {
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                        elapsed += Time.unscaledDeltaTime;
                        source.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(elapsed / fadeDuration));
                    }
                }

                source.Stop();
                source.volume = musicSourceVolume;
            } catch (System.OperationCanceledException) {
                // A newer transition owns the source now.
            }
        }
        void CancelFade() {
            fadeCTS?.Cancel();
            fadeCTS?.Dispose();
            fadeCTS = null;
        }
        void StopPlaybackLoop() {
            musicCTS?.Cancel();
            musicCTS?.Dispose();
            musicCTS = null;
        }
        void StopMusic() {
            StopPlaybackLoop();
            CancelFade();
            audioSource?.Stop();
            secondaryAudioSource?.Stop();
        }
        public void StopAllMusic() {
            StopMusic();
            activeTrack = MusicSelection.None;
            playingSelection = MusicSelection.None;
        }
        void OnDestroy() {
            StopMusic();
            UnsubscribeFromMusicTrack();
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
