using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The screen for the three minigames: a gauge with a needle and a green zone (the engine), four lit keys played back
    /// and sung (a hymn), a dial that warms as it nears its angle (the bell's locks). The server owns the rules
    /// (<see cref="CH_PuzzleRun"/>) and sends what to draw; this only draws it and sends the investigator's presses.
    /// Everything stays under the HUD canvas so the world is still visible around it: you can look up and see him coming.
    /// </summary>
    public sealed class CH_PuzzlePanel : MonoBehaviour {
        [Header("Layout (built by Rebuild Investigation HUD)")]
        public CanvasGroup group;
        public TMP_Text title, hint, status, readout;
        [Tooltip("Gauge: the needle and green zone of the engine, the dial marker of the locks.")]
        public GameObject gaugeRoot;
        public RectTransform gauge, zone, needle;
        public Image needleImage, zoneImage;
        [Tooltip("The lock's heat bar.")]
        public GameObject heatRoot;
        public RectTransform heatFill;
        public Image heatImage;
        [Tooltip("The hymn's four keys.")]
        public GameObject tilesRoot;
        public Image[] tiles = new Image[4];
        public TMP_Text[] tileKeys = new TMP_Text[4];
        [Tooltip("One pip per step; the unused ones are hidden.")]
        public Image[] pips = new Image[5];

        [Header("Look")]
        [SerializeField] Color pipDone = new(0.94f, 0.68f, 0.32f);
        [SerializeField] Color pipTodo = new(0.25f, 0.22f, 0.2f, 0.9f);
        [SerializeField] Color good = new(0.5f, 0.85f, 0.5f);
        [SerializeField] Color bad = new(0.9f, 0.25f, 0.2f);
        [SerializeField] Color idle = new(0.18f, 0.16f, 0.15f, 0.95f);
        [SerializeField] Color lit = new(0.98f, 0.8f, 0.45f);
        [SerializeField] Color coldDial = new(0.35f, 0.5f, 0.85f);
        [SerializeField] Color hotDial = new(1f, 0.45f, 0.2f);

        [Header("Feel")]
        [Tooltip("Seconds a hymn note is lit and rings while the hymn is played to you.")]
        [SerializeField, Min(0.2f)] float noteSeconds = 0.72f;
        [SerializeField, Min(0f)] float replayAfterMistake = 1.3f;
        [Tooltip("The dial turns this fast while A or D is held (degrees per second).")]
        [SerializeField, Min(10f)] float dialSpeed = 70f;
        [Tooltip("The dial is probed at most this often (seconds).")]
        [SerializeField, Min(0.03f)] float probeInterval = 0.1f;
        [SerializeField, Min(0f)] float resultSeconds = 1.4f;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        Func<CH_PuzzleAction, int, float, bool> send;
        AudioSource sound, humSource;
        CH_PuzzleBroadcast current;
        int stationId = -1, pendingStation = -1;
        float pendingUntil, syncedAt, resultUntil;
        string result;
        // Hymn playback.
        int[] playing = Array.Empty<int>();
        int playPos = -1;
        float playNextAt, replayAt = -1f, litUntil;
        int litTile = -1;
        // Lock.
        float dialAngle, nextProbe, lastProbedAngle = -1000f, warmth;
        float flashUntil;
        Color flashColour;
        readonly InputAction[] keys = new InputAction[4];
        InputAction turnLeft, turnRight;

        public bool IsOpen { get; private set; }

        /// <summary>The station being worked or asked for (-1 when none).</summary>
        public int StationId => IsOpen ? stationId : pendingStation;

        /// <summary>The client gives the panel a way to send: (action, station, value) returns false if it could not be sent.</summary>
        public void Bind(Func<CH_PuzzleAction, int, float, bool> sender) {
            send = sender;
            Initialise();
        }

        void Awake() => Initialise();

        /// <summary>
        /// Creates the panel's sound sources and key bindings. The card starts hidden (its object inactive), so Awake may not
        /// have run by the time the controller first binds or closes it: both paths come here, and the second is a no-op.
        /// </summary>
        void Initialise() {
            if (sound != null) return;
            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 0f;
            humSource = gameObject.AddComponent<AudioSource>();
            humSource.playOnAwake = false;
            humSource.loop = true;
            humSource.spatialBlend = 0f;
            for (int i = 0; i < keys.Length; i++) {
                keys[i] = new InputAction($"Choir note {i + 1}", InputActionType.Button, $"<Keyboard>/{i + 1}");
                keys[i].AddBinding($"<Keyboard>/numpad{i + 1}");
                keys[i].Enable();
            }
            turnLeft = new InputAction("Dial left", InputActionType.Value, "<Keyboard>/a");
            turnLeft.AddBinding("<Keyboard>/leftArrow");
            turnRight = new InputAction("Dial right", InputActionType.Value, "<Keyboard>/d");
            turnRight.AddBinding("<Keyboard>/rightArrow");
            turnLeft.Enable();
            turnRight.Enable();
            SetVisible(false);
        }

        void OnDestroy() {
            foreach (InputAction key in keys) key?.Dispose();
            turnLeft?.Dispose();
            turnRight?.Dispose();
        }

        // ---- From the controller ---------------------------------------------------------------------------------------------

        /// <summary>E at a station asks to open it; E in the panel is the minigame's action (cord, lock).</summary>
        public void Request(int station) {
            if (send == null || IsOpen) return;
            pendingStation = station;
            pendingUntil = Time.unscaledTime + 1.5f;
            send(CH_PuzzleAction.Open, station, 0f);
        }

        /// <summary>E while a station is open.</summary>
        public void Action() {
            if (!IsOpen) return;
            if (current.kind == CH_PuzzleKind.Engine) {
                Play(CH_Tones.Cord(), 1f);
                send(CH_PuzzleAction.Strike, stationId, 0f);
            } else if (current.kind == CH_PuzzleKind.BellLock) {
                send(CH_PuzzleAction.Set, stationId, dialAngle);
            }
        }

        /// <summary>R or walking off: step back from the station.</summary>
        public void Step() {
            if (!IsOpen) return;
            send(CH_PuzzleAction.Close, stationId, 0f);
        }

        /// <summary>The night changed under the panel (a new night, a cutscene): close it without a word.</summary>
        public void ForceClose() {
            IsOpen = false;
            pendingStation = -1;
            humSource.Stop();
            SetVisible(false);
        }

        public void OnBroadcast(CH_PuzzleBroadcast message) {
            current = message;
            switch (message.evt) {
                case CH_PuzzleEvent.Opened:
                    IsOpen = true;
                    stationId = message.stationId;
                    pendingStation = -1;
                    Layout(message);
                    SetVisible(true);
                    Sync(message);
                    break;
                case CH_PuzzleEvent.Progress:
                    Sync(message);
                    Flash(good, 0.25f);
                    SetPips(message.progress, message.total);
                    if (message.kind == CH_PuzzleKind.Engine) { Play(CH_Tones.Click(), 0.8f); status.text = "Caught. Again."; }
                    else if (message.kind == CH_PuzzleKind.BellLock) { Play(CH_Tones.Chain(), 0.7f); status.text = "One lock open."; dialAngle = UnityEngine.Random.Range(0f, 360f); lastProbedAngle = -1000f; }
                    else status.text = "Yes. Go on.";
                    break;
                case CH_PuzzleEvent.Mistake:
                    Sync(message);
                    Flash(bad, 0.5f);
                    SetPips(message.progress, message.total);
                    status.text = message.kind switch {
                        CH_PuzzleKind.Engine => "Flooded! He heard that.",
                        CH_PuzzleKind.Hymn => "Wrong note. He heard that.",
                        _ => "The chain clanked. He heard that."
                    };
                    if (message.kind == CH_PuzzleKind.Hymn) replayAt = Time.unscaledTime + replayAfterMistake;
                    else Play(CH_Tones.Chain(), 1f);
                    break;
                case CH_PuzzleEvent.Warmth:
                    warmth = message.warmth;
                    break;
                case CH_PuzzleEvent.Solved:
                    SetPips(message.total, message.total);
                    Flash(good, resultSeconds);
                    status.text = message.kind switch { CH_PuzzleKind.Engine => "She's running.", CH_PuzzleKind.Hymn => "They are free.", _ => "The bell is loose." };
                    if (message.kind == CH_PuzzleKind.Engine) Play(CH_Tones.Ignition(), 1f);
                    else Play(CH_Tones.Chain(), 1f);
                    End(resultSeconds);
                    break;
                case CH_PuzzleEvent.Interrupted:
                    Flash(bad, 0.6f);
                    status.text = string.IsNullOrEmpty(message.message) ? "He is coming!" : message.message;
                    End(0.9f);
                    break;
                default:
                    if (pendingStation >= 0) pendingStation = -1;
                    if (!IsOpen) { feedback = message.message; feedbackUntil = Time.unscaledTime + 2.5f; }
                    End(0f);
                    break;
            }
        }

        /// <summary>A refusal from the server (not now, move closer), for the controller's prompt.</summary>
        public string Feedback => Time.unscaledTime < feedbackUntil ? feedback : null;
        string feedback;
        float feedbackUntil;

        // ---- Showing ------------------------------------------------------------------------------------------------------

        void Layout(CH_PuzzleBroadcast message) {
            title.text = CH_Story.StationTitle(message.kind, StationSingerOf(message));
            hint.text = CH_Story.StationHint(message.kind);
            gaugeRoot.SetActive(message.kind != CH_PuzzleKind.Hymn);
            tilesRoot.SetActive(message.kind == CH_PuzzleKind.Hymn);
            heatRoot.SetActive(message.kind == CH_PuzzleKind.BellLock);
            zone.gameObject.SetActive(message.kind == CH_PuzzleKind.Engine);
            readout.text = "";
            status.text = message.kind switch { CH_PuzzleKind.Engine => "Wait for the needle...", CH_PuzzleKind.Hymn => "Listen...", _ => "Turn the dial" };
            SetPips(message.progress, message.total);
            warmth = 0f;
            dialAngle = UnityEngine.Random.Range(0f, 360f);
            lastProbedAngle = -1000f;
            litTile = -1;
            if (message.kind == CH_PuzzleKind.Hymn) StartPlayback(message.sequence, 0.8f);
            if (message.kind == CH_PuzzleKind.BellLock) { humSource.clip = CH_Tones.Hum(); humSource.volume = 0f; humSource.Play(); }
            else humSource.Stop();
        }

        /// <summary>The singer a hymn belongs to is carried by the station list the controller keeps; the title falls back to the bare kind.</summary>
        public Func<int, int> SingerOfStation { get; set; }

        int StationSingerOf(CH_PuzzleBroadcast message) => SingerOfStation != null ? SingerOfStation(message.stationId) : -1;

        void Sync(CH_PuzzleBroadcast message) {
            syncedAt = Time.unscaledTime;
            if (message.kind == CH_PuzzleKind.Engine && message.total > 0) {
                SetZone(message.zoneCenter, message.zoneWidth);
            }
        }

        void SetZone(float center, float width) {
            zone.anchorMin = new Vector2(center - width * 0.5f, 0f);
            zone.anchorMax = new Vector2(center + width * 0.5f, 1f);
            zone.offsetMin = zone.offsetMax = Vector2.zero;
        }

        void SetPips(int done, int total) {
            for (int i = 0; i < pips.Length; i++) {
                pips[i].gameObject.SetActive(i < total);
                pips[i].color = i < done ? pipDone : pipTodo;
            }
        }

        void SetVisible(bool visible) {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = false;
            gameObject.SetActive(true);
            group.gameObject.SetActive(visible);
        }

        void End(float after) {
            IsOpen = false;
            pendingStation = -1;
            humSource.Stop();
            resultUntil = Time.unscaledTime + after;
            if (after <= 0f) SetVisible(false);
        }

        void Flash(Color colour, float seconds) {
            flashColour = colour;
            flashUntil = Time.unscaledTime + seconds;
        }

        void Play(AudioClip clip, float gain) {
            if (clip != null) sound.PlayOneShot(clip, gain * volume);
        }

        // ---- Frame ----------------------------------------------------------------------------------------------------------

        void Update() {
            if (pendingStation >= 0 && Time.unscaledTime > pendingUntil) pendingStation = -1;
            if (!IsOpen) {
                if (group.gameObject.activeSelf && Time.unscaledTime >= resultUntil) SetVisible(false);
                return;
            }
            switch (current.kind) {
                case CH_PuzzleKind.Engine: UpdateEngine(); break;
                case CH_PuzzleKind.Hymn: UpdateHymn(); break;
                default: UpdateLock(); break;
            }
            UpdateFlash();
        }

        void UpdateFlash() {
            Color baseColour = idle;
            if (Time.unscaledTime < flashUntil) baseColour = Color.Lerp(idle, flashColour, 0.8f);
            foreach (Image tile in tiles) if (tile != null && litTile < 0) tile.color = baseColour;
        }

        void UpdateEngine() {
            float position = CH_PuzzleRun.NeedleAt(Time.unscaledTime - syncedAt, current.sweepSpeed);
            needle.anchorMin = needle.anchorMax = new Vector2(position, 0.5f);
            needle.anchoredPosition = Vector2.zero;
            needleImage.color = Time.unscaledTime < flashUntil ? flashColour : lit;
            float zoneLow = current.zoneCenter - current.zoneWidth * 0.5f, zoneHigh = current.zoneCenter + current.zoneWidth * 0.5f;
            bool inside = position >= zoneLow && position <= zoneHigh;
            zoneImage.color = inside ? Color.Lerp(good, Color.white, 0.35f) : good * 0.7f;
            readout.text = inside ? "NOW" : "";
        }

        void UpdateHymn() {
            // The hymn is played to you first (and again after a mistake); keys do nothing until it has finished.
            if (replayAt > 0f && Time.unscaledTime >= replayAt) { replayAt = -1f; StartPlayback(current.sequence, 0f); }
            bool listening = playPos >= 0;
            if (listening && Time.unscaledTime >= playNextAt) {
                if (playPos >= playing.Length) {
                    playPos = -1;
                    litTile = -1;
                    status.text = "Your turn. Keys 1 2 3 4";
                } else {
                    LightTile(playing[playPos], noteSeconds * 0.8f);
                    playPos++;
                    playNextAt = Time.unscaledTime + noteSeconds;
                }
            }
            if (litTile >= 0 && Time.unscaledTime >= litUntil) litTile = -1;
            for (int i = 0; i < tiles.Length; i++) tiles[i].color = i == litTile ? lit : (Time.unscaledTime < flashUntil ? Color.Lerp(idle, flashColour, 0.5f) : idle);
            if (listening || replayAt > 0f) return;
            for (int i = 0; i < keys.Length; i++) {
                if (!keys[i].WasPressedThisFrame()) continue;
                LightTile(i, 0.3f);
                send(CH_PuzzleAction.Press, stationId, i);
            }
        }

        void StartPlayback(int[] sequence, float delay) {
            playing = sequence ?? Array.Empty<int>();
            playPos = playing.Length > 0 ? 0 : -1;
            playNextAt = Time.unscaledTime + delay;
            litTile = -1;
            if (playPos >= 0) status.text = "Listen...";
        }

        void LightTile(int note, float seconds) {
            litTile = Mathf.Clamp(note, 0, tiles.Length - 1);
            litUntil = Time.unscaledTime + seconds;
            Play(CH_Tones.Note(litTile), 1f);
        }

        void UpdateLock() {
            float turn = (turnRight.ReadValue<float>() - turnLeft.ReadValue<float>()) * dialSpeed * Time.unscaledDeltaTime;
            dialAngle = Mathf.Repeat(dialAngle + turn, 360f);
            if (Time.unscaledTime >= nextProbe && Mathf.Abs(Mathf.DeltaAngle(dialAngle, lastProbedAngle)) > 0.4f) {
                nextProbe = Time.unscaledTime + probeInterval;
                lastProbedAngle = dialAngle;
                send(CH_PuzzleAction.Probe, stationId, dialAngle);
            }
            needle.anchorMin = needle.anchorMax = new Vector2(dialAngle / 360f, 0.5f);
            needle.anchoredPosition = Vector2.zero;
            heatFill.anchorMax = new Vector2(Mathf.Clamp01(warmth), 1f);
            heatFill.offsetMax = Vector2.zero;
            Color heat = Color.Lerp(coldDial, hotDial, warmth);
            heatImage.color = heat;
            needleImage.color = Time.unscaledTime < flashUntil ? flashColour : heat;
            readout.text = warmth > 0.93f ? "HOT: set it (E)" : warmth > 0.7f ? "warm" : warmth > 0.35f ? "cool" : "cold";
            humSource.volume = warmth * warmth * 0.7f * volume;
            humSource.pitch = 0.75f + warmth * 0.9f;
        }
    }
}
