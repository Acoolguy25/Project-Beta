using RyanAssets.Characters.Shared;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using RyanAssets.DataService;
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

        List<(Canvas, GameCharacter)> nameTags = new();
        void Start() {
            GameCharacter.GameCharacterAdded += GameCharacterAdded;
            GameCharacter.GameCharacterRemoved += GameCharacterRemoved;
        }
        Transform GetHead(Transform root) {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (t.name == "Head")
                    return root;
            }
            Debug.LogError($"No head found in {root.name}");
            return null;
        }
        void GameCharacterAdded(GameCharacter gameCharater) {
            if (!gameCharater.ShowNameTag) return;
            Transform head = GetHead(gameCharater.transform);
            GameObject nameTag = Instantiate(nameTagPrefab, head, true);
            Canvas nameTagCanvas = nameTag.GetComponent<Canvas>();

            Transform Backing = nameTag.transform.Find("Backing");
            Transform HealthBarBacking = Backing.Find("HealthBarBacking");
            TextMeshProUGUI displayNameText = Backing.GetChild(0).GetComponent<TextMeshProUGUI>();
            RectTransform healthBar = HealthBarBacking.GetChild(0).GetComponent<RectTransform>();
            TextMeshProUGUI healthLabelText = HealthBarBacking.GetChild(1).GetComponent<TextMeshProUGUI>();
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
            for (int i = nameTags.Count - 1; i >= 0; i--) {
                (Canvas, GameCharacter) nameTagTuple = nameTags.ElementAt(i);
                Canvas nameTag = nameTagTuple.Item1;
                GameCharacter gameCharacter = nameTagTuple.Item2;
                
                if (nameTag != null) {
                    // Camera switches briefly leave no active MainCamera. Nametags
                    // can wait a frame rather than throwing during that transition.
                    Camera mainCamera = Camera.main;
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
        }
        void OnDestroy() {
            GameCharacter.GameCharacterAdded -= GameCharacterAdded;
            GameCharacter.GameCharacterRemoved -= GameCharacterRemoved;
        }
    }
}
