using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Combat;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>How one Flag Wars gun looks and sounds (one row of <see cref="CTF_GunLooks.looks"/>).</summary>
    [Serializable]
    public sealed class CTF_GunLook {
        public CTF_Weapon weapon;
        [Tooltip("The held model: built by the HUD authoring from the shared gun pack, posed in the pistol tool's weapon-root space. None keeps the pistol.")]
        public GameObject model;
        [Tooltip("Where the muzzle sits with the model on, in the weapon root's space (the flash and tracers start here).")]
        public Vector3 muzzle;
        [Tooltip("Hotbar and ammo-panel icon for the gun (the loadout card's icon).")]
        public Sprite icon;
        [Header("Shot")]
        [Range(0.3f, 2f)] public float shotPitch = 1f;
        [Range(0f, 2f)] public float shotVolume = 1f;
        [Tooltip("An extra kit cue under your own shot (none = only the gun's report).")]
        public bool ownShotLayer;
        public SfxCue ownShotCue = SfxCue.Hit;
        [Range(0f, 1f)] public float ownShotCueVolume = 0.3f;
        [Header("Tracer and flash (every shot shown on this client)")]
        public Color tracerColor = new(1f, 0.85f, 0.45f, 0.9f);
        [Min(0.005f)] public float tracerWidth = 0.03f;
        [Min(0.02f)] public float tracerSeconds = 0.08f;
        public Color flashTint = new(1f, 0.82f, 0.45f);
        [Min(0.05f)] public float flashScale = 0.6f;
        [Tooltip("A puff of smoke at the muzzle too (a heavy gun).")]
        public bool flashSmoke;
        [Header("First person (your own gun)")]
        [Tooltip("Where the gun sits, in camera space (metres: right, up, forward).")]
        public Vector3 firstPersonOffset = new(0.3f, -0.28f, 0.55f);
        [Min(0.01f)] public float firstPersonScale = 0.7f;
    }

    /// <summary>
    /// Gives every Flag Wars gun its own look and sound. All four are the shared pistol tool re-tuned
    /// (see <see cref="CTF_GunSetup"/>), so on this client each gun tool in play, yours and everyone
    /// else's, is dressed for the gun its holder wears: the pistol's parts hide behind the gun's model,
    /// the muzzle moves to the model's barrel, and the shot's pitch and volume, tracer and flash follow
    /// the gun's row in <see cref="looks"/>. Yours is the gun the server last sent
    /// (<see cref="CTF_LoadoutBroadcast.liveWeapon"/>); everyone else's comes with the roster
    /// (<see cref="CTF_RosterBroadcast"/>, by body object id). Your own also names the gun on the
    /// hotbar and the ammo panel, which read the tool's name and image.
    /// </summary>
    public sealed class CTF_GunLooks : MonoBehaviour {
        [Tooltip("One row per gun, in CTF_Weapon order (baked by the HUD authoring).")]
        public CTF_GunLook[] looks = new CTF_GunLook[0];
        [Tooltip("A shot's muzzle within this many metres of a gun's muzzle is that gun's shot.")]
        [Min(0.01f)] public float muzzleMatch = 0.25f;
        [Tooltip("Gold Tracers (level 27): the colour every shot of that player draws, on every client.")]
        public Color goldTracerColor = new(1f, 0.78f, 0.18f, 1f);
        [Tooltip("Gold Tracers: width multiplier over the gun's own tracer.")]
        [Min(1f)] public float goldTracerWidth = 1.6f;

        sealed class Worn {
            public ToolGunShared gun;
            public bool applied;
            public CTF_Weapon weapon;
            public GameObject model;
            public readonly List<Renderer> pistolParts = new();
            public Transform fire;
            public Vector3 fireInRoot, fireScale;
            public AudioSource audio;
            public float pitch, volume;
            public string toolName;
            public Sprite toolImage;
        }

        readonly List<Worn> worn = new();
        readonly Dictionary<int, CTF_Weapon> weaponByBody = new();
        /// <summary>Bodies whose player unlocked Gold Tracers (level 27): their shots draw gold for everyone.</summary>
        readonly HashSet<int> goldBodies = new();
        readonly Stack<LineRenderer> tracerPool = new();
        readonly List<(LineRenderer line, float start, float end, Color color)> tracers = new();
        ClientManager manager;
        CTF_LoadoutBroadcast loadout;
        bool hasLoadout;
        Material tracerMaterial;

        /// <summary>The look of <paramref name="weapon"/> (null when the table has no row for it).</summary>
        public CTF_GunLook Look(CTF_Weapon weapon) {
            if (looks == null) return null;
            foreach (var look in looks) if (look != null && look.weapon == weapon) return look;
            return null;
        }

        /// <summary>Your gun's look this life (the sidearm's until the server says).</summary>
        public CTF_GunLook OwnLook => Look(hasLoadout ? loadout.liveWeapon : CTF_Weapon.Sidearm);

        void OnEnable() {
            ToolBaseShared.createStaticEvent += OnToolCreated;
            ToolBaseShared.destroyStaticEvent += OnToolDestroyed;
            WeaponFireEffects.ShotShown += OnShotShown;
        }

        void OnDisable() {
            ToolBaseShared.createStaticEvent -= OnToolCreated;
            ToolBaseShared.destroyStaticEvent -= OnToolDestroyed;
            WeaponFireEffects.ShotShown -= OnShotShown;
        }

        void OnDestroy() {
            if (manager != null) {
                manager.UnregisterBroadcast<CTF_LoadoutBroadcast>(OnLoadout);
                manager.UnregisterBroadcast<CTF_RosterBroadcast>(OnRoster);
            }
            foreach (var t in tracers) if (t.line != null) Destroy(t.line.gameObject);
            foreach (var line in tracerPool) if (line != null) Destroy(line.gameObject);
            if (tracerMaterial != null) Destroy(tracerMaterial);
        }

        void OnToolCreated(ToolBaseShared tool) {
            if (tool is not ToolGunShared gun) return;
            foreach (var w in worn) if (w.gun == gun) return;
            var entry = new Worn { gun = gun };
            // The pistol's own parts, before anything is added under the weapon root.
            if (gun.weaponRoot != null)
                foreach (var part in gun.weaponRoot.GetComponentsInChildren<Renderer>(true))
                    if (part is not ParticleSystemRenderer) entry.pistolParts.Add(part);
            if (gun.FireParticleSystem != null && gun.weaponRoot != null) {
                entry.fire = gun.FireParticleSystem.transform;
                entry.fireInRoot = gun.weaponRoot.transform.InverseTransformPoint(entry.fire.position);
                entry.fireScale = entry.fire.localScale;
            }
            entry.audio = gun.weaponRoot != null ? gun.weaponRoot.GetComponent<AudioSource>() : null;
            if (entry.audio != null) {
                entry.pitch = entry.audio.pitch;
                entry.volume = entry.audio.volume;
            }
            entry.toolName = gun.toolName;
            entry.toolImage = gun.toolImage;
            worn.Add(entry);
            Refresh(entry);
        }

        void OnToolDestroyed(ToolBaseShared tool) {
            for (int i = worn.Count - 1; i >= 0; i--) {
                if (worn[i].gun != tool) continue;
                if (worn[i].model != null) Destroy(worn[i].model);
                worn.RemoveAt(i);
            }
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_LoadoutBroadcast>(OnLoadout);
                manager.RegisterBroadcast<CTF_RosterBroadcast>(OnRoster);
            }
            // A tool's character link arrives a moment after the tool itself.
            for (int i = worn.Count - 1; i >= 0; i--) {
                if (worn[i].gun == null) {
                    worn.RemoveAt(i);
                    continue;
                }
                if (!worn[i].applied) Refresh(worn[i]);
            }
            FadeTracers();
        }

        void OnLoadout(CTF_LoadoutBroadcast message, Channel channel) {
            loadout = message;
            hasLoadout = true;
            foreach (var w in worn) if (w.gun != null && w.gun.IsOwner) Refresh(w);
        }

        void OnRoster(CTF_RosterBroadcast message, Channel channel) {
            weaponByBody.Clear();
            goldBodies.Clear();
            Note(message.bots);
            Note(message.players);
            foreach (var w in worn) if (w.gun != null && !w.gun.IsOwner) Refresh(w);
        }

        void Note(CTF_BotView[] views) {
            if (views == null) return;
            foreach (var view in views) {
                if (view.objectId < 0) continue;
                weaponByBody[view.objectId] = view.weapon;
                if (view.goldTracers) goldBodies.Add(view.objectId);
            }
        }

        bool WeaponOf(Worn w, out CTF_Weapon weapon) {
            weapon = CTF_Weapon.Sidearm;
            if (w.gun.IsOwner) {
                weapon = hasLoadout ? loadout.liveWeapon : CTF_Weapon.Sidearm;
                return hasLoadout;
            }
            var holder = w.gun.connectedCharacter;
            return holder != null && weaponByBody.TryGetValue(holder.ObjectId, out weapon);
        }

        void Refresh(Worn w) {
            if (w.gun == null || !WeaponOf(w, out var weapon)) return;
            if (w.applied && w.weapon == weapon) return;
            w.applied = true;
            w.weapon = weapon;
            Dress(w, Look(weapon));
        }

        void Dress(Worn w, CTF_GunLook look) {
            var gun = w.gun;
            if (w.model != null) Destroy(w.model);
            w.model = null;
            bool swap = look != null && look.model != null && gun.weaponRoot != null;
            foreach (var part in w.pistolParts) if (part != null) part.enabled = !swap;
            if (swap) {
                w.model = Instantiate(look.model, gun.weaponRoot.transform, false);
                w.model.name = look.model.name;
                SetLayer(w.model.transform, gun.weaponRoot.layer);
                // The model must never stop a shot: the hitscan's muzzle check ignores the tool, not extra colliders.
                foreach (var c in w.model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            }
            if (w.fire != null) {
                w.fire.position = gun.weaponRoot.transform.TransformPoint(swap ? look.muzzle : w.fireInRoot);
                float flash = look != null ? look.flashScale / 0.6f : 1f;
                w.fire.localScale = w.fireScale * Mathf.Clamp(flash, 0.3f, 3f);
            }
            if (w.audio != null) {
                w.audio.pitch = w.pitch * (look != null ? look.shotPitch : 1f);
                w.audio.volume = w.volume * (look != null ? look.shotVolume : 1f);
            }
            if (!gun.IsOwner) return;
            // Your hotbar slot and ammo panel read the tool's name and picture.
            gun.toolName = look != null ? CTF_Loadouts.Get(look.weapon).title : w.toolName;
            if (look != null && look.icon != null) gun.toolImage = look.icon;
            ToolbarNames.Refresh(gun);
        }

        static void SetLayer(Transform t, int layer) {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // ---------------------------------------------------------------- shots
        void OnShotShown(Vector3 muzzle, Vector3 impact, Vector3 normal, bool hasImpact) {
            var look = ShotLook(muzzle, out bool own, out bool gold);
            if (look == null) return;
            Vector3 aim = impact - muzzle;
            Camera cam = Camera.main;
            bool nearCamera = cam != null && (muzzle - cam.transform.position).sqrMagnitude < 9f;
            if (!nearCamera) {
                GameFx.Play(FxKind.Sparks, muzzle, aim.sqrMagnitude > 0f ? aim.normalized : Vector3.up, look.flashTint, look.flashScale);
                if (look.flashSmoke) GameFx.Play(FxKind.Smoke, muzzle, look.flashScale * 0.5f);
            }
            if (own && look.ownShotLayer) GameSfx.Play(look.ownShotCue, look.ownShotCueVolume);
            Tracer(muzzle, impact, look, gold);
        }

        /// <summary>Which gun fired a shot that left from <paramref name="muzzle"/>: the gun tool whose muzzle is there.</summary>
        CTF_GunLook ShotLook(Vector3 muzzle, out bool own, out bool gold) {
            own = gold = false;
            float best = muzzleMatch * muzzleMatch;
            Worn match = null;
            foreach (var w in worn) {
                if (w.gun == null || w.fire == null || !w.applied) continue;
                float d = (w.fire.position - muzzle).sqrMagnitude;
                if (d > best) continue;
                best = d;
                match = w;
            }
            if (match == null) return null;
            own = match.gun.IsOwner;
            var holder = match.gun.connectedCharacter;
            gold = holder != null && goldBodies.Contains(holder.ObjectId);
            return Look(match.weapon);
        }

        void Tracer(Vector3 from, Vector3 to, CTF_GunLook look, bool gold) {
            if (tracerMaterial == null) {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                tracerMaterial = new Material(shader) { name = "CTF tracer (shared)", hideFlags = HideFlags.DontSave };
            }
            LineRenderer line;
            if (tracerPool.Count > 0) line = tracerPool.Pop();
            else {
                var go = new GameObject("CTF_Tracer") { layer = 2 }; // Ignore Raycast
                line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = tracerMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
            }
            line.gameObject.SetActive(true);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            // Gold Tracers keep the gun's shape (width, length of life) and swap its colour.
            Color color = gold ? goldTracerColor : look.tracerColor;
            float width = gold ? look.tracerWidth * goldTracerWidth : look.tracerWidth;
            line.startWidth = width;
            line.endWidth = width * 0.5f;
            line.startColor = line.endColor = color;
            tracers.Add((line, Time.time, Time.time + look.tracerSeconds * (gold ? 1.3f : 1f), color));
        }

        void FadeTracers() {
            float now = Time.time;
            for (int i = tracers.Count - 1; i >= 0; i--) {
                var (line, start, end, color) = tracers[i];
                if (line == null) {
                    tracers.RemoveAt(i);
                    continue;
                }
                if (now >= end) {
                    line.gameObject.SetActive(false);
                    tracerPool.Push(line);
                    tracers.RemoveAt(i);
                    continue;
                }
                float k = 1f - Mathf.InverseLerp(start, end, now);
                var c = new Color(color.r, color.g, color.b, color.a * k);
                line.startColor = c;
                line.endColor = new Color(c.r, c.g, c.b, c.a * 0.4f);
            }
        }

        /// <summary>
        /// The shared hotbar writes the weapon's name on the ammo panel only when a tool is equipped and
        /// draws a slot's picture only when the slot is made. A gun changed while held (before a match the
        /// pick is worn at once) is redrawn here through the hotbar's own fields.
        /// </summary>
        static class ToolbarNames {
            static Type toolbarType;
            static FieldInfo titleField, slotsField;
            static bool looked;

            public static void Refresh(ToolGunShared gun) {
                if (!looked) {
                    looked = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                        toolbarType = asm.GetType("RyanAssets.Client.ClientUI.Toolbar.ToolbarUI");
                        if (toolbarType != null) break;
                    }
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    titleField = toolbarType?.GetField("weaponTitleText", flags);
                    slotsField = toolbarType?.GetField("toolBaseToItem", flags);
                }
                if (toolbarType == null) return;
                var bar = FindAnyObjectByType(toolbarType);
                if (bar == null) return;
                if (gun.equipped && titleField?.GetValue(bar) is TMPro.TMP_Text title) title.text = gun.toolName;
                if (slotsField?.GetValue(bar) is Dictionary<ToolBaseShared, Transform> slots
                    && slots.TryGetValue(gun, out var slot) && slot != null && slot.childCount > 0
                    && slot.GetChild(0).TryGetComponent(out UnityEngine.UI.Image image) && gun.toolImage != null)
                    image.sprite = gun.toolImage;
            }
        }
    }
}
