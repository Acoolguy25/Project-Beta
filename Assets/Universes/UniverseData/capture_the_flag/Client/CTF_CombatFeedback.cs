using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// What a fight feels like in Flag Wars, all from server broadcasts: impact sparks on every
    /// confirmed hit, a hit marker, damage number and hit sound for the shooter, a kill sound and feed
    /// line for every kill, a damage pulse and low-health heartbeat for the local player, and a
    /// respawn countdown while dead. Decides nothing.
    /// </summary>
    public sealed class CTF_CombatFeedback : MonoBehaviour {
        [Header("Hit marker")]
        public HudPopup hitMarker;
        public Image hitMarkerIcon;
        public Color hitColor = Color.white;
        public Color killColor = new(1f, 0.3f, 0.25f);
        [Tooltip("Game seconds the marker stays up (game time, so slow motion keeps it on screen).")]
        [Min(0.05f)] public float hitMarkerSeconds = 0.22f;
        [Min(0f)] public float hitMarkerPunch = 0.35f;

        [Header("Kill feed")]
        public HudToastFeed killFeed;
        public Sprite pistolIcon, daggerIcon, fallIcon, skullIcon;
        [Min(0.5f)] public float killFeedSeconds = 5f;

        [Header("Respawn")]
        public HudPopup respawnCard;
        public TMP_Text respawnCount;
        public TMP_Text respawnBy;

        [Header("World effects")]
        [Tooltip("Metres above the victim's feet where impact sparks play (about chest height on the robot).")]
        [Min(0f)] public float impactHeight = 3.2f;
        [Min(0.1f)] public float impactScale = 1.6f;
        public Color impactTint = new(1f, 0.9f, 0.6f);
        [Tooltip("Metres above the victim's feet where damage numbers start.")]
        [Min(0f)] public float damageNumberHeight = 6f;
        [Min(0.1f)] public float damageNumberSize = 1.2f;
        [Tooltip("Height of the robot, for the puff where someone is eliminated.")]
        [Min(0.1f)] public float poofHeight = 5.6f;
        [Min(0.1f)] public float poofScale = 1.2f;

        [Header("Shake and screen")]
        [Range(0f, 1f)] public float killShake = 0.15f;
        [Range(0f, 1f)] public float deathFlashAlpha = 0.35f;
        [Tooltip("Damage pulse strength for losing all of your health at once (scaled by the share lost).")]
        [Min(0f)] public float damagePulseScale = 2f;

        ClientManager manager;
        LocalCharacter watched;
        float respawnAt, hitMarkerUntil;
        int shownSeconds = -1;

        int MyId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;

        void OnEnable() {
            LocalPlayer.OnCharacterAdded.Subscribe(OnLocalCharacter);
            LocalPlayer.OnCharacterRemoved += OnLocalCharacterRemoved;
        }

        void OnDisable() {
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnLocalCharacter);
            LocalPlayer.OnCharacterRemoved -= OnLocalCharacterRemoved;
            Unwatch();
            ScreenFx.SetLowHealth(1f);
        }

        void OnDestroy() {
            if (manager == null) return;
            manager.UnregisterBroadcast<CTF_HitBroadcast>(OnHit);
            manager.UnregisterBroadcast<CTF_KillBroadcast>(OnKill);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_HitBroadcast>(OnHit);
                manager.RegisterBroadcast<CTF_KillBroadcast>(OnKill);
            }
            if (hitMarkerUntil > 0f && Time.time >= hitMarkerUntil) {
                hitMarkerUntil = 0f;
                hitMarker.Hide();
            }
            if (respawnAt <= 0f) return;
            int seconds = Mathf.CeilToInt(respawnAt - Time.time);
            if (seconds <= 0) {
                HideRespawn();
                return;
            }
            if (seconds != shownSeconds) {
                shownSeconds = seconds;
                respawnCount.SetText("{0}", seconds);
            }
        }

        // ---------------------------------------------------------------- local player
        void OnLocalCharacter(LocalCharacter character) {
            Unwatch();
            if (character == null) return;
            watched = character;
            watched.Health.OnChange += OnLocalHealth;
            ScreenFx.SetLowHealth(1f);
            HideRespawn();
        }

        void OnLocalCharacterRemoved(LocalCharacter character) {
            if (character == watched) Unwatch();
        }

        void Unwatch() {
            if (watched != null) watched.Health.OnChange -= OnLocalHealth;
            watched = null;
        }

        void OnLocalHealth(long previous, long next, bool asServer) {
            if (asServer || watched == null) return;
            long max = watched.MaxHealth.Value;
            if (max <= 0) return;
            if (next < previous && next > 0) ScreenFx.DamagePulse(Mathf.Clamp01((previous - next) / (float)max) * damagePulseScale);
            ScreenFx.SetLowHealth(next <= 0 ? 1f : next / (float)max);
        }

        void HideRespawn() {
            respawnAt = 0f;
            shownSeconds = -1;
            respawnCard.Hide();
        }

        // ---------------------------------------------------------------- broadcasts
        void OnHit(CTF_HitBroadcast message, Channel channel) {
            GameFx.Play(FxKind.Sparks, message.position + Vector3.up * impactHeight, impactTint, impactScale);
            if (message.killed) GameFx.Poof(message.position, poofHeight, poofScale);
            if (message.attackerId != MyId || message.victimId == MyId) return;
            hitMarkerIcon.color = message.killed ? killColor : hitColor;
            hitMarker.Show();
            hitMarkerUntil = Time.time + hitMarkerSeconds;
            Punch.Scale(hitMarkerIcon.rectTransform, hitMarkerPunch, hitMarkerSeconds);
            FloatingText.Damage(message.position + Vector3.up * damageNumberHeight, message.damage, damageNumberSize);
            GameSfx.PlayAt(SfxCue.Hit, message.position);
        }

        void OnKill(CTF_KillBroadcast message, Channel channel) {
            int me = MyId;
            // Killers that are not players (a turret, a bot) have no id but still have a name.
            bool hasKiller = message.killerId >= 0 || !string.IsNullOrEmpty(message.killerName);
            string victim = Name(message.victimName, message.victimTeam, message.victimId == me);
            string line = hasKiller
                ? $"{Name(message.killerName, message.killerTeam, message.killerId == me)}  <color=#9AA3B2><size=85%>eliminated</size></color>  {victim}"
                : $"{victim}  <color=#9AA3B2><size=85%>{Fate(message.cause, message.victimId == me)}</size></color>";
            killFeed.Push(Icon(message.cause), CTF_Rules.Color(hasKiller ? message.killerTeam : message.victimTeam), line, killFeedSeconds);

            if (hasKiller && message.killerId == me) {
                GameSfx.Play(SfxCue.Kill);
                CameraShake.Kick(killShake);
            }
            if (message.victimId != me) return;
            ScreenFx.SetLowHealth(1f);
            ScreenFx.Flash(new Color(killColor.r, killColor.g, killColor.b, deathFlashAlpha));
            respawnBy.text = hasKiller
                ? $"Eliminated by {Name(message.killerName, message.killerTeam, false)}"
                : message.cause == CTF_KillCause.Fall ? "You fell off the map" : "You were eliminated";
            respawnAt = Time.time + message.respawnSeconds;
            shownSeconds = -1;
            respawnCard.Show();
        }

        static string Fate(CTF_KillCause cause, bool isMe) =>
            cause == CTF_KillCause.Fall ? "fell" : isMe ? "were eliminated" : "was eliminated";

        static string Name(string name, CTF_Team team, bool isMe) =>
            $"<color={CTF_Rules.Hex(team)}>{(isMe ? "You" : string.IsNullOrEmpty(name) ? "Someone" : name)}</color>";

        Sprite Icon(CTF_KillCause cause) => cause switch {
            CTF_KillCause.Gun => pistolIcon,
            CTF_KillCause.Melee => daggerIcon,
            CTF_KillCause.Fall => fallIcon,
            _ => skullIcon
        };
    }
}
