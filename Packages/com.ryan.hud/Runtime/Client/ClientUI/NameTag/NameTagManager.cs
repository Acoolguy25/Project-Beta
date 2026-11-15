using RyanAssets.Characters.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Cameras;

namespace RyanAssets.Client.ClientUI.NameTag {
    /// <summary>
    /// Draws the overhead name and health tag for every character, and for world entities -
    /// structures, objectives, vehicles - that need one.
    /// <para>
    /// Tags are kept under this manager rather than inside the thing they label, and are moved to
    /// it every frame. A tag parented into a character inherited the character's scale: a build that
    /// made a soldier short, tall, or wide stretched its tag the same way, and because the tag turns
    /// to face the camera, a non-uniformly scaled parent also sheared it - which no counter-scale on
    /// the tag can undo. Held outside, every tag is the one authored size; only its height follows
    /// the body, so it still clears a tall soldier's head and sits low over a short one.
    /// </para>
    /// </summary>
    public class NameTagManager : MonoBehaviour {
        [SerializeField]
        GameObject nameTagPrefab;
        [SerializeField]
        Vector2 nameTagSize;
        [Tooltip("Offset from a character's root to its tag, for a body at its authored size. The " +
                 "offset follows the character's build, so a taller body carries its tag higher.")]
        [SerializeField]
        Vector3 nameTagOffset;

        [Header("World Entities")]
        [Tooltip("Overhead size used for structures and other world entities, which are far larger " +
                 "than a character and would otherwise carry a tag too small to read.")]
        [SerializeField]
        Vector2 entityNameTagSize = new(6f, 2f);
        [Tooltip("World-space gap left between the top of an entity's bounds and its tag.")]
        [SerializeField]
        float entityNameTagPadding = 2f;

        /// <summary>One character's tag and the subscriptions that keep it current.</summary>
        sealed class CharacterTag {
            public Canvas Canvas;
            public GameCharacter Character;
            public Action Unbind;
        }

        /// <summary>One world entity's tag, and how far above the entity's root it floats.</summary>
        sealed class EntityTag {
            public Canvas Canvas;
            public EntityBase Entity;
            public GameObject HealthBar;
            public float Height;
            public Action Unbind;
        }

        readonly List<CharacterTag> nameTags = new();
        // World entities - structures, objectives - carry a tag that only appears once they have
        // actually taken damage, so an untouched base stays clean while a building being chewed on
        // reads as such from across the map. Entities that ask for it, such as vehicles, keep theirs
        // up the way a player does.
        readonly List<EntityTag> entityNameTags = new();

        void Start() {
            GameCharacter.GameCharacterAdded += GameCharacterAdded;
            GameCharacter.GameCharacterRemoved += GameCharacterRemoved;
            EntityBase.EntityAdded += EntityAdded;
            EntityBase.EntityRemoved += EntityRemoved;

            // The manager can be created after a scene's structures have already spawned, so adopt
            // whatever is on the field rather than waiting for the next spawn.
            foreach (EntityBase entity in EntityBase.All.ToArray())
                EntityAdded(entity);
        }

        /// <summary>
        /// Resolves the three labels the authored tag prefab contains. Both the character tag and the
        /// world-entity tag are the same prefab, so they bind through one lookup rather than each
        /// re-walking the hierarchy with its own assumptions about the child order.
        /// </summary>
        static void GetNameTagParts(
            GameObject nameTag,
            out Transform healthBarBacking,
            out TextMeshProUGUI displayNameText,
            out RectTransform healthBar,
            out TextMeshProUGUI healthLabelText) {
            Transform backing = nameTag.transform.Find("Backing");
            healthBarBacking = backing.Find("HealthBarBacking");
            displayNameText = backing.GetChild(0).GetComponent<TextMeshProUGUI>();
            healthBar = healthBarBacking.GetChild(0).GetComponent<RectTransform>();
            healthLabelText = healthBarBacking.GetChild(1).GetComponent<TextMeshProUGUI>();
        }

        /// <summary>
        /// Creates a tag under this manager at the authored size. The manager is expected to be
        /// unscaled; should it ever sit under a scaled parent, that scale is divided back out once
        /// here so every tag still comes out at <paramref name="size"/> world units.
        /// </summary>
        Canvas CreateTag(Vector2 size) {
            GameObject nameTag = Instantiate(nameTagPrefab, transform, false);
            Vector3 managerScale = transform.lossyScale;
            nameTag.transform.localScale = new Vector3(
                Mathf.Approximately(managerScale.x, 0f) ? 1f : 1f / managerScale.x,
                Mathf.Approximately(managerScale.y, 0f) ? 1f : 1f / managerScale.y,
                Mathf.Approximately(managerScale.z, 0f) ? 1f : 1f / managerScale.z);
            nameTag.GetComponent<RectTransform>().sizeDelta = size;
            return nameTag.GetComponent<Canvas>();
        }

