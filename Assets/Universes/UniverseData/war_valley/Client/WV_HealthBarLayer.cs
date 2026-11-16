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
    /// Small health bars over War Valley's soldiers, in place of the shared name tags.
    /// <para>
    /// A battle puts dozens of soldiers on screen, and a full name tag over each - "Knifeman" in
    /// giant red letters - buried the fight under labels. Here every troop and wave enemy instead
    /// gets a thin bar in its side's colour (green for the commander's own, the ally's colour for an
    /// ally's, red for the waves) that shows only once it is hurt, while it is selected, or while the
    /// pointer is over it; the name appears only on hover, or over a small selection. Player
    /// characters keep their shared tag, since knowing who is who among the commanders still matters.
    /// </para>
    /// <para>
    /// Vehicles get the same bars (their shared tag is filtered out through
    /// <see cref="NameTagManager.EntityTagFilter"/>). Buildings keep the shared tag, which shows once
    /// they are damaged; the layer stretches the shared tag distances to suit the commander's camera,
    /// which looks at the field from far higher than the 60-95 m the tags fade at by default.
    /// </para>
    /// <para>
    /// The bars are pooled copies of a template authored in the HUD prefab, drawn on the HUD canvas
    /// under its panels and placed from the soldiers' world positions every frame.
    /// </para>
    /// </summary>
    public sealed class WV_HealthBarLayer : MonoBehaviour {
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
        [Tooltip("Selected soldiers are named only while the selection is at most this big; a whole " +
                 "army's names would cover the field again.")]
        [SerializeField, Min(0)] int namedSelectionLimit = 3;
        [Tooltip("Gap between the top of a soldier's body and its bar (m).")]
        [SerializeField, Min(0f)] float headroom = 0.6f;
        [Tooltip("Furthest a hover ray looks for a soldier under the pointer (m).")]
        [SerializeField, Min(1f)] float hoverRayDistance = 600f;

        [Header("Shared Tags")]
        [Tooltip("While War Valley is on screen, the shared tags left on (commanders, damaged buildings) " +
                 "start fading at this distance (m) instead of the prefab's.")]
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

        sealed class Entry {
            public EntityBase Entity;
            public GameCharacter Character;
            public Collider Body;
            public WV_HealthBar Bar;
            public bool IsBoss;
        }

        readonly List<Entry> entries = new();
        readonly Dictionary<EntityBase, Entry> byEntity = new();
        readonly Stack<WV_HealthBar> pool = new();

        Func<EntityBase, bool> isSelected;
        Func<EntityBase, bool> isOwn;
        Func<int> selectionCount;
        Canvas canvas;
        EntityBase hovered;
        bool tagFilterInstalled;
        NameTagManager.ViewOverride tagView;

        /// <summary>
        /// Connects the layer to the commander's selection, which the client controller owns.
        /// Until bound, nothing counts as selected or as the commander's own.
        /// </summary>
        public void Bind(Func<EntityBase, bool> selected, Func<EntityBase, bool> own, Func<int> count) {
            isSelected = selected;
            isOwn = own;
            selectionCount = count;
        }

        void Awake() {
            canvas = GetComponentInParent<Canvas>();
            if (template == null)
                Debug.LogError($"{nameof(WV_HealthBarLayer)} has no bar template; re-run Ryan/War Valley/Rebuild HUD.", this);
            else
                template.SetVisible(false);
        }

        void OnEnable() {
            GameCharacter.GameCharacterAdded += HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved += HandleCharacterRemoved;
            foreach (List<GameCharacter> team in GameCharacter.TeamToCharacter.Values) {
                foreach (GameCharacter character in team)
                    HandleCharacterAdded(character);
            }

            // Troops and wave enemies draw their bar here instead of a shared tag. A filter some
            // other game left installed is replaced only while War Valley is on screen.
            NameTagManager.CharacterTagFilter = ShowSharedTag;
            NameTagManager.EntityTagFilter = ShowSharedEntityTag;
            tagFilterInstalled = true;
            tagView = NameTagManager.OverrideView(sharedTagFadeStart, Mathf.Max(sharedTagFadeStart, sharedTagFadeEnd),
                sharedTagMaxScreenHeight);
        }

        void OnDisable() {
            GameCharacter.GameCharacterAdded -= HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved -= HandleCharacterRemoved;
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

        /// <summary>Vehicles draw their bar here; buildings and the flag keep the shared tag.</summary>
        static bool ShowSharedEntityTag(EntityBase entity) => entity is not WV_Unit;

        void HandleCharacterAdded(GameCharacter character) {
            if (character == null || character is LocalCharacter || byEntity.ContainsKey(character))
                return;
            Track(character, character);
        }

        void HandleCharacterRemoved(GameCharacter character) {
            if (character != null && byEntity.TryGetValue(character, out Entry entry))
                Forget(entry);
        }

        Entry Track(EntityBase entity, GameCharacter character) {
            var entry = new Entry {
                Entity = entity,
                Character = character,
                Body = entity.GetComponent<Collider>()
            };
            entries.Add(entry);
            byEntity.Add(entity, entry);
            return entry;
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
            TrackUnits();

            float scaleFactor = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            Vector3 cameraPosition = camera.transform.position;
            int selected = selectionCount != null ? selectionCount() : 0;
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

        /// <summary>
        /// Vehicles are listed by <see cref="WV_Unit.All"/> rather than by an event, so new ones are
        /// picked up here; departed ones drop out when their entity is destroyed.
        /// </summary>
        void TrackUnits() {
            IReadOnlyList<WV_Unit> units = WV_Unit.All;
            for (int i = 0; i < units.Count; i++) {
                WV_Unit unit = units[i];
                if (unit != null && !byEntity.ContainsKey(unit))
                    Track(unit, null);
            }
        }

        bool TryPlace(Entry entry, Camera camera, Vector3 cameraPosition, float scaleFactor, int selected) {
            EntityBase entity = entry.Entity;
            if (entity.IsDead || !entity.isActiveAndEnabled)
                return false;
            if (entry.Character != null && !entry.IsBoss)
                entry.IsBoss = entry.Character.DisplayName == WV_WaveBoss.DefaultName;

            long max = entity.MaxHealth.Value;
            float health = max > 0 ? Mathf.Clamp01(entity.Health.Value / (float)max) : 1f;
            bool damaged = health < 0.999f;
            bool isHovered = hovered == entity;
            bool isSelectedEntity = isSelected != null && isSelected(entity);

            bool show = entry.IsBoss || isHovered || isSelectedEntity || damaged || showAtFullHealth;
            bool named = entry.IsBoss || isHovered || (isSelectedEntity && selected <= namedSelectionLimit);
            if (!show)
                return false;

            Vector3 position = entity.transform.position;
            float distance = Vector3.Distance(cameraPosition, position);
            if (distance > maxDistance && !isHovered)
                return false;

            float top = entry.Body != null ? entry.Body.bounds.max.y : position.y + 2f;
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(position.x, top + headroom, position.z));
            if (screen.z <= 0f)
                return false;

            if (entry.Bar == null)
                entry.Bar = Acquire();
            WV_HealthBar bar = entry.Bar;
            bar.SetVisible(true);

            float far = maxDistance > fadeStartDistance
                ? Mathf.InverseLerp(fadeStartDistance, maxDistance, distance)
                : 0f;
            if (isHovered)
                far = 0f;
            bar.Place(new Vector2(screen.x, screen.y) / scaleFactor,
                Mathf.Lerp(1f, farScale, far), Mathf.Lerp(1f, farAlpha, far));
            bar.SetHealth(health, entry.IsBoss ? bossColor : GetSideColor(entity));
            bar.SetName(named ? entity.DisplayName : null, entry.IsBoss ? bossColor : nameColor);
            return true;
        }

        /// <summary>Green for the commander's own, red for the waves, and an ally's own commander colour.</summary>
        Color GetSideColor(EntityBase entity) {
            TeamConfig team = entity.Team;
            if (team == null)
                return ownColor;
            if (team.realTeam == WV_Alliances.GetWaveSide())
                return enemyColor;
            if (isOwn != null && isOwn(entity))
                return ownColor;
            return Color.Lerp(TeamConfig.TeamToColor(team.displayTeam), Color.white, allyColorSoftening);
        }

        /// <summary>The soldier or vehicle under the pointer, if any.</summary>
        EntityBase FindHovered(Camera camera) {
            Mouse mouse = Mouse.current;
            if (mouse == null || !Application.isFocused)
                return null;
            Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, hoverRayDistance, WV_Combat.TargetMask,
                    QueryTriggerInteraction.Ignore))
                return null;
            EntityBase entity = hit.collider.GetComponentInParent<EntityBase>();
            return entity is GameCharacter or WV_Unit ? entity : null;
        }
    }
}
