using System.Collections.Generic;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Plays the map's authored cutscenes (<see cref="CH_CutsceneSet"/>) on the main camera: letterbox bars, eased camera
    /// moves between marks, the priest and Mara as client-only puppets on a cleared stage (<see cref="CH_CutsceneStage"/>),
    /// sound cues and a title card. Runs after the first-person
    /// camera each frame so it wins, and before the game-feel kit so shakes still land. Movement input is off while a
    /// scene plays; the server holds the night for the same length.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class CH_CutsceneDirector : MonoBehaviour {
        [Header("Letterbox")]
        public RectTransform topBar;
        public RectTransform bottomBar;
        [Tooltip("Height of each bar as a fraction of the screen.")]
        [SerializeField, Range(0f, 0.3f)] float barFraction = 0.13f;
        [SerializeField, Min(0.05f)] float barSeconds = 0.6f;
        [Tooltip("The gameplay HUD, faded out while a scene plays.")]
        public CanvasGroup gameplayHud;

        [Header("Title card")]
        public CanvasGroup titleCard;
        public TMP_Text titleText;
        public TMP_Text subtitleText;
        [SerializeField, Min(0.1f)] float titleFadeSeconds = 1.2f;
        [Tooltip("The title card appears this many seconds before the scene ends.")]
        [SerializeField, Min(0.5f)] float titleLead = 4.5f;

        [Header("Fades")]
        [Tooltip("Full-screen black, used to cut between shots and to fade in and out of a scene.")]
        public CanvasGroup blackout;
        [SerializeField, Min(0f)] float cutFadeSeconds = 0.25f;

        [Tooltip("Seconds between checks for characters that spawned mid-scene, so they are cleared off the stage too.")]
        [SerializeField, Min(0.1f)] float stageCheckSeconds = 0.5f;

        [Header("Fill")]
        [Tooltip("A wide, shadowless light carried with the camera; a shot's fill sets how bright it is.")]
        public Light fillLight;
        [SerializeField, Min(0f)] float fillIntensity = 2.2f;
        [Tooltip("Seconds the fill takes to rise or fall between shots.")]
        [SerializeField, Min(0.01f)] float fillBlendSeconds = 0.6f;

        [Header("Cues")]
        public SfxSound thud = new();
        public SfxSound bell = new();
        public SfxSound scream = new();
        public SfxSound doors = new();
        public SfxSound engine = new();
        public SfxSound splash = new();

        CH_CutsceneSet set;
        CH_Puppet priest, mara;
        CH_CutsceneSet.Scene scene;
        // The ferry's mooring and what rides in it (the parts fitted aboard), put back when the scene ends.
        Pose ferryHome;
        bool ferryMoved;
        readonly List<Transform> riders = new();
        readonly List<Pose> riderHomes = new();
        float startedAt, length, defaultFov = -1f, letterbox;
        int lastShot = -1;
        float nextStageCheck;
        bool playing, inputTaken;
        Canvas barCanvas;
        readonly CH_CutsceneStage stage = new();

        public bool Playing => playing;

        /// <summary>Keeps the gameplay HUD faded out outside a scene too (looking out of a hiding place, where only the crack shows).</summary>
        public bool HideGameplayHud { get; set; }

        public void Bind(CH_CutsceneSet cutscenes, CH_Puppet priestPuppet, CH_Puppet maraPuppet) {
            set = cutscenes;
            priest = priestPuppet;
            mara = maraPuppet;
        }

        /// <summary>
        /// Starts the scene for <paramref name="phase"/>, <paramref name="secondsIn"/> seconds in (late joiners pick it up
        /// mid-way). Of <paramref name="aboardCandidates"/>, whatever lies in the ferry sails with it if the scene moves it.
        /// </summary>
        public void Play(CH_Phase phase, float secondsIn, IReadOnlyList<Transform> aboardCandidates = null) {
            if (set == null || !set.TryGet(phase, out scene)) { Stop(); return; }
            BoardFerry(aboardCandidates);
            startedAt = Time.unscaledTime - Mathf.Max(0f, secondsIn);
            length = CH_Story.CutsceneSeconds(phase);
            playing = true;
            lastShot = -1;
            if (!inputTaken) {
                InputService.SetInputScreenActive(InputScreen.GameMenu, true);
                inputTaken = true;
            }
            if (titleCard != null) titleCard.alpha = 0f;
            ShowTitle(scene.titleCard);
            stage.Clear();
            nextStageCheck = Time.unscaledTime + stageCheckSeconds;
        }

        public void Stop() {
            if (!playing && !inputTaken) return;
            playing = false;
            if (inputTaken) {
                InputService.SetInputScreenActive(InputScreen.GameMenu, false);
                inputTaken = false;
            }
            if (priest != null) priest.Hide();
            if (mara != null) mara.Hide();
            stage.Restore();
            DockFerry();
            Camera camera = Camera.main;
            if (camera != null && defaultFov > 0f) camera.fieldOfView = defaultFov;
            if (titleCard != null) titleCard.alpha = 0f;
            if (blackout != null) blackout.alpha = 0f;
            if (fillLight != null) { fillLight.intensity = 0f; fillLight.enabled = false; }
        }

        /// <summary>Carries the fill light with the camera, easing it toward this shot's <paramref name="fill"/>.</summary>
        void UpdateFill(Transform camera, float fill) {
            if (fillLight == null) return;
            fillLight.transform.SetPositionAndRotation(camera.position, camera.rotation);
            fillLight.intensity = Mathf.MoveTowards(fillLight.intensity, fill * fillIntensity, fillIntensity * Time.unscaledDeltaTime / fillBlendSeconds);
            fillLight.enabled = fillLight.intensity > 0.001f;
        }

        void ShowTitle(string card) {
            if (titleText == null) return;
            int split = string.IsNullOrEmpty(card) ? -1 : card.IndexOf('\n');
            titleText.text = split >= 0 ? card.Substring(0, split) : card ?? string.Empty;
            if (subtitleText != null) subtitleText.text = split >= 0 ? card.Substring(split + 1) : string.Empty;
        }

        void OnDisable() => Stop();

        void LateUpdate() {
            letterbox = Mathf.MoveTowards(letterbox, playing ? 1f : 0f, Time.unscaledDeltaTime / barSeconds);
            ApplyLetterbox();
            if (!playing) return;
            if (Time.unscaledTime >= nextStageCheck) {
                nextStageCheck = Time.unscaledTime + stageCheckSeconds;
                stage.Clear();
            }

            // Wall-clock time: the server ends the scene on its own clock, and a hitch must not leave the camera behind it.
            float elapsed = Time.unscaledTime - startedAt;
            int index = CH_CutsceneSet.ShotAt(scene, elapsed, out float progress);
            CH_CutsceneSet.Shot shot = scene.shots[index];
            if (index != lastShot) BeginShot(shot, index);
            // The ferry first: marks parented to it (Mara at the tiller, a camera on its bow) move with it.
            if (ferryMoved) PlaceFerry(FerryPose(index, progress));

            Camera camera = Camera.main;
            if (camera != null && shot.from != null) {
                if (defaultFov < 0f) defaultFov = camera.fieldOfView;
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                Vector3 position = shot.to != null ? Vector3.Lerp(shot.from.position, shot.to.position, eased) : shot.from.position;
                Quaternion rotation = shot.lookAt != null
                    ? Quaternion.LookRotation(shot.lookAt.position - position, Vector3.up)
                    : shot.to != null ? Quaternion.Slerp(shot.from.rotation, shot.to.rotation, eased) : shot.from.rotation;
                camera.transform.SetPositionAndRotation(position, rotation);
                if (shot.fieldOfView > 1f) camera.fieldOfView = shot.fieldOfView;
                UpdateFill(camera.transform, shot.fill);
            }
            if (priest != null) priest.Animate(shot.actorMark, shot.actorTarget, shot.actorPose, progress);
            if (mara != null) mara.Animate(shot.maraMark, shot.maraTarget, shot.maraPose, progress);

            // A short dip to black at each cut, and out of the last shot.
            float sinceCut = ShotElapsed(index, elapsed);
            float cutFade = cutFadeSeconds > 0f ? 1f - Mathf.Clamp01(sinceCut / cutFadeSeconds) : 0f;
            float endFade = Mathf.Clamp01((elapsed - (length - 0.8f)) / 0.8f);
            if (blackout != null) blackout.alpha = Mathf.Max(index == 0 ? 0f : cutFade, endFade);
            if (titleCard != null) {
                float titleTime = elapsed - (length - titleLead);
                titleCard.alpha = string.IsNullOrEmpty(scene.titleCard) ? 0f : Mathf.Clamp01(titleTime / titleFadeSeconds) * (1f - endFade);
            }
            // The first-person camera unlocks the cursor while input is off; a cutscene keeps it hidden.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (elapsed > length + 0.5f) Stop();
        }

        void BoardFerry(IReadOnlyList<Transform> candidates) {
            DockFerry();
            if (set.ferry == null || !CH_CutsceneSet.MovesFerry(scene)) return;
            ferryHome = new Pose(set.ferry.position, set.ferry.rotation);
            ferryMoved = true;
            if (set.ferryWake != null) set.ferryWake.Play(true);
            if (candidates == null) return;
            Bounds hull = new(ferryHome.position, Vector3.zero);
            bool any = false;
            foreach (Renderer part in set.ferry.GetComponentsInChildren<Renderer>()) {
                if (!any) { hull = part.bounds; any = true; } else hull.Encapsulate(part.bounds);
            }
            hull.Expand(new Vector3(0.2f, 1f, 0.2f));
            foreach (Transform candidate in candidates) {
                if (candidate == null || !hull.Contains(candidate.position)) continue;
                riders.Add(candidate);
                riderHomes.Add(new Pose(candidate.position, candidate.rotation));
            }
        }

        /// <summary>Puts the ferry and its riders back where they were moored.</summary>
        void DockFerry() {
            // Stop the wake before the hull jumps home, or it would lay foam all the way back across the lake.
            if (set != null && set.ferryWake != null) set.ferryWake.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (ferryMoved && set != null && set.ferry != null) PlaceFerry(ferryHome);
            ferryMoved = false;
            riders.Clear();
            riderHomes.Clear();
        }

        /// <summary>Where the ferry is <paramref name="progress"/> through shot <paramref name="index"/>: eased from where the last move left it.</summary>
        Pose FerryPose(int index, float progress) {
            Pose start = ferryHome;
            for (int i = index - 1; i >= 0; i--)
                if (scene.shots[i].ferryTarget != null) { start = new Pose(scene.shots[i].ferryTarget.position, scene.shots[i].ferryTarget.rotation); break; }
            Transform target = scene.shots[index].ferryTarget;
            if (target == null) return start;
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            return new Pose(Vector3.Lerp(start.position, target.position, eased), Quaternion.Slerp(start.rotation, target.rotation, eased));
        }

        void PlaceFerry(Pose pose) {
            set.ferry.SetPositionAndRotation(pose.position, pose.rotation);
            Quaternion turn = pose.rotation * Quaternion.Inverse(ferryHome.rotation);
            for (int i = 0; i < riders.Count; i++)
                if (riders[i] != null)
                    riders[i].SetPositionAndRotation(pose.position + turn * (riderHomes[i].position - ferryHome.position), turn * riderHomes[i].rotation);
        }

        float ShotElapsed(int index, float elapsed) {
            float start = 0f;
            for (int i = 0; i < index; i++) start += Mathf.Max(0.1f, scene.shots[i].seconds);
            return elapsed - start;
        }

        void BeginShot(CH_CutsceneSet.Shot shot, int index) {
            lastShot = index;
            if (shot.shake > 0f) CameraShake.Kick(shot.shake, 0.5f);
            SfxSound cue = shot.cue switch {
                "thud" => thud, "bell" => bell, "scream" => scream, "doors" => doors, "engine" => engine, "splash" => splash, _ => null
            };
            if (cue != null && cue.HasClips) GameSfx.Play(cue);
        }

        void Awake() {
            if (topBar != null) barCanvas = topBar.GetComponentInParent<Canvas>().rootCanvas;
        }

        void ApplyLetterbox() {
            float eased = Mathf.SmoothStep(0f, 1f, letterbox);
            // Bar heights are in canvas units; the canvas scales them to pixels.
            float scale = barCanvas != null ? Mathf.Max(0.01f, barCanvas.scaleFactor) : 1f;
            float height = Screen.height * barFraction * eased / scale;
            if (topBar != null) topBar.sizeDelta = new Vector2(topBar.sizeDelta.x, height);
            if (bottomBar != null) bottomBar.sizeDelta = new Vector2(bottomBar.sizeDelta.x, height);
            if (gameplayHud != null) gameplayHud.alpha = HideGameplayHud ? 0f : 1f - eased;
        }
    }
}