        // --- Characters -------------------------------------------------------

        void GameCharacterAdded(GameCharacter gameCharater) {
            if (!gameCharater.ShowNameTag) return;
            Canvas nameTagCanvas = CreateTag(nameTagSize);
            GameObject nameTag = nameTagCanvas.gameObject;
            var tag = new CharacterTag { Canvas = nameTagCanvas, Character = gameCharater };

            GetNameTagParts(
                nameTag,
                out Transform HealthBarBacking,
                out TextMeshProUGUI displayNameText,
                out RectTransform healthBar,
                out TextMeshProUGUI healthLabelText);
            void OnPlayerNameChanged(string _, string newValue, bool asServer) {
                // Use the value supplied by FishNet. This also remains correct if a
                // callback is raised before another reader observes the SyncVar value.
                displayNameText.text = GameCharacter.NormalizeDisplayName(newValue);
            }
            void OnPlayerHealthChanged(long _1, long _2, bool asServer) {
                float healthPercent = (gameCharater.MaxHealth.Value == 0)? 1f: ((float)gameCharater.Health.Value / (float)gameCharater.MaxHealth.Value);
                healthBar.anchorMax = new Vector2(healthPercent, healthBar.anchorMax.y);
                healthLabelText.text = $"{gameCharater.Health.Value}/{gameCharater.MaxHealth.Value}";
                HealthBarBacking.gameObject.SetActive(gameCharater.Health.Value < gameCharater.MaxHealth.Value && !gameCharater.IsDead && !gameCharater.IsEffectActive(CharacterEffect.Invul));
            }
            void OnPlayerTeamChanged(TeamConfig _, TeamConfig newValue, bool asServer) {
                displayNameText.color = newValue.displayTeamColor;
            }
            void OnActiveEffectsChanged(FishNet.Object.Synchronizing.SyncDictionaryOperation op, CharacterEffect _2, float _3, bool _4) {
                if (op == FishNet.Object.Synchronizing.SyncDictionaryOperation.Complete)
                    OnPlayerHealthChanged(default, default, default);
            }
            void UnsubscribeNameTag() {
                gameCharater.DisplayNameSync.OnChange -= OnPlayerNameChanged;
                gameCharater.Health.OnChange -= OnPlayerHealthChanged;
                gameCharater.MaxHealth.OnChange -= OnPlayerHealthChanged;
                gameCharater.ActiveEffects.OnChange -= OnActiveEffectsChanged;
                gameCharater.TeamSync.OnChange -= OnPlayerTeamChanged;
                gameCharater.OnDied -= OnCharacterDied;
            }
            void OnCharacterRevived() {
                gameCharater.OnRevive -= OnCharacterRevived;
                GameCharacterAdded(gameCharater);
            }
            void OnCharacterDied(DamageType damageType, IEntity ownerObj) {
                // Removed first: removal unbinds the revival hook, which is only then armed.
                RemoveCharacterTag(tag);
                gameCharater.OnRevive += OnCharacterRevived;
            }
            gameCharater.DisplayNameSync.OnChange += OnPlayerNameChanged;

            gameCharater.Health.OnChange += OnPlayerHealthChanged;
            gameCharater.MaxHealth.OnChange += OnPlayerHealthChanged;
            gameCharater.ActiveEffects.OnChange += OnActiveEffectsChanged;
            gameCharater.TeamSync.OnChange += OnPlayerTeamChanged;
            gameCharater.OnDied += OnCharacterDied;

            OnPlayerNameChanged(default, gameCharater.DisplayName, false);
            OnPlayerHealthChanged(default, default, false);
            OnPlayerTeamChanged(default, gameCharater.Team, false);

            // A character leaving the game drops its tag without dying first; a revival waiting to
            // re-add one must not outlive it either.
            tag.Unbind = () => {
                UnsubscribeNameTag();
                gameCharater.OnRevive -= OnCharacterRevived;
            };

            PlaceCharacterTag(tag, Camera.main);
            nameTags.Add(tag);
        }

