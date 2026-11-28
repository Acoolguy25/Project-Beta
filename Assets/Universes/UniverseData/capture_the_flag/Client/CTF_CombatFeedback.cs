using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Declarations;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// What a fight feels like in Flag Wars, all from server broadcasts: impact sparks on every
    /// confirmed hit, a hit marker, damage number and hit sound for the shooter, a kill sound and feed
    /// line for every kill, a damage pulse and a low-health edge vignette for the local player, and a
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
        [Tooltip("Kill-feed icon per gun, in CTF_Weapon order (a gun kill shows the gun that made it).")]
        public Sprite[] weaponIcons = new Sprite[0];
        [Min(0.5f)] public float killFeedSeconds = 5f;
        [Tooltip("Colour of the verb between the names (\"eliminated\").")]
        public Color feedVerbColor = new(0.6f, 0.64f, 0.7f);
        [Tooltip("The mastery mark before a mastered gun's name in the feed.")]
        public Color masteryColor = new(1f, 0.8f, 0.25f);

        [Header("Respawn")]
        public HudPopup respawnCard;
        public TMP_Text respawnCount;
        public TMP_Text respawnBy;
        [Tooltip("The fight that killed you: hits each way this life and the range.")]
        public TMP_Text respawnFight;
        [Tooltip("The Payback nudge under it.")]
        public TMP_Text respawnPayback;
        [Tooltip("Kills closer than this (metres) read \"up close\" instead of a distance.")]
        [Min(0f)] public float closeRangeMetres = 8f;
        [Tooltip("You had them nearly beaten when you dealt at least this share of their health (\"So close!\").")]
        [Range(0f, 1f)] public float nearlyShare = 0.5f;
        [Tooltip("For the killer's marker and their body; the controller knows bots' bodies.")]
        public CTF_ClientController hud;
        public CTF_Waypoints waypoints;

        [Header("World effects")]
        [Tooltip("Metres above the victim's feet where impact sparks play (about chest height on the robot).")]
        [Min(0f)] public float impactHeight = 3.2f;
        [Min(0.1f)] public float impactScale = 1.6f;
        public Color impactTint = new(1f, 0.9f, 0.6f);
        [Tooltip("Metres above the victim's feet where damage numbers start: chest height, so the number " +
                 "rises beside the head and never sits on the name tag.")]
        [Min(0f)] public float damageNumberHeight = 3.4f;
        [Tooltip("Metres to the shooter's right of the victim, so the number does not cover the target.")]
        public float damageNumberSide = 1.4f;
        [Tooltip("Damage number look: a bit bigger and longer-lived than the kit default (0.8 s), so a " +
                 "one-shot-per-second pistol hit stays readable.")]
        public FloatingTextStyle damageNumberStyle = new(new Color(1f, 0.32f, 0.28f), 1.1f, 1.6f, 1.3f, 1.8f, 0.4f);
        [Tooltip("Height of the robot, for the puff where someone is eliminated.")]
        [Min(0.1f)] public float poofHeight = 5.6f;
        [Min(0.1f)] public float poofScale = 0.9f;
        [Tooltip("Smoke colour of that puff: a dark grey, so it reads as smoke and not white cotton.")]
        public Color poofTint = new(0.42f, 0.4f, 0.38f);

        [Header("Shake and screen")]
        [Range(0f, 1f)] public float killShake = 0.15f;
        [Range(0f, 1f)] public float deathFlashAlpha = 0.35f;
        [Tooltip("Damage pulse strength for losing all of your health at once (scaled by the share lost).")]
        [Min(0f)] public float damagePulseScale = 2f;
        [Tooltip("Share of max health at or below which the low-health warning beats. Hits deal half of max health, so 0.5 = one hit left.")]
        [Range(0.05f, 1f)] public float lowHealthAt = 0.5f;
        [Tooltip("The warning is the shared edge-only vignette (the centre of the view, where the enemy and the crosshair are, " +
                 "stays clear; try 1's heartbeat tinted the whole screen): its strength at lowHealthAt (rising toward 1 at 0).")]
        [Range(0.05f, 1f)] public float lowHealthStrength = 0.55f;
        public Color lowHealthColor = new(0.9f, 0.08f, 0.08f);
        [Range(0.05f, 1f)] public float lowHealthMaxAlpha = 0.45f;
        [Min(0f)] public float lowHealthPulsesPerSecond = 1.4f;

        ClientManager manager;
        LocalCharacter watched;
        float respawnAt, hitMarkerUntil;
        readonly CTF_DeathRecap recap = new();
        int killerId = -1;
        CTF_Team killerTeam;
        Vector3 deathAt;
        long knownMaxHealth;
        int shownSeconds = -1;
        string verbHex, masteryHex;
        int lastDamage = int.MinValue;
        string lastDamageText;

        /// <summary>Every pistol and dagger hit deals the same damage, so the text is built once.</summary>
        string DamageText(int damage) {
            if (damage != lastDamage) {
                lastDamage = damage;
                lastDamageText = damage.ToString();
            }
            return lastDamageText;
        }

        void Awake() {
            verbHex = "#" + ColorUtility.ToHtmlStringRGB(feedVerbColor);
            masteryHex = "#" + ColorUtility.ToHtmlStringRGB(masteryColor);
        }

        int MyId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;

        void OnEnable() {
            LocalPlayer.OnCharacterAdded.Subscribe(OnLocalCharacter);
            LocalPlayer.OnCharacterRemoved += OnLocalCharacterRemoved;
        }

        void OnDisable() {
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnLocalCharacter);
            LocalPlayer.OnCharacterRemoved -= OnLocalCharacterRemoved;
            Unwatch();
            SetLowHealth(1f);
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
            MarkKiller();
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
            SetLowHealth(1f);
            HideRespawn();
            recap.Clear();
            knownMaxHealth = character.MaxHealth.Value;
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
            knownMaxHealth = max;
            if (next < previous && next > 0) ScreenFx.DamagePulse(Mathf.Clamp01((previous - next) / (float)max) * damagePulseScale);
            SetLowHealth(next / (float)max);
        }

        /// <summary>One hit left: an edge vignette beats (never the centre-filling heartbeat).</summary>
        void SetLowHealth(float health01) {
            float strength = CTF_Rules.LowHealthEdge(health01, lowHealthAt, lowHealthStrength);
            CTF_EdgeVignette.Set(CTF_EdgeVignette.Source.LowHealth, strength, lowHealthColor, lowHealthMaxAlpha, lowHealthPulsesPerSecond);
        }

        void HideRespawn() {
            respawnAt = 0f;
            shownSeconds = -1;
            killerId = -1;
            respawnCard.Hide();
            if (waypoints != null) waypoints.SetKiller(false, Vector3.zero, null, Color.white);
        }

        /// <summary>While you wait, a skull marker sits over whoever eliminated you (through walls), for the Payback.</summary>
        void MarkKiller() {
            if (waypoints == null) return;
            Transform body = killerId >= 0 && hud != null ? hud.ActorBody(killerId) : null;
            waypoints.SetKiller(body != null, body != null ? body.position : Vector3.zero, skullIcon, CTF_Rules.Color(killerTeam));
        }

        // ---------------------------------------------------------------- broadcasts
        void OnHit(CTF_HitBroadcast message, Channel channel) {
            int me = MyId;
            recap.Hit(me, message.attackerId, message.victimId, message.damage);
            if (message.victimId == me && message.killed) deathAt = message.position;
            GameFx.Play(FxKind.Sparks, message.position + Vector3.up * impactHeight, impactTint, impactScale);
            if (message.killed) GameFx.Poof(message.position, poofHeight, poofTint, poofScale);
            if (message.attackerId != me || message.victimId == me) return;
            hitMarkerIcon.color = message.killed ? killColor : hitColor;
            hitMarker.Show();
            hitMarkerUntil = Time.time + hitMarkerSeconds;
            Punch.Scale(hitMarkerIcon.rectTransform, hitMarkerPunch, hitMarkerSeconds);
            Camera view = Camera.main;
            Vector3 side = view != null ? view.transform.right : Vector3.zero;
            FloatingText.Spawn(message.position + Vector3.up * damageNumberHeight + side * damageNumberSide,
                DamageText(message.damage), damageNumberStyle);
            // 2D: the shooter must hear the confirm at any range.
            GameSfx.Play(SfxCue.Hit);
        }

        void OnKill(CTF_KillBroadcast message, Channel channel) {
            int me = MyId;
            // Bots have actor ids like players; a killer outside the match (a turret) has no id but still a name.
            bool hasKiller = message.killerId >= 0 || !string.IsNullOrEmpty(message.killerName);
            string victim = Name(message.victimName, message.victimTeam, message.victimId == me);
            // A gun kill names the gun (bots carry all four from Regular up): "Rivet  [Scattergun]  You".
            string verb = hasKiller ? CTF_Rules.KillVerb(message.cause, CTF_Loadouts.Get(message.weapon).title) : "";
            // A gun its killer mastered (50 kills with it) carries a gold check: "Rivet  ✓ [Scattergun]  You".
            if (hasKiller && message.killerMastered && message.cause == CTF_KillCause.Gun) verb = $"<color={masteryHex}>{CTF_Goals.MasteryMark}</color> {verb}";
            string line = hasKiller
                ? $"{Name(message.killerName, message.killerTeam, message.killerId == me)}  <color={verbHex}><size=85%>{verb}</size></color>  {victim}"
                : $"{victim}  <color={verbHex}><size=85%>{Fate(message.cause, message.victimId == me)}</size></color>";
            killFeed.Push(KillIcon(message), CTF_Rules.Color(hasKiller ? message.killerTeam : message.victimTeam), line, killFeedSeconds);

            if (hasKiller && message.killerId == me) {
                GameSfx.Play(SfxCue.Kill);
                CameraShake.Kick(killShake);
            }
            if (message.victimId != me) return;
            SetLowHealth(1f);
            ScreenFx.Flash(new Color(killColor.r, killColor.g, killColor.b, deathFlashAlpha));
            respawnBy.text = hasKiller
                ? $"Eliminated by {Name(message.killerName, message.killerTeam, false, startsSentence: false)}" +
                  (message.cause == CTF_KillCause.Gun ? $" <color={verbHex}>· {CTF_Loadouts.Get(message.weapon).title}</color>" : "")
                : message.cause == CTF_KillCause.Fall ? "You fell off the map" : "You were eliminated";
            ShowRecap(message, hasKiller);
            respawnAt = Time.time + message.respawnSeconds;
            shownSeconds = -1;
            respawnCard.Show();
        }

        /// <summary>The fight line, the range and the Payback nudge under "Eliminated by".</summary>
        void ShowRecap(in CTF_KillBroadcast message, bool hasKiller) {
            bool enemy = hasKiller && message.killerId >= 0 && message.killerId != MyId && !IsMyTeam(message.killerTeam);
            killerId = enemy ? message.killerId : -1;
            killerTeam = message.killerTeam;
            if (respawnFight != null) {
                string fight = enemy ? CTF_DeathRecap.FightLine(recap.Get(message.killerId)) : "";
                Transform body = enemy && hud != null ? hud.ActorBody(message.killerId) : null;
                if (body != null && deathAt != Vector3.zero) {
                    string range = CTF_DeathRecap.RangeText(Vector3.Distance(deathAt, body.position), closeRangeMetres);
                    fight = fight.Length > 0 ? $"{fight}\n{range}" : range;
                }
                respawnFight.text = fight;
                respawnFight.gameObject.SetActive(fight.Length > 0);
            }
            if (respawnPayback != null) {
                // Everyone spawns with the same health, so your own max stands in for theirs.
                string line = enemy ? CTF_DeathRecap.PaybackLine(recap.Get(message.killerId), knownMaxHealth, nearlyShare, CTF_MedalTracker.Get(CTF_Medal.Payback).xp) : "";
                respawnPayback.text = line;
                respawnPayback.gameObject.SetActive(line.Length > 0);
            }
            deathAt = Vector3.zero;
        }

        static string Fate(CTF_KillCause cause, bool isMe) =>
            cause == CTF_KillCause.Fall ? "fell" : isMe ? "were eliminated" : "was eliminated";

        static string Name(string name, CTF_Team team, bool isMe, bool startsSentence = true) =>
            $"<color={CTF_Rules.Hex(team)}>{CTF_Rules.PlayerLabel(name, isMe, IsMyTeam(team), startsSentence)}</color>";

        /// <summary>Whether <paramref name="team"/> is the local player's team (from their character).</summary>
        static bool IsMyTeam(CTF_Team team) {
            var me = LocalPlayer.Character;
            return me != null && me.GetTeam().realTeam == (team == CTF_Team.Red ? TeamColor.Red : TeamColor.Blue);
        }

        Sprite KillIcon(in CTF_KillBroadcast message) {
            int i = (int)message.weapon;
            bool gun = message.cause == CTF_KillCause.Gun && weaponIcons != null && i >= 0 && i < weaponIcons.Length && weaponIcons[i] != null;
            return gun ? weaponIcons[i] : Icon(message.cause);
        }

        Sprite Icon(CTF_KillCause cause) => cause switch {
            CTF_KillCause.Gun => pistolIcon,
            CTF_KillCause.Melee => daggerIcon,
            CTF_KillCause.Fall => fallIcon,
            _ => skullIcon
        };
    }
}
