using RyanAssets.Cameras;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Levels.Shared;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;
using RyanAssets.UI;

namespace RyanAssets.Client.ClientUI.Spectate
{
    public class ClientSpectate : ICamera
    {
        [SerializeField]
        TMP_Text playerName, characterHealth, playerLevel;
        [SerializeField]
        Button leftArrow, rightArrow;
        [SerializeField]
        CanvasGroupController canvasGroupController;

        CinemachineCamera cinemachineCamera;
        CameraController controller;

        PlayerData currentPlayer;
        GameCharacter currentCharacter;
        bool isSpectating => gameObject.activeSelf;
        void Awake() {
            cinemachineCamera = GetComponent<CinemachineCamera>();
            controller = transform.parent.GetComponent<CameraController>();
            leftArrow.onClick.AddListener(() => AdvancePosition(-1));
            rightArrow.onClick.AddListener(() => AdvancePosition(1));
        }
        public override void EnableCamera(Transform oldCamera, GameCameraType oldCameraType) {
            base.EnableCamera(oldCamera, oldCameraType);
            GameCharacter.GameCharacterAdded += OnGameCharacterAdded;
            canvasGroupController.SetVisible(true, 0.3f);
            currentCharacter = null;
            AdvancePosition(0);
        }
        public override void DisableCamera(Transform newCamera, GameCameraType newCameraType) {
            base.DisableCamera(newCamera, newCameraType);
            GameCharacter.GameCharacterAdded -= OnGameCharacterAdded;
            canvasGroupController.SetVisible(false, 0.3f);
            UnsetCamera();
        }
        void AdvancePosition(int deltaPosition) {
            List<GameCharacter> characters = GameCharacter.TeamToCharacter.Values
                .SelectMany(x => x)
                // A despawning Unity object can compare equal to null. Require a live
                // object before allowing the current selection through the fallback.
                .Where(c => c && ((!c.IsDead && c.CanSpectate.Value) || currentCharacter == c))
                .ToList();
            if (characters.Count == 0 || (characters.Count == 1 && characters[0] == currentCharacter)) {
                // This is expected while waiting for the next round to spawn.
                currentCharacter = null;
                //Debug.LogWarning($"No valid characters to spectate. Waiting for next round.");
                SetCamera(null);
                return;
            }
            int currentIndex = characters.FindIndex(c => c == currentCharacter);
            GameCharacter nextCharacter = null;
            if (currentIndex == -1) {
                nextCharacter = characters[0];
            } else {
                int newIndex = MathHelper.Mod(currentIndex + deltaPosition, characters.Count);
                nextCharacter = characters[newIndex];
            }
            if (nextCharacter == null) {
                Debug.LogError("No valid character found for spectating.");
                SetCamera(null);
                return;
            }
            SetCamera(nextCharacter);
        }
        void UnsetCamera() {
            if (currentPlayer) {
                currentPlayer.username.OnChange -= OnPlayerNameChanged;
                currentPlayer.xp.OnChange -= OnPlayerXPChanged;
            }
            if (currentCharacter) {
                currentCharacter.DisplayNameSync.OnChange -= OnPlayerNameChanged;
                currentCharacter.TeamSync.OnChange -= OnPlayerTeamChanged;
                currentCharacter.Health.OnChange -= OnPlayerHealthChanged;
                currentCharacter.MaxHealth.OnChange -= OnPlayerHealthChanged;
                currentCharacter.OnDied -= OnPlayerDied;
                currentCharacter.MyGameCharacterRemoved -= OnMyGameCharacterRemoved;
            }

            currentCharacter = null;
            currentPlayer = null;
        }
        void SetCamera(GameCharacter character) {
            UnsetCamera();
            currentCharacter = character;
            currentPlayer = character? PlayerData.GetPlayerData(character.Owner) : null;

            if (character != null) {
                if (currentPlayer) {
                    currentPlayer.username.OnChange += OnPlayerNameChanged;
                    currentPlayer.xp.OnChange += OnPlayerXPChanged;
                }
                currentCharacter.DisplayNameSync.OnChange += OnPlayerNameChanged;
                currentCharacter.TeamSync.OnChange += OnPlayerTeamChanged;
                currentCharacter.Health.OnChange += OnPlayerHealthChanged;
                currentCharacter.MaxHealth.OnChange += OnPlayerHealthChanged;
                currentCharacter.OnDied += OnPlayerDied;
                currentCharacter.MyGameCharacterRemoved += OnMyGameCharacterRemoved;

                controller.SetCameraTarget(character);
            }

            UpdatePlayerLabel();
            UpdatePlayerHealth();
            UpdatePlayerLevel();

        }
        void OnGameCharacterAdded(GameCharacter character) {
            if (isSpectating && currentCharacter == null)
                AdvancePosition(0);
        }
        void OnDestroy() {
            UnsetCamera();
        }
        // Dumb wrappers
        void OnPlayerNameChanged(string oldVal, string newVal, bool asServer) {
            UpdatePlayerLabel();
        }
        void OnPlayerTeamChanged(TeamConfig oldTeamConfig, TeamConfig newTeamConfig, bool asServer) {
            UpdatePlayerLabel();
        }
        void OnPlayerHealthChanged(long oldVal, long newVal, bool asServer) {
            UpdatePlayerHealth();
        }
        void OnPlayerXPChanged(ulong oldVal, ulong newVal, bool asServer) {
            UpdatePlayerLevel();
        }
        void OnPlayerDied(DamageType source, IEntity sourceEntity) {
            AdvancePosition(1);
        }
        void OnMyGameCharacterRemoved(GameCharacter character) {
            AdvancePosition(1);
        }

        void UpdatePlayerLabel() {
            if (currentCharacter == null) {
                playerName.text = "No Characters Found";
                playerName.color = Color.white;
            } else {
                playerName.text = (currentPlayer && currentPlayer.username.Value != currentCharacter.DisplayName) ?
                        $"{currentCharacter.DisplayName} (@{currentPlayer.username.Value})"
                        : currentCharacter.DisplayName;
                playerName.color = currentCharacter.GetTeam().realTeamColor;
            }
        }
        void UpdatePlayerHealth() {
            if (currentCharacter == null)
                characterHealth.text = $"";
            else {
                characterHealth.text = $"{currentCharacter.Health.Value}/{currentCharacter.MaxHealth.Value}";
                characterHealth.color = Color.Lerp(Color.darkRed, Color.darkGreen, (currentCharacter.MaxHealth.Value == 0) ? 1f :
                        currentCharacter.Health.Value / currentCharacter.MaxHealth.Value);
            }
        }
        void UpdatePlayerLevel() {
            if (currentCharacter == null)
                playerLevel.text = $"";
            else if (currentPlayer)
                playerLevel.text = $"Level {(currentPlayer? LevelsCalc.GetRank(currentPlayer.xp.Value): 67)}";
            else
                playerLevel.text = $"NPC";
        }
    }
}