        void GameCharacterRemoved(GameCharacter gameCharater) {
            int index = nameTags.FindIndex(t => t.Character == gameCharater);
            if (index != -1)
                RemoveCharacterTag(nameTags[index]);
        }

        void RemoveCharacterTag(CharacterTag tag) {
            nameTags.Remove(tag);
            tag.Unbind?.Invoke();
            tag.Unbind = null;
            if (tag.Canvas != null)
                Destroy(tag.Canvas.gameObject);
        }

        /// <summary>
        /// Seats a character's tag over its head. The offset is authored for the body at its
        /// authored size and is scaled by the character's build, so the tag clears a tall soldier's
        /// head and does not float over a short one; the tag itself keeps its one size.
        /// </summary>
        void PlaceCharacterTag(CharacterTag tag, Camera mainCamera) {
            Transform body = tag.Character.transform;
            Vector3 offset = Vector3.Scale(nameTagOffset, tag.Character.BodyScale);
            Transform tagTransform = tag.Canvas.transform;
            tagTransform.position = body.position + body.rotation * offset;
            if (mainCamera != null)
                tagTransform.forward = mainCamera.transform.forward;
        }

        // --- World entities ---------------------------------------------------

        /// <summary>
        /// Gives a structure, objective, or vehicle the same overhead tag characters carry. By
        /// default the tag stays hidden until the entity is actually hurt: a player needs to see
        /// which of their buildings is being taken apart, not a label over every wall they own.
        /// Entities that ask to be labelled all the time get a player-style tag instead.
        /// </summary>
        void EntityAdded(EntityBase entity) {
            // Characters run through GameCharacterAdded, which also handles death, revival, and the
            // spectated-character case. Never give one two tags.
            if (entity == null || entity is GameCharacter || entity.HealthComponent == null)
                return;
            if (entityNameTags.FindIndex(t => t.Entity == entity) != -1)
                return;

            Canvas nameTagCanvas = CreateTag(entityNameTagSize);

            GetNameTagParts(
                nameTagCanvas.gameObject,
                out Transform healthBarBacking,
                out TextMeshProUGUI displayNameText,
                out RectTransform healthBar,
                out TextMeshProUGUI healthLabelText);

            var tag = new EntityTag {
                Canvas = nameTagCanvas,
                Entity = entity,
                HealthBar = healthBarBacking.gameObject
            };

            // A game mode can hand a placed structure or a built vehicle to its commander a frame
            // after it spawns, so the tag follows the team and the name rather than keeping
            // whatever the prefab was authored with.
            void OnEntityTeamChanged(EntityBase changed) {
                if (displayNameText != null)
                    displayNameText.color = changed.Team != null ? changed.Team.displayTeamColor : Color.white;
            }
            void OnEntityNameChanged(EntityBase changed) {
                if (displayNameText != null)
                    displayNameText.text = changed.DisplayName;
            }
            void OnEntityHealthChanged(long _1, long _2, bool asServer) {
                long max = entity.MaxHealth.Value;
                float healthPercent = max <= 0 ? 1f : Mathf.Clamp01(entity.Health.Value / (float)max);
                healthBar.anchorMax = new Vector2(healthPercent, healthBar.anchorMax.y);
                healthLabelText.text = $"{entity.Health.Value}/{max}";
            }

            entity.TeamChanged += OnEntityTeamChanged;
            entity.DisplayNameChanged += OnEntityNameChanged;
            entity.Health.OnChange += OnEntityHealthChanged;
            entity.MaxHealth.OnChange += OnEntityHealthChanged;
            tag.Unbind = () => {
                entity.TeamChanged -= OnEntityTeamChanged;
                entity.DisplayNameChanged -= OnEntityNameChanged;
                if (entity.HealthComponent != null) {
                    entity.Health.OnChange -= OnEntityHealthChanged;
                    entity.MaxHealth.OnChange -= OnEntityHealthChanged;
                }
            };

            OnEntityTeamChanged(entity);
            OnEntityNameChanged(entity);
            OnEntityHealthChanged(default, default, false);

            tag.Height = MeasureTagHeight(entity);
            nameTagCanvas.gameObject.SetActive(false);

            entityNameTags.Add(tag);
        }

        void EntityRemoved(EntityBase entity) {
            int index = entityNameTags.FindIndex(t => t.Entity == entity);
            if (index != -1)
                RemoveEntityTag(index);
        }

