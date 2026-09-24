using RyanAssets.Characters.Shared;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using RyanAssets.DataService;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Cameras;

namespace RyanAssets.Client.ClientUI.NameTag {
    public class NameTagManager : MonoBehaviour {
        [SerializeField]
        GameObject nameTagPrefab;
        [SerializeField]
        Vector2 nameTagSize;
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

        List<(Canvas, GameCharacter)> nameTags = new();
        // World entities - structures, objectives, vehicles - carry a tag that only appears once
        // they have actually taken damage, so an untouched base stays clean while a building being
        // chewed on reads as such from across the map.
        List<(Canvas, EntityBase)> entityNameTags = new();

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
        Transform GetHead(Transform root) {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (t.name == "Head")
                    return root;
            }
            Debug.LogError($"No head found in {root.name}");
            return null;
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

        void GameCharacterAdded(GameCharacter gameCharater) {
            if (!gameCharater.ShowNameTag) return;
            Transform head = GetHead(gameCharater.transform);
            GameObject nameTag = Instantiate(nameTagPrefab, head, true);
            Canvas nameTagCanvas = nameTag.GetComponent<Canvas>();

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
                UnsubscribeNameTag();
                gameCharater.OnRevive += OnCharacterRevived;
                Destroy(nameTag);
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

            UpdateNameTagPositioning(nameTagCanvas);

            nameTags.Add((nameTagCanvas, gameCharater));
        }
        void GameCharacterRemoved(GameCharacter gameCharater) {
            int index = nameTags.FindIndex(t => t.Item2 == gameCharater);
            if (index != -1) {
                Destroy(nameTags[index].Item1.gameObject);
                nameTags.RemoveAt(index);
            }
        }

        // --- World entities ---------------------------------------------------

        /// <summary>
        /// Gives a structure, objective, or vehicle the same overhead tag characters carry. The tag
        /// stays hidden until the entity is actually hurt: a player needs to see which of their
        /// buildings is being taken apart, not a label over every wall they own.
        /// </summary>
        void EntityAdded(EntityBase entity) {
            // Characters run through GameCharacterAdded, which also handles death, revival, and the
            // spectated-character case. Never give one two tags.
            if (entity == null || entity is GameCharacter || entity.HealthComponent == null)
                return;
            if (entityNameTags.FindIndex(t => t.Item2 == entity) != -1)
                return;

            GameObject nameTag = Instantiate(nameTagPrefab, entity.transform, true);
            Canvas nameTagCanvas = nameTag.GetComponent<Canvas>();

            GetNameTagParts(
                nameTag,
                out Transform healthBarBacking,
                out TextMeshProUGUI displayNameText,
                out RectTransform healthBar,
                out TextMeshProUGUI healthLabelText);

            healthBarBacking.gameObject.SetActive(true);
            displayNameText.text = entity.DisplayName;
            displayNameText.color = entity.Team != null ? entity.Team.displayTeamColor : Color.white;

            void OnEntityHealthChanged(long _1, long _2, bool asServer) {
                long max = entity.MaxHealth.Value;
                float healthPercent = max <= 0 ? 1f : Mathf.Clamp01(entity.Health.Value / (float)max);
                healthBar.anchorMax = new Vector2(healthPercent, healthBar.anchorMax.y);
                healthLabelText.text = $"{entity.Health.Value}/{max}";
            }

            entity.Health.OnChange += OnEntityHealthChanged;
            entity.MaxHealth.OnChange += OnEntityHealthChanged;
            OnEntityHealthChanged(default, default, false);

            UpdateEntityNameTagPositioning(nameTagCanvas, entity);
            nameTagCanvas.gameObject.SetActive(false);

            entityNameTags.Add((nameTagCanvas, entity));
        }

