using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Speaks the server's voice lines (<see cref="CH_VoiceBroadcast"/>) and subtitles them. Mara's radio and Lily's
    /// whisper are in your ear; the priest speaks from wherever he is; a freed singer speaks from their grave. Every
    /// line also goes into the night's transcript, which the journal shows.
    /// </summary>
    public sealed class CH_VoicePlayer : MonoBehaviour {
        [System.Serializable]
        public struct SpeakerStyle {
            public string speaker;
            public Color caption;
            [Tooltip("The subtitle's emblem for this speaker when they are not on the radio.")]
            public Sprite icon;
        }

        public CH_RadioFeed subtitles;
        [Tooltip("2D source for lines in your ear.")]
        public AudioSource ear;
        [Tooltip("3D source that follows the priest.")]
        public AudioSource monster;
        [Tooltip("3D source moved to wherever a world line is spoken.")]
        public AudioSource world;
        [SerializeField, Range(0f, 1f)] float earVolume = 1f;
        [SerializeField, Range(0f, 1f)] float monsterVolume = 1f;
        [Tooltip("The priest's lines are subtitled only within this distance (metres); further off they are just a voice in the dark.")]
        [SerializeField, Min(0f)] float monsterSubtitleRange = 45f;
        [Tooltip("Caption colour per speaker.")]
        public SpeakerStyle[] styles = new SpeakerStyle[0];
        [SerializeField] Color defaultCaption = new(0.78f, 0.74f, 0.66f);
        [Tooltip("The emblem on a radio line, whoever speaks it.")]
        public Sprite radioIcon;
        [Tooltip("The emblem for a speaker without a style (the freed singers).")]
        public Sprite defaultIcon;

        CH_VoiceBank bank;
        Transform monsterTransform;
        readonly List<string> transcript = new();

        /// <summary>Every line heard tonight, "SPEAKER / words", oldest first.</summary>
        public IReadOnlyList<string> Transcript => transcript;
        public int TranscriptRevision { get; private set; }
        public bool EarBusy => ear != null && ear.isPlaying;

        public void Bind(CH_VoiceBank voices) => bank = voices;

        public void SetMonster(Transform presence) => monsterTransform = presence;

        public void ResetNight() {
            transcript.Clear();
            TranscriptRevision++;
            if (ear != null) ear.Stop();
            if (monster != null) monster.Stop();
            if (world != null) world.Stop();
            subtitles.Clear();
        }

        public void Play(CH_VoiceBroadcast voice) {
            if (bank == null || !bank.TryGet(voice.line, out CH_VoiceBank.Line line)) {
                Debug.LogWarning($"[Drowned Choir] unknown voice line '{voice.line}'");
                return;
            }
            SpeakerStyle style = StyleOf(line.speaker);
            Color caption = style.caption;
            Sprite icon = line.radio ? radioIcon : style.icon;
            bool subtitle = true;
            switch (voice.mode) {
                case CH_VoiceMode.Ear:
                    PlayOn(ear, line.clip, earVolume);
                    break;
                case CH_VoiceMode.Monster:
                    if (monsterTransform != null && monster != null) monster.transform.position = monsterTransform.position + Vector3.up * 1.7f;
                    PlayOn(monster, line.clip, monsterVolume);
                    Camera view = Camera.main;
                    subtitle = monsterTransform == null || view == null
                        || Vector3.Distance(view.transform.position, monsterTransform.position) <= monsterSubtitleRange;
                    break;
                case CH_VoiceMode.World:
                    if (world != null) world.transform.position = voice.position;
                    PlayOn(world, line.clip, 1f);
                    break;
            }
            transcript.Add(line.speaker + CH_StoryText.Separator + line.text);
            TranscriptRevision++;
            if (subtitle) subtitles.Show(line.speaker, line.text, caption, icon, line.seconds, line.radio);
        }

        void LateUpdate() {
            // The priest keeps talking as he moves.
            if (monster != null && monster.isPlaying && monsterTransform != null)
                monster.transform.position = monsterTransform.position + Vector3.up * 1.7f;
        }

        static void PlayOn(AudioSource source, AudioClip clip, float volume) {
            if (source == null || clip == null) return;
            source.Stop();
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        SpeakerStyle StyleOf(string speaker) {
            foreach (SpeakerStyle style in styles) if (style.speaker == speaker) return style;
            return new SpeakerStyle { speaker = speaker, caption = defaultCaption, icon = defaultIcon };
        }
    }
}
