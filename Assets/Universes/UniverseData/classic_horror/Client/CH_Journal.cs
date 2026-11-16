using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The field journal. Each record is a page with its heading set apart from the text; new
    /// records announce themselves with a page-turn sound, a "New entry" badge and a punch on the
    /// F hint, and the journal opens on the newest unread page. The world keeps moving while it is open.
    /// </summary>
    public sealed class CH_Journal : MonoBehaviour {
        public GameObject panel;
        public TMP_Text body, pageLabel;
        public Button close, next, previous;
        [Tooltip("The F hint on the case card; punched when a new entry arrives.")]
        public RectTransform hint;
        [Tooltip("\"New entry\" badge shown until the journal is opened.")]
        public HudPopup newEntryBadge;
        [SerializeField] Color headingColor = new(0.86f, 0.62f, 0.32f);
        [SerializeField, Range(0.3f, 1f)] float headingScale = 0.72f;

        [Header("Sounds")]
        public AudioClip openSound;
        public AudioClip closeSound;
        public AudioClip[] pageSounds = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] float soundVolume = 0.7f;
        [SerializeField, Min(0f)] float hintPunch = 0.35f;

        string[] entries = new string[0];
        int page, readCount;
        InputAction closeInput;
        string headingHex;

        public bool IsOpen { get; private set; }

        void Awake() {
            headingHex = ColorUtility.ToHtmlStringRGB(headingColor);
            closeInput = new InputAction("Close case journal", InputActionType.Button, "<Keyboard>/f");
            closeInput.AddBinding("<Keyboard>/escape");
            closeInput.AddBinding("<Gamepad>/buttonEast");
            closeInput.performed += OnCloseInput;
            close.onClick.AddListener(Close);
            next.onClick.AddListener(NextPage);
            previous.onClick.AddListener(PreviousPage);
            panel.SetActive(false);
            if (newEntryBadge != null) newEntryBadge.HideImmediate();
        }

        void OnDestroy() {
            closeInput?.Dispose();
        }

        /// <summary>Replaces the journal's pages. A fresh case starts unread from page one.</summary>
        public void SetEntries(string[] journal, bool freshCase) {
            journal ??= new string[0];
            if (freshCase) {
                Close();
                page = 0;
                readCount = 0;
                if (newEntryBadge != null) newEntryBadge.HideImmediate();
            } else if (journal.Length > entries.Length && entries.Length > 0) {
                Announce();
            }
            entries = journal;
            if (IsOpen) readCount = entries.Length;
            Refresh();
        }

        void Announce() {
            PlayPageSound();
            if (hint != null) Punch.Scale(hint, hintPunch);
            if (!IsOpen && newEntryBadge != null) newEntryBadge.Show();
        }

        public void Open() {
            if (IsOpen || entries.Length == 0) return;
            IsOpen = true;
            // Jump to the first unread page so a new record is what the investigator sees.
            if (readCount < entries.Length) page = readCount;
            readCount = entries.Length;
            panel.SetActive(true);
            if (newEntryBadge != null) newEntryBadge.Hide();
            InputService.SetInputScreenActive(InputScreen.GameMenu, true);
            closeInput.Enable();
            PlaySound(openSound);
            Refresh();
        }

        public void Close() {
            if (!IsOpen) return;
            IsOpen = false;
            panel.SetActive(false);
            closeInput.Disable();
            InputService.SetInputScreenActive(InputScreen.GameMenu, false);
            PlaySound(closeSound);
        }

        void OnCloseInput(InputAction.CallbackContext _) => Close();
        void NextPage() { page++; PlayPageSound(); Refresh(); }
        void PreviousPage() { page--; PlayPageSound(); Refresh(); }

        void Refresh() {
            if (entries.Length == 0) return;
            page = Mathf.Clamp(page, 0, entries.Length - 1);
            body.text = Format(entries[page]);
            pageLabel.text = $"Page {page + 1} of {entries.Length}";
            previous.interactable = page > 0;
            next.interactable = page + 1 < entries.Length;
        }

        /// <summary>"Heading / text" becomes a small amber heading above the text.</summary>
        string Format(string entry) {
            CH_StoryText.Split(entry, out string heading, out string text);
            if (string.IsNullOrEmpty(heading)) return text;
            return $"<size={headingScale * 100f:0}%><color=#{headingHex}><b>{heading.ToUpperInvariant()}</b></color></size>\n\n{text}";
        }

        void PlayPageSound() {
            if (pageSounds.Length > 0) PlaySound(pageSounds[Random.Range(0, pageSounds.Length)]);
        }

        void PlaySound(AudioClip clip) {
            if (clip != null) GameSfx.PlayClip(clip, soundVolume);
        }
    }
}