        void EntityRemoved(EntityBase entity) {
            int index = entityNameTags.FindIndex(t => t.Item2 == entity);
            if (index == -1)
                return;

            Canvas canvas = entityNameTags[index].Item1;
            entityNameTags.RemoveAt(index);
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        /// <summary>A damaged, living entity shows its tag; everything else hides it.</summary>
        static bool ShouldShowEntityTag(EntityBase entity) {
            long max = entity.MaxHealth.Value;
            return max > 0 && entity.Health.Value < max && !entity.IsDead;
        }

        /// <summary>
        /// Sits the tag just above the entity's own bounds rather than at a fixed character-sized
        /// offset, because a refinery and a fence post are nothing like the same height.
        /// </summary>
        void UpdateEntityNameTagPositioning(Canvas nameTag, EntityBase entity) {
            Transform root = entity.transform;
            Vector3 scale = root.lossyScale;
            // A degenerate axis would divide the tag's size out to infinity.
            scale = new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 1f : scale.x,
                Mathf.Approximately(scale.y, 0f) ? 1f : scale.y,
                Mathf.Approximately(scale.z, 0f) ? 1f : scale.z);

            RectTransform nameTagRect = nameTag.GetComponent<RectTransform>();
            nameTagRect.sizeDelta = new Vector2(
                entityNameTagSize.x / scale.x,
                entityNameTagSize.y / scale.y
            );

            float worldTop = GetWorldTop(root);
            float localHeight = (worldTop + entityNameTagPadding - root.position.y) / scale.y;
            nameTagRect.localPosition = new Vector3(0f, localHeight, 0f);
        }

        /// <summary>
        /// Highest point of an entity's visible body. Renderers are preferred over colliders because a
        /// structure's collider is often a single box that stops short of its roof.
        /// </summary>
        static float GetWorldTop(Transform root) {
            bool found = false;
            Bounds bounds = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) {
                // The tag itself is a child by the time this is recomputed, and a canvas renderer
                // would drag the measured top upward every frame it ran.
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

        void UpdateNameTagPositioning(Canvas nameTag) {
            Transform head = nameTag.transform.parent;

            Vector3 scale = head.lossyScale;

            RectTransform nameTagRect = nameTag.GetComponent<RectTransform>();
            nameTagRect.sizeDelta = new Vector2(
                nameTagSize.x / scale.x,
                nameTagSize.y / scale.y
            );

            nameTagRect.localPosition = new Vector3(
                nameTagOffset.x / scale.x,
                nameTagOffset.y / scale.y,
                nameTagOffset.z / scale.z
            );
        }
        void Update() {
            // Camera switches briefly leave no active MainCamera. Nametags
            // can wait a frame rather than throwing during that transition.
            Camera mainCamera = Camera.main;

            for (int i = nameTags.Count - 1; i >= 0; i--) {
                (Canvas, GameCharacter) nameTagTuple = nameTags.ElementAt(i);
                Canvas nameTag = nameTagTuple.Item1;
                GameCharacter gameCharacter = nameTagTuple.Item2;

                if (nameTag != null) {
                    if (mainCamera != null)
                        nameTag.transform.forward = mainCamera.transform.forward;
                    nameTag.gameObject.SetActive(CameraController.targetCharacter != gameCharacter && !gameCharacter.IsDead);
#if UNITY_EDITOR
                    //UpdateNameTagPositioning(nameTag);
#endif
                }
                else{
                    nameTags.RemoveAt(i);
                }
            }

            for (int i = entityNameTags.Count - 1; i >= 0; i--) {
                (Canvas nameTag, EntityBase entity) = entityNameTags[i];
                if (nameTag == null || entity == null) {
                    if (nameTag != null)
                        Destroy(nameTag.gameObject);
                    entityNameTags.RemoveAt(i);
                    continue;
                }

                bool show = ShouldShowEntityTag(entity);
                if (show != nameTag.gameObject.activeSelf) {
                    nameTag.gameObject.SetActive(show);
                    // A structure finishes construction, and a vehicle is built, at its full size only
                    // after it spawns, so the tag is re-seated the moment it is first needed.
                    if (show)
                        UpdateEntityNameTagPositioning(nameTag, entity);
                }
                if (show && mainCamera != null)
                    nameTag.transform.forward = mainCamera.transform.forward;
            }
        }
        void OnDestroy() {
            GameCharacter.GameCharacterAdded -= GameCharacterAdded;
            GameCharacter.GameCharacterRemoved -= GameCharacterRemoved;
            EntityBase.EntityAdded -= EntityAdded;
            EntityBase.EntityRemoved -= EntityRemoved;
        }
    }
}
