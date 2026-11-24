using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Rounds;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// How abilities look and sound on this client, from the server's <see cref="HS_AbilityBroadcast"/>:
    /// <list type="bullet">
    /// <item>Dash / Rush: speed lines and a FOV kick for the user, a dust puff and a whoosh for everyone else
    /// (and a trail behind owners of Ghost Trail).</item>
    /// <item>Decoy: only its thrower gets this message, and a marker where it landed for as long as it is live.
    /// Everyone else gets a real taunt from the server, drawn by <see cref="HS_ClientController"/>.</item>
    /// <item>Blend In: seekers stop drawing the hider (only the shadow stays) until it ends, also if they become
    /// a seeker or join while it runs; the hider gets a cold edge tint and a line above the bar.</item>
    /// <item>Radar: a pulse everyone sees; the seeker also gets beacons on what it picked up.</item>
    /// <item>Flashlight: a beam everyone sees (light and cone, sized to what the server tested); dazzled
    /// hiders are whited out, and seekers see them marked through walls.</item>
    /// </list>
    /// Results of your own abilities show on the line above the bar (<see cref="HS_AbilityBar.Say"/>), not mid-screen.
    /// </summary>
    public sealed class HS_AbilityFeel : MonoBehaviour {
        [SerializeField] HS_ClientController controller;
        [SerializeField] HS_AbilityBar bar;
        [Tooltip("Full-screen edge tint shown to the hider while they are blended.")]
        [SerializeField] CanvasGroup blendVignette;

        [Header("Sounds")]
        [SerializeField] SfxSound dashSound = new();
        [SerializeField] SfxSound blendSound = new();
        [SerializeField] SfxSound radarSound = new();
        [SerializeField] SfxSound flashSound = new();
        [SerializeField] SfxSound dazzledSound = new();

        [Header("Colours")]
        [SerializeField] Color dashColor = new(0.55f, 0.85f, 1f, 1f);
        [SerializeField] Color decoyColor = new(1f, 0.72f, 0.2f, 1f);
        [SerializeField] Color radarColor = new(0.3f, 0.9f, 1f, 1f);
        [SerializeField] Color flashColor = new(1f, 0.96f, 0.75f, 1f);
        [SerializeField] Color blindedFlash = new(1f, 1f, 0.94f, 1f);
        [Tooltip("Ghost Trail: the trail behind an owner's dash or rush.")]
        [SerializeField] Color trailColor = new(0.75f, 0.45f, 1f, 1f);

        [Header("Dash and rush")]
        [SerializeField, Range(0f, 1f)] float speedLines = 0.7f;
        [SerializeField, Min(0f)] float fovKickDegrees = 8f;
        [SerializeField, Min(0.1f)] float dustScale = 1.6f;
        [Tooltip("Height of the Ghost Trail above the feet (world units).")]
        [SerializeField, Min(0f)] float trailHeight = 2.4f;
        [SerializeField, Min(0.1f)] float flairTrailScale = 2f;

        [Header("Decoy (the thrower's view)")]
        [SerializeField, Min(0.1f)] float decoyRingScale = 2.5f;

        [Header("Blend In (the hider's view)")]
        [SerializeField, Range(0f, 1f)] float blendVignetteAlpha = 0.35f;
        [SerializeField, Min(0.01f)] float blendFadeSeconds = 0.25f;

        [Header("Radar")]
        [Tooltip("Ring size per world unit of radar radius.")]
        [SerializeField, Min(0f)] float radarRingPerUnit = 0.08f;
        [SerializeField, Min(0.1f)] float radarBeaconScale = 2.2f;

        [Header("Flashlight")]
        [SerializeField, Min(0.05f)] float userFlashSeconds = 0.2f;
        [SerializeField, Range(0f, 1f)] float userFlashAlpha = 0.18f;
        [Tooltip("The dazzled hider's whiteout: it starts fully white and fades over this long.")]
        [SerializeField, Min(0.05f)] float blindedSeconds = 1.6f;
        [SerializeField, Range(0f, 1f)] float blindedShake = 0.5f;
        [Tooltip("The '!' over a dazzled hider that seekers see (world units above the feet).")]
        [SerializeField] float revealMarkHeight = 6.5f;
        [SerializeField, Min(0.1f)] float revealMarkSize = 1.2f;
        [SerializeField, Min(0.1f)] float trailScale = 1.5f;

        [Header("Messages above the bar")]
        [SerializeField, Min(0.5f)] float messageSeconds = 2.5f;

        sealed class Blended {
            public Renderer[] renderers;
            public ShadowCastingMode[] modes;
        }

        ClientManager manager;
        /// <summary>Every other player's running blend (client id: when it ends), whatever the local role.</summary>
        readonly Dictionary<int, float> blends = new();
        /// <summary>The blended hiders this client stops drawing right now (only while it seeks).</summary>
        readonly Dictionary<int, Blended> hidden = new();
        readonly List<(FxHandle fx, float until)> timed = new();
        readonly List<HS_DecoyMarker> markers = new();
        readonly List<int> scratch = new();
        float speedLinesUntil = -1f, myBlendUntil = -1f;

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<HS_AbilityBroadcast>(OnAbility);
            foreach (int id in new List<int>(hidden.Keys)) Reveal(id);
            foreach (var (fx, _) in timed) fx.Stop();
            timed.Clear();
            ClearMarkers();
            if (speedLinesUntil >= 0f) ScreenFx.SetSpeedLines(0f);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<HS_AbilityBroadcast>(OnAbility);
            }
            float now = Time.unscaledTime;
            if (speedLinesUntil >= 0f && now >= speedLinesUntil) {
                speedLinesUntil = -1f;
                ScreenFx.SetSpeedLines(0f);
            }
            for (int i = timed.Count - 1; i >= 0; i--) {
                if (now < timed[i].until) continue;
                timed[i].fx.Stop();
                timed.RemoveAt(i);
            }
            bool inRound = controller.HasState && (controller.Phase is HS_Phase.Hiding or HS_Phase.Seeking);
            if (!inRound && markers.Count > 0) ClearMarkers();
            markers.RemoveAll(m => m == null);
            RefreshBlends(now, inRound);
            RefreshMyBlend(now, inRound);
        }

        bool Mine(HS_AbilityBroadcast message) => message.clientId == controller.LocalClientId;
        bool LocalSeeks => HS_Abilities.SeekerSide(controller.LocalRole);

        void OnAbility(HS_AbilityBroadcast message, Channel channel) {
            switch (message.ability) {
                case HS_Ability.Dash:
                case HS_Ability.Rush:
                    OnSpeedBurst(message);
                    break;
                case HS_Ability.Decoy:
                    OnDecoy(message);
                    break;
                case HS_Ability.Blend:
                    OnBlend(message);
                    break;
                case HS_Ability.Radar:
                    OnRadar(message);
                    break;
                case HS_Ability.Flashlight:
                    OnFlashlight(message);
                    break;
            }
        }

        void Say(string text, float seconds = -1f) => bar.Say(text, seconds > 0f ? seconds : messageSeconds);

        // ---------------------------------------------------------------- dash and rush
        void OnSpeedBurst(HS_AbilityBroadcast message) {
            if (message.flair) {
                LocalCharacter user = CharacterLookup.ByClientId(message.clientId);
                if (user != null)
                    timed.Add((GameFx.Attach(FxKind.Trail, user.transform, Vector3.up * trailHeight, trailColor, flairTrailScale), Time.unscaledTime + message.seconds));
            }
            if (Mine(message)) {
                GameSfx.Play(dashSound);
                ScreenFx.SetSpeedLines(speedLines);
                CameraFov.Kick(fovKickDegrees, message.seconds);
                speedLinesUntil = Time.unscaledTime + message.seconds;
                return;
            }
            GameSfx.PlayAt(dashSound, message.position);
            GameFx.Play(FxKind.DustPuff, message.position, dashColor, dustScale);
        }

        // ---------------------------------------------------------------- decoy (only the thrower gets this)
        void OnDecoy(HS_AbilityBroadcast message) {
            GameFx.Play(FxKind.ShockwaveRing, message.position, decoyColor, decoyRingScale);
            GameObject prefab = controller.Map != null ? controller.Map.decoyMarkerPrefab : null;
            if (prefab != null) {
                var marker = Instantiate(prefab, message.position, Quaternion.identity).GetComponent<HS_DecoyMarker>();
                if (marker != null) {
                    marker.Place(message.position, message.seconds);
                    markers.Add(marker);
                }
            }
            Say("Decoy thrown: the seekers hear a taunt there.");
        }

        void ClearMarkers() {
            foreach (HS_DecoyMarker marker in markers) if (marker != null) marker.End();
            markers.Clear();
        }

        // ---------------------------------------------------------------- blend in
        void OnBlend(HS_AbilityBroadcast message) {
            float now = Time.unscaledTime;
            if (Mine(message)) {
                if (message.seconds <= 0f) {
                    if (myBlendUntil >= 0f) Say("You're visible again.");
                    myBlendUntil = -1f;
                    return;
                }
                GameSfx.Play(blendSound);
                myBlendUntil = now + message.seconds;
                Say("Blended in: seekers see only your shadow. Press 3 to move.", message.seconds);
                return;
            }
            if (message.seconds <= 0f) blends.Remove(message.clientId);
            else blends[message.clientId] = now + message.seconds;
        }

        /// <summary>
        /// Hides each blended hider while the local player seeks, and shows them again when the blend ends,
        /// the hider is found or the round moves on. Checked every frame, so a hider who is tagged while a
        /// blend runs, or a seeker who joins mid-blend, sees the same as everyone else on their side.
        /// </summary>
        void RefreshBlends(float now, bool inRound) {
            scratch.Clear();
            foreach (var pair in blends)
                if (now >= pair.Value || !inRound || !controller.RoleTable.TryGetValue(pair.Key, out HS_Role role) || role != HS_Role.Hider) scratch.Add(pair.Key);
            foreach (int id in scratch) blends.Remove(id);
            bool seeks = inRound && LocalSeeks;
            scratch.Clear();
            foreach (var pair in hidden) if (!seeks || !blends.ContainsKey(pair.Key)) scratch.Add(pair.Key);
            foreach (int id in scratch) Reveal(id);
            if (!seeks) return;
            foreach (var pair in blends) {
                if (hidden.ContainsKey(pair.Key)) continue;
                LocalCharacter hider = CharacterLookup.ByClientId(pair.Key);
                if (hider != null) Hide(pair.Key, hider);
            }
        }

        /// <summary>The blended hider's own view: a cold edge tint for as long as it lasts.</summary>
        void RefreshMyBlend(float now, bool inRound) {
            bool blended = myBlendUntil >= 0f && now < myBlendUntil && inRound && controller.LocalRole == HS_Role.Hider;
            if (!blended && myBlendUntil >= 0f && (!inRound || controller.LocalRole != HS_Role.Hider)) myBlendUntil = -1f;
            if (blendVignette == null) return;
            float target = blended ? blendVignetteAlpha : 0f;
            if (Mathf.Approximately(blendVignette.alpha, target)) return;
            blendVignette.alpha = Mathf.MoveTowards(blendVignette.alpha, target, Time.unscaledDeltaTime / blendFadeSeconds);
        }

        /// <summary>Stops drawing a blended hider for this client (shadows keep casting, so a sharp seeker can still spot them).</summary>
        void Hide(int clientId, LocalCharacter hider) {
            var renderers = hider.GetComponentsInChildren<Renderer>();
            var state = new Blended { renderers = renderers, modes = new ShadowCastingMode[renderers.Length] };
            for (int i = 0; i < renderers.Length; i++) {
                state.modes[i] = renderers[i].shadowCastingMode;
                if (renderers[i] is ParticleSystemRenderer) continue;
                renderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            hidden[clientId] = state;
        }

        void Reveal(int clientId) {
            if (!hidden.Remove(clientId, out Blended state)) return;
            for (int i = 0; i < state.renderers.Length; i++)
                if (state.renderers[i] != null) state.renderers[i].shadowCastingMode = state.modes[i];
        }

        // ---------------------------------------------------------------- radar
        void OnRadar(HS_AbilityBroadcast message) {
            GameFx.Play(FxKind.ShockwaveRing, message.position, radarColor, Mathf.Max(1f, message.range * radarRingPerUnit));
            if (!Mine(message)) {
                GameSfx.PlayAt(radarSound, message.position);
                return;
            }
            GameSfx.Play(radarSound);
            int found = message.positions?.Length ?? 0;
            float until = Time.unscaledTime + Mathf.Max(0.5f, message.seconds);
            if (message.positions != null)
                foreach (Vector3 p in message.positions) timed.Add((GameFx.Start(FxKind.BeaconBeam, p, radarColor, radarBeaconScale), until));
            Say(found == 0 ? "Radar: nothing close by." : found == 1 ? "Radar: 1 contact nearby!" : $"Radar: {found} contacts nearby!");
        }

        // ---------------------------------------------------------------- flashlight
        void OnFlashlight(HS_AbilityBroadcast message) {
            bool mine = Mine(message);
            GameObject prefab = controller.Map != null ? controller.Map.flashlightBeamPrefab : null;
            if (prefab != null) {
                var beam = Instantiate(prefab).GetComponent<HS_FlashlightBeam>();
                if (beam != null) beam.Fire(message.position, message.direction, message.range, message.angle);
            }
            if (mine) {
                GameSfx.Play(flashSound);
                ScreenFx.Flash(new Color(flashColor.r, flashColor.g, flashColor.b, userFlashAlpha), userFlashSeconds);
            } else GameSfx.PlayAt(flashSound, message.position);
            int[] targets = message.targets ?? System.Array.Empty<int>();
            foreach (int id in targets) {
                if (id == controller.LocalClientId) {
                    GameSfx.Play(dazzledSound);
                    ScreenFx.Flash(blindedFlash, blindedSeconds);
                    CameraShake.Kick(blindedShake, blindedSeconds);
                    Say("Blinded! You're slowed and the seekers can see you.", message.seconds);
                    myBlendUntil = -1f;
                    continue;
                }
                LocalCharacter hider = CharacterLookup.ByClientId(id);
                if (hider == null) continue;
                // The beam ends a blend: draw the hider again at once (the server's blend-end message follows).
                blends.Remove(id);
                Reveal(id);
                HitFlash.On(hider, flashColor);
                if (!LocalSeeks) continue;
                // Seekers see who was caught in the beam, even through a wall, for as long as the dazzle lasts.
                FloatingText.Callout(hider.transform.position + Vector3.up * revealMarkHeight, "!", flashColor, revealMarkSize, message.seconds);
                timed.Add((GameFx.Attach(FxKind.Trail, hider.transform, Vector3.up * revealMarkHeight * 0.5f, flashColor, trailScale), Time.unscaledTime + message.seconds));
            }
            if (mine) Say(targets.Length == 0 ? "The beam hit nobody." : targets.Length == 1 ? "Dazzled a hider: they're marked and slowed!" : $"Dazzled {targets.Length} hiders!");
        }
    }
}
