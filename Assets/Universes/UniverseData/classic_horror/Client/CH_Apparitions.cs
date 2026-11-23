using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The priest where he cannot be, for one investigator at a time (the server decides when):
    /// <list type="bullet">
    /// <item><b>Apparition:</b> a whisper right behind you; turn round and he is there, an arm's length away, for an instant.</item>
    /// <item><b>Watcher:</b> he stands far ahead in the dark, watching; when your light finds him he is gone.</item>
    /// </list>
    /// He is a client-only actor; nothing here can hurt you, which you only learn afterwards.
    /// </summary>
    public sealed class CH_Apparitions : MonoBehaviour {
        public CH_PriestActor actor;
        public CH_VoicePlayer voice;
        public CH_Jumpscare jumpscare;
        public SfxSound revealSting = new();
        public SfxSound fadeSting = new();

        [Header("Behind you")]
        [SerializeField] Vector2 behindDistance = new(1.8f, 2.6f);
        [Tooltip("How directly you must face him for him to be seen (dot product).")]
        [SerializeField, Range(0f, 1f)] float faceAlignment = 0.72f;
        [SerializeField, Min(0.05f)] float revealSeconds = 0.18f;
        [SerializeField, Min(1f)] float behindTimeout = 7f;

        [Header("Watcher")]
        [SerializeField] Vector2 watcherDistance = new(16f, 26f);
        [SerializeField] Vector2 watcherSideAngle = new(10f, 22f);
        [Tooltip("He is gone once the beam or your gaze is this close to him (degrees) for watcherNoticeSeconds.")]
        [SerializeField, Range(1f, 30f)] float watcherNoticeAngle = 9f;
        [SerializeField, Min(0f)] float watcherNoticeSeconds = 0.35f;
        [SerializeField, Min(1f)] float watcherTimeout = 12f;

        enum Mode { None, Behind, Watcher, Revealed }

        Mode mode;
        float startedAt, revealedAt, noticedFor;
        int sequence;
        int mask;
        CH_Atmosphere atmosphere;

        public bool Active => mode != Mode.None;

        void Awake() {
            mask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
            atmosphere = GetComponent<CH_Atmosphere>();
        }

        public void ResetNight() => End();

        public void Play(CH_ScareKind kind, int scareSequence) {
            if (actor == null || mode != Mode.None) return;
            sequence = scareSequence;
            Camera camera = Camera.main;
            if (camera == null) return;
            bool placed = kind == CH_ScareKind.Apparition ? PlaceBehind(camera) : PlaceWatcher(camera);
            if (!placed) return;
            mode = kind == CH_ScareKind.Apparition ? Mode.Behind : Mode.Watcher;
            startedAt = Time.time;
            noticedFor = 0f;
            if (mode == Mode.Behind && voice != null)
                voice.Play(new CH_VoiceBroadcast { line = CH_Story.Whispers[Random.Range(0, 2)], mode = CH_VoiceMode.World, position = actor.transform.position + Vector3.up * 1.6f });
        }

        bool PlaceBehind(Camera camera) {
            Vector3 back = -Flat(camera.transform.forward).normalized;
            for (int attempt = 0; attempt < 6; attempt++) {
                Vector3 direction = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * back;
                Vector3 eye = camera.transform.position;
                float distance = Random.Range(behindDistance.x, behindDistance.y);
                if (Physics.Raycast(eye, direction, distance + 0.4f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(eye + direction * distance, Vector3.down, out RaycastHit ground, 4f, mask, QueryTriggerInteraction.Ignore)) continue;
                actor.Place(ground.point, -direction);
                return true;
            }
            return false;
        }

        bool PlaceWatcher(Camera camera) {
            Vector3 forward = Flat(camera.transform.forward).normalized;
            for (int attempt = 0; attempt < 10; attempt++) {
                float side = Random.Range(watcherSideAngle.x, watcherSideAngle.y) * (Random.value < 0.5f ? -1f : 1f);
                Vector3 direction = Quaternion.Euler(0f, side, 0f) * forward;
                float distance = Random.Range(watcherDistance.x, watcherDistance.y);
                Vector3 eye = camera.transform.position;
                Vector3 spot = eye + direction * distance;
                if (!Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, mask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 head = ground.point + Vector3.up * 1.7f;
                // He must be in plain view, just not where you are looking.
                if (Physics.Linecast(eye, head, mask, QueryTriggerInteraction.Ignore)) continue;
                actor.Place(ground.point, eye - ground.point);
                return true;
            }
            return false;
        }

        void Update() {
            if (mode == Mode.None) return;
            Camera camera = Camera.main;
            if (camera == null) { End(); return; }
            Vector3 head = actor.transform.position + Vector3.up * 1.6f;
            Vector3 toHim = head - camera.transform.position;
            float alignment = Vector3.Dot(camera.transform.forward, toHim.normalized);
            switch (mode) {
                case Mode.Behind:
                    // He turns with you, so he is always just behind your shoulder until you face him.
                    Vector3 lookingFrom = actor.transform.position - camera.transform.position;
                    actor.Place(actor.transform.position, -Flat(lookingFrom));
                    if (alignment > faceAlignment && !Physics.Linecast(camera.transform.position, head, mask, QueryTriggerInteraction.Ignore)) Reveal(true);
                    else if (Time.time - startedAt > behindTimeout) End();
                    break;
                case Mode.Watcher:
                    float angle = Vector3.Angle(camera.transform.forward, toHim);
                    noticedFor = angle < watcherNoticeAngle ? noticedFor + Time.deltaTime : 0f;
                    if (noticedFor >= watcherNoticeSeconds) {
                        GameSfx.Play(fadeSting);
                        atmosphere?.LightsOut(0.35f);
                        End();
                    } else if (Time.time - startedAt > watcherTimeout) End();
                    break;
                case Mode.Revealed:
                    if (Time.time - revealedAt > revealSeconds) End();
                    break;
            }
        }

        void Reveal(bool face) {
            mode = Mode.Revealed;
            revealedAt = Time.time;
            GameSfx.Play(revealSting);
            CameraShake.Kick(0.5f, 0.35f);
            if (face && jumpscare != null) jumpscare.Play(sequence, CH_ScareKind.Apparition, force: true);
        }

        void End() {
            mode = Mode.None;
            if (actor != null) actor.Hide();
        }

        static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);
    }
}