        void RemoveEntityTag(int index) {
            EntityTag tag = entityNameTags[index];
            entityNameTags.RemoveAt(index);
            if (tag.Entity != null)
                tag.Unbind?.Invoke();
            tag.Unbind = null;
            if (tag.Canvas != null)
                Destroy(tag.Canvas.gameObject);
        }

        static bool IsDamaged(EntityBase entity) {
            long max = entity.MaxHealth.Value;
            return max > 0 && entity.Health.Value < max;
        }

        /// <summary>
        /// A living entity shows its tag once damaged, or always when it asks to be labelled like a
        /// player. Dead ones never do.
        /// </summary>
        static bool ShouldShowEntityTag(EntityBase entity) =>
            !entity.IsDead && (entity.AlwaysShowNameTag || IsDamaged(entity));

        /// <summary>
        /// How far above the entity's root its tag floats: just clear of its own bounds rather than
        /// at a fixed character-sized offset, because a refinery and a fence post are nothing like
        /// the same height.
        /// </summary>
        float MeasureTagHeight(EntityBase entity) =>
            GetWorldTop(entity.transform) + entityNameTagPadding - entity.transform.position.y;

        /// <summary>
        /// Highest point of an entity's visible body. Renderers are preferred over colliders because a
        /// structure's collider is often a single box that stops short of its roof.
        /// </summary>
        static float GetWorldTop(Transform root) {
            bool found = false;
            Bounds bounds = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) {
                // Canvases a game mode hangs on the entity - a build timer - are not its body.
                if (renderer.GetComponentInParent<Canvas>() != null)
                    continue;
                if (!found) {
                    bounds = renderer.bounds;
                    found = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!found) {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) {
                    if (!found) {
                        bounds = collider.bounds;
                        found = true;
                    } else {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            return found ? bounds.max.y : root.position.y + 2f;
        }

        void LateUpdate() {
            // Camera switches briefly leave no active MainCamera. Nametags
            // can wait a frame rather than throwing during that transition.
            Camera mainCamera = Camera.main;

            for (int i = nameTags.Count - 1; i >= 0; i--) {
                CharacterTag tag = nameTags[i];
                if (tag.Canvas == null || tag.Character == null) {
                    // The character went without a removal event; its tag lives here, not under it.
                    RemoveCharacterTag(tag);
                    continue;
                }

                bool show = CameraController.targetCharacter != tag.Character && !tag.Character.IsDead;
                if (tag.Canvas.gameObject.activeSelf != show)
                    tag.Canvas.gameObject.SetActive(show);
                if (show)
                    PlaceCharacterTag(tag, mainCamera);
            }

            for (int i = entityNameTags.Count - 1; i >= 0; i--) {
                EntityTag tag = entityNameTags[i];
                if (tag.Canvas == null || tag.Entity == null) {
                    RemoveEntityTag(i);
                    continue;
                }

                EntityBase entity = tag.Entity;
                bool show = ShouldShowEntityTag(entity);
                if (show != tag.Canvas.gameObject.activeSelf) {
                    tag.Canvas.gameObject.SetActive(show);
                    // A structure finishes construction, and a vehicle is built, at its full size only
                    // after it spawns, so the tag is re-seated the moment it is first needed.
                    if (show)
                        tag.Height = MeasureTagHeight(entity);
                }
                if (!show)
                    continue;

                // A player-style tag shows health only once there is damage to show, as a
                // character's does; a damage-only tag is up precisely because there is.
                bool showHealth = !entity.AlwaysShowNameTag || IsDamaged(entity);
                if (tag.HealthBar.activeSelf != showHealth)
                    tag.HealthBar.SetActive(showHealth);

                Transform tagTransform = tag.Canvas.transform;
                tagTransform.position = entity.transform.position + Vector3.up * tag.Height;
                if (mainCamera != null)
                    tagTransform.forward = mainCamera.transform.forward;
            }
        }

        void OnDestroy() {
            GameCharacter.GameCharacterAdded -= GameCharacterAdded;
            GameCharacter.GameCharacterRemoved -= GameCharacterRemoved;
            EntityBase.EntityAdded -= EntityAdded;
            EntityBase.EntityRemoved -= EntityRemoved;

            foreach (CharacterTag tag in nameTags)
                tag.Unbind?.Invoke();
            nameTags.Clear();
            foreach (EntityTag tag in entityNameTags) {
                if (tag.Entity != null)
                    tag.Unbind?.Invoke();
            }
            entityNameTags.Clear();
        }
    }
}
