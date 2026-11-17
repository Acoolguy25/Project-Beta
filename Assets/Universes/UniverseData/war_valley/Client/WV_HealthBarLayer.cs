using System;
using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.NameTag;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.InputSystem;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Small health bars over everything that fights in War Valley, in place of the shared name tags.
    /// <para>
    /// A battle puts dozens of soldiers on screen, and a full name tag over each - "Knifeman" in
    /// giant red letters - buried the fight under labels. Here every troop, wave enemy, vehicle,
    /// building and the flag instead gets one bar in its side's colour (green for the commander's
    /// own, the ally's colour for an ally's, red for the waves). A wave enemy always wears its red
    /// bar, which is what marks it as an enemy whatever colour its body. A defender's soldier or
    /// vehicle shows its bar once it is hurt, while it is selected, or while the pointer is over it,
    /// and its name only on hover or over a small selection; the commander's own are named by kind
    /// alone ("Gunner"), an ally's with the owner ("Sam's Gunner"). A building's bar is larger and
    /// shows while it is hurt or still being built; the flag's bar always shows. Both are named only
    /// on hover or while damaged, so the labels stay off a quiet base.
    /// Player characters keep their shared tag, since knowing who is who among the commanders still
    /// matters.
    /// </para>
    /// <para>
    /// The bars are pooled copies of a template authored in the HUD prefab, drawn on the HUD canvas
    /// under its panels and placed from the entities' world positions every frame.
    /// </para>
    /// </summary>
    public sealed class WV_HealthBarLayer : MonoBehaviour {
        /// <summary>Health at or above this share counts as untouched.</summary>
        const float FullHealth = 0.999f;

        enum Kind { Soldier, Vehicle, Structure, Flag }

        [Header("Authored References")]
        [Tooltip("Inactive bar inside this layer that every bar is copied from.")]
        [SerializeField] WV_HealthBar template;

        [Header("Visibility")]
        [Tooltip("Bars further than this from the camera are hidden (m).")]
        [SerializeField, Min(1f)] float maxDistance = 160f;
        [Tooltip("Bars start shrinking and fading beyond this distance (m).")]
        [SerializeField, Min(0f)] float fadeStartDistance = 70f;
        [Tooltip("Smallest scale a far bar shrinks to.")]
        [SerializeField, Range(0.2f, 1f)] float farScale = 0.6f;
        [Tooltip("Lowest opacity a far bar fades to before it is hidden.")]
        [SerializeField, Range(0f, 1f)] float farAlpha = 0.45f;
        [Tooltip("Show a bar on every soldier even at full health. Off keeps an untouched army clean.")]
        [SerializeField] bool showAtFullHealth;
        [Tooltip("Wave enemies always show their red bar, even untouched: wave soldiers come in random " +
                 "body colours, so the bar is what tells them from the defenders at a glance.")]
        [SerializeField] bool markWaveEnemies = true;
        [Tooltip("Selected soldiers are named only while the selection is at most this big; a whole " +
                 "army's names would cover the field again.")]
        [SerializeField, Min(0)] int namedSelectionLimit = 3;
        [Tooltip("Untouched selected soldiers keep a bar only while the selection is at most this big; " +
                 "past it only the hurt ones do, so a big army's fight stays readable.")]
        [SerializeField, Min(0)] int fullHealthSelectionLimit = 6;
        [Tooltip("A name is left off when it would land closer than this to a name already drawn this " +
                 "frame (canvas pixels, horizontal and vertical), so a squad standing together shows " +
                 "one label instead of a stack.")]
        [SerializeField] Vector2 nameSpacing = new(120f, 18f);
        [Tooltip("Gap between the top of a body and its bar (m).")]
        [SerializeField, Min(0f)] float headroom = 0.6f;
        [Tooltip("Height of the bar above an entity that has no collider to measure (m).")]
        [SerializeField, Min(0f)] float fallbackHeight = 2f;
        [Tooltip("Furthest a hover ray looks for a soldier under the pointer (m).")]
        [SerializeField, Min(1f)] float hoverRayDistance = 600f;

        [Header("Sizes")]
        [Tooltip("A vehicle's bar, as a multiple of a soldier's.")]
        [SerializeField, Min(0.5f)] float vehicleBarScale = 1.4f;
        [Tooltip("A building's bar, as a multiple of a soldier's.")]
        [SerializeField, Min(0.5f)] float structureBarScale = 1.8f;
        [Tooltip("The flag's bar, as a multiple of a soldier's. Always shown.")]
        [SerializeField, Min(0.5f)] float flagBarScale = 2f;
        [Tooltip("Buildings and the flag are seen from further away than soldiers: their bars stay up to " +
                 "this many times the soldiers' distance.")]
        [SerializeField, Min(1f)] float structureDistanceScale = 1.8f;

        [Header("Shared Tags")]
        [Tooltip("While War Valley is on screen, the shared tags left on (commanders) start fading at " +
                 "this distance (m) instead of the prefab's.")]
        [SerializeField, Min(0f)] float sharedTagFadeStart = 140f;
        [Tooltip("...and are gone beyond this distance (m).")]
        [SerializeField, Min(0f)] float sharedTagFadeEnd = 230f;
        [Tooltip("Largest share of the screen height a shared tag may cover (0 keeps the prefab's).")]
        [SerializeField, Range(0f, 1f)] float sharedTagMaxScreenHeight = 0.045f;

        [Header("Colours")]
        [SerializeField] Color ownColor = new(0.36f, 0.9f, 0.45f, 1f);
        [SerializeField] Color enemyColor = new(0.95f, 0.3f, 0.26f, 1f);
        [Tooltip("Blended into an ally's commander colour so it reads as a bar rather than a tag.")]
        [SerializeField, Range(0f, 1f)] float allyColorSoftening = 0.25f;
        [SerializeField] Color nameColor = new(1f, 1f, 1f, 0.95f);
        [Tooltip("Bosses always show their bar and name, in this colour.")]
        [SerializeField] Color bossColor = new(1f, 0.6f, 0.15f, 1f);
        [Tooltip("The flag's bar and name.")]
        [SerializeField] Color flagColor = new(1f, 0.84f, 0.3f, 1f);

        sealed class Entry {
            public EntityBase Entity;
            public Kind Kind;
            public Collider Body;
            public WV_Constructable Site;
            public WV_HealthBar Bar;
            public bool IsBoss;
            /// <summary>"Gunner", "Tank": the name without its owner, for the commander's own.</summary>
            public string KindName;
            /// <summary>The display name <see cref="KindName"/> and <see cref="IsBoss"/> were read from.</summary>
            public string NameSource;
        }

        readonly List<Entry> entries = new();
        readonly Dictionary<EntityBase, Entry> byEntity = new();
        readonly Stack<WV_HealthBar> pool = new();
        readonly List<Vector2> namesPlaced = new();

        Func<EntityBase, bool> isSelected;
        Func<EntityBase, bool> isOwn;
        Func<int> selectionCount;
        Func<Vector2, bool> isOverHud;
        Canvas canvas;
        EntityBase hovered;
        bool tagFilterInstalled;
        NameTagManager.ViewOverride tagView;

        /// <summary>
        /// Connects the layer to the commander's selection and HUD, which the client controller owns.
        /// Until bound, nothing counts as selected or as the commander's own.
        /// </summary>
        public void Bind(Func<EntityBase, bool> selected, Func<EntityBase, bool> own, Func<int> count,
            Func<Vector2, bool> pointerOverHud) {
            isSelected = selected;
            isOwn = own;
            selectionCount = count;
            isOverHud = pointerOverHud;
        }

        void Awake() {
            canvas = GetComponentInParent<Canvas>();
            if (template == null)
                Debug.LogError($"{nameof(WV_HealthBarLayer)} has no bar template; re-run Ryan/War Valley/Rebuild HUD.", this);
            else
                template.SetVisible(false);
        }

        void OnEnable() {
            // Characters publish their own roster; vehicles, buildings and the flag go through the
            // entity registry.
            GameCharacter.GameCharacterAdded += HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved += HandleRemoved;
            EntityBase.EntityAdded += HandleEntityAdded;
            EntityBase.EntityRemoved += HandleRemoved;
            foreach (List<GameCharacter> team in GameCharacter.TeamToCharacter.Values) {
                foreach (GameCharacter character in team)
                    HandleCharacterAdded(character);
            }
            foreach (EntityBase entity in EntityBase.All)
                HandleEntityAdded(entity);

            // Everything this layer draws a bar for loses its shared tag. A filter some other game
            // left installed is replaced only while War Valley is on screen.
            NameTagManager.CharacterTagFilter = ShowSharedTag;
            NameTagManager.EntityTagFilter = ShowSharedEntityTag;
            tagFilterInstalled = true;
            tagView = NameTagManager.OverrideView(sharedTagFadeStart, Mathf.Max(sharedTagFadeStart, sharedTagFadeEnd),
                sharedTagMaxScreenHeight);
        }

        void OnDisable() {
            GameCharacter.GameCharacterAdded -= HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved -= HandleRemoved;
            EntityBase.EntityAdded -= HandleEntityAdded;
            EntityBase.EntityRemoved -= HandleRemoved;
            if (tagFilterInstalled && NameTagManager.CharacterTagFilter == (Func<GameCharacter, bool>)ShowSharedTag)
                NameTagManager.CharacterTagFilter = null;
            if (tagFilterInstalled && NameTagManager.EntityTagFilter == (Func<EntityBase, bool>)ShowSharedEntityTag)
                NameTagManager.EntityTagFilter = null;
            tagFilterInstalled = false;
            tagView?.Dispose();
            tagView = null;
            for (int i = entries.Count - 1; i >= 0; i--)
                Release(entries[i]);
            entries.Clear();
            byEntity.Clear();
        }

        /// <summary>Only commanders keep the shared overhead tag; soldiers get a bar from this layer.</summary>
        static bool ShowSharedTag(GameCharacter character) => character is LocalCharacter;

        /// <summary>Vehicles, buildings and the flag all draw their bar here.</summary>
        static bool ShowSharedEntityTag(EntityBase entity) => GetKind(entity) == null;

        static Kind? GetKind(EntityBase entity) => entity switch {
            LocalCharacter => null,
            GameCharacter => Kind.Soldier,
            WV_Unit => Kind.Vehicle,
            WV_Flag => Kind.Flag,
            StructureComponent => Kind.Structure,
            _ => null
        };

        void HandleCharacterAdded(GameCharacter character) => HandleEntityAdded(character);

        void HandleEntityAdded(EntityBase entity) {
            if (entity == null || byEntity.ContainsKey(entity) || GetKind(entity) is not Kind kind)
                return;
            var entry = new Entry {
                Entity = entity,
                Kind = kind,
                Body = entity.GetComponent<Collider>(),
                Site = entity.GetComponent<WV_Constructable>()
            };
            entries.Add(entry);
            byEntity.Add(entity, entry);
        }

        void HandleRemoved(EntityBase entity) {
            if (entity != null && byEntity.TryGetValue(entity, out Entry entry))
                Forget(entry);
        }

        /// <summary>
        /// Everything read from the entity's name: whether it is a boss, and its bare kind name. The
        /// server names a spawned troop after it reaches the client, so this is re-read whenever the
        /// name is no longer the one last read: a string comparison per frame, not a parse.
        /// </summary>
        static void RefreshNames(Entry entry) {
            EntityBase entity = entry.Entity;
            string name = entity.DisplayName;
            if (name == entry.NameSource)
                return;
            entry.NameSource = name;
            entry.IsBoss = entry.Kind == Kind.Soldier && name == WV_WaveBoss.DefaultName;
            entry.KindName = entry.Kind switch {
                Kind.Soldier when WV_Rules.TryGetTroopKind(name, out WV_TroopKind troop) => WV_Rules.GetTroopDisplayName(troop),
                Kind.Vehicle when entity is WV_Unit unit => WV_Rules.GetUnitDisplayName(unit.Kind),
                _ => name
            };
        }

        void Forget(Entry entry) {
            Release(entry);
            entries.Remove(entry);
            byEntity.Remove(entry.Entity);
        }

        void Release(Entry entry) {
            if (entry.Bar == null)
                return;
            entry.Bar.SetVisible(false);
            pool.Push(entry.Bar);
            entry.Bar = null;
        }

        WV_HealthBar Acquire() {
            while (pool.Count > 0) {
                WV_HealthBar pooled = pool.Pop();
                if (pooled != null)
                    return pooled;
            }
            WV_HealthBar bar = Instantiate(template, transform, false);
            bar.name = "HealthBar";
            return bar;
        }

        void LateUpdate() {
            Camera camera = Camera.main;
            if (camera == null || template == null) {
                for (int i = 0; i < entries.Count; i++)
                    Release(entries[i]);
                return;
            }

            hovered = FindHovered(camera);

            float scaleFactor = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            Vector3 cameraPosition = camera.transform.position;
            int selected = selectionCount != null ? selectionCount() : 0;
            namesPlaced.Clear();
            for (int i = entries.Count - 1; i >= 0; i--) {
                Entry entry = entries[i];
                if (entry.Entity == null) {
                    Forget(entry);
                    continue;
                }
                if (!TryPlace(entry, camera, cameraPosition, scaleFactor, selected))
                    Release(entry);
            }
        }

        bool TryPlace(Entry entry, Camera camera, Vector3 cameraPosition, float scaleFactor, int selected) {
            EntityBase entity = entry.Entity;
            if (entity.IsDead || !entity.isActiveAndEnabled)
                return false;

            RefreshNames(entry);
            long max = entity.MaxHealth.Value;
            float health = max > 0 ? Mathf.Clamp01(entity.Health.Value / (float)max) : 1f;
            bool damaged = health < FullHealth;
            bool isHovered = hovered == entity;
            bool isSelectedEntity = isSelected != null && isSelected(entity);
            bool building = entry.Site != null && !entry.Site.IsOperational;

            bool show;
            bool named;
            float size;
            switch (entry.Kind) {
                case Kind.Flag:
                    show = true;
                    named = isHovered || damaged;
                    size = flagBarScale;
                    break;
                case Kind.Structure:
                    show = isHovered || damaged || building || showAtFullHealth;
                    named = isHovered || (damaged && !building);
                    size = structureBarScale;
                    break;
                default:
                    bool selectedShown = isSelectedEntity && (damaged || selected <= fullHealthSelectionLimit);
                    bool marked = markWaveEnemies && IsWave(entity);
                    show = entry.IsBoss || marked || isHovered || selectedShown || damaged || showAtFullHealth;
                    named = entry.IsBoss || isHovered || (isSelectedEntity && selected <= namedSelectionLimit);
                    size = entry.Kind == Kind.Vehicle ? vehicleBarScale : 1f;
                    break;
            }
            if (!show)
                return false;

            bool large = entry.Kind is Kind.Structure or Kind.Flag;
            float reach = large ? maxDistance * structureDistanceScale : maxDistance;
            float fadeStart = large ? fadeStartDistance * structureDistanceScale : fadeStartDistance;
            Vector3 position = entity.transform.position;
            float distance = Vector3.Distance(cameraPosition, position);
            if (distance > reach && !isHovered)
                return false;

            float top = entry.Body != null ? entry.Body.bounds.max.y : position.y + fallbackHeight;
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(position.x, top + headroom, position.z));
            if (screen.z <= 0f)
                return false;

            if (entry.Bar == null)
                entry.Bar = Acquire();
            WV_HealthBar bar = entry.Bar;
            bar.SetVisible(true);

            float far = reach > fadeStart && !isHovered ? Mathf.InverseLerp(fadeStart, reach, distance) : 0f;
            Vector2 canvasPosition = new Vector2(screen.x, screen.y) / scaleFactor;
            bar.Place(canvasPosition, size * Mathf.Lerp(1f, farScale, far), Mathf.Lerp(1f, farAlpha, far));
            // The hovered entity and a boss keep their name even in a crowd.
            if (named && !ClaimNameSpace(canvasPosition) && !isHovered && !entry.IsBoss)
                named = false;
            Color color = entry.Kind == Kind.Flag ? flagColor : entry.IsBoss ? bossColor : GetSideColor(entity);
            bar.SetHealth(health, color);
            bar.SetName(named ? GetLabel(entry) : null,
                entry.Kind == Kind.Flag ? flagColor : entry.IsBoss ? bossColor : nameColor);
            return true;
        }

        /// <summary>True, and the spot is taken, when no name drawn this frame is within <see cref="nameSpacing"/>.</summary>
        bool ClaimNameSpace(Vector2 position) {
            foreach (Vector2 placed in namesPlaced) {
                if (Mathf.Abs(placed.x - position.x) < nameSpacing.x && Mathf.Abs(placed.y - position.y) < nameSpacing.y)
                    return false;
            }
            namesPlaced.Add(position);
            return true;
        }

        /// <summary>The commander's own are named by kind alone; everyone else's keep their full name.</summary>
        string GetLabel(Entry entry) =>
            entry.Kind is Kind.Soldier or Kind.Vehicle && isOwn != null && isOwn(entry.Entity)
                ? entry.KindName
                : entry.Entity.DisplayName;

        static bool IsWave(EntityBase entity) =>
            entity.Team != null && entity.Team.realTeam == WV_Alliances.GetWaveSide();

        /// <summary>Green for the commander's own, red for the waves, and an ally's own commander colour.</summary>
        Color GetSideColor(EntityBase entity) {
            TeamConfig team = entity.Team;
            if (team == null)
                return ownColor;
            if (IsWave(entity))
                return enemyColor;
            if (isOwn != null && isOwn(entity))
                return ownColor;
            return Color.Lerp(TeamConfig.TeamToColor(team.displayTeam), Color.white, allyColorSoftening);
        }

        /// <summary>Whatever this layer draws a bar for under the pointer, unless the pointer is on the HUD.</summary>
        EntityBase FindHovered(Camera camera) {
            Mouse mouse = Mouse.current;
            if (mouse == null || !Application.isFocused)
                return null;
            Vector2 pointer = mouse.position.ReadValue();
            if (isOverHud != null && isOverHud(pointer))
                return null;
            Ray ray = camera.ScreenPointToRay(pointer);
            if (!Physics.Raycast(ray, out RaycastHit hit, hoverRayDistance, WV_Combat.TargetMask,
                    QueryTriggerInteraction.Ignore))
                return null;
            EntityBase entity = hit.collider.GetComponentInParent<EntityBase>();
            return entity != null && byEntity.ContainsKey(entity) ? entity : null;
        }
    }
}
