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

namespace RyanAssets.Client.ClientUI.Spectate {
    public class ClientSpectate : ICamera {
        [SerializeField]
        TMP_Text playerName, characterHealth, playerLevel;
        [SerializeField]
        Button leftArrow, rightArrow;
        [SerializeField]
        CanvasGroupController canvasGroupController;
        [SerializeField, Range(0f, 1f), Tooltip("How far the watched character's team colour is blended towards white for the name.")]
        float teamColorSoftening = 0.45f;

        CinemachineCamera cinemachineCamera;
        CameraController controller;

        PlayerData currentPlayer;
        GameCharacter currentCharacter;
        // Where the current target sat in the rotation. A character that leaves the registry
        // vacates its slot to whoever followed it, so resuming at the same index hands the
        // camera to the next investigator rather than restarting at the first one.
        int currentIndex;
        bool isSpectating => gameObject.activeSelf;

        static ClientSpectate instance;
        static bool barHidden;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            instance = null;
            barHidden = false;
        }

        /// <summary>
        /// Lets a game hide the spectate bar while its own UI needs the bottom of the screen (a results card,
        /// for example). The camera keeps following; only the bar is hidden. Cleared when the spectate camera is destroyed;
        /// a game that sets it also clears it when its own HUD goes away.
        /// </summary>
        public static void SetBarHidden(bool hidden) {
            if (barHidden == hidden)
                return;
            barHidden = hidden;
            if (instance != null && instance.isSpectating)
                instance.canvasGroupController.SetVisible(!hidden && instance.currentCharacter != null, 0.3f);
        }

        void Awake() {
            instance = this;
            cinemachineCamera = GetComponent<CinemachineCamera>();
            controller = transform.parent.GetComponent<CameraController>();
            leftArrow.onClick.AddListener(() => AdvancePosition(-1));
            rightArrow.onClick.AddListener(() => AdvancePosition(1));
            // Departures arrive through the spectated character's own MyGameCharacterRemoved,
            // which is all this camera reacts to; the rotation itself is re-read each time.
            GameCharacter.GameCharacterAdded += OnGameCharacterAdded;
        }
        public override void EnableCamera(Transform oldCamera, GameCameraType oldCameraType) {
            base.EnableCamera(oldCamera, oldCameraType);
            UnsetCamera();
            AdvancePosition(0);
        }
        public override void DisableCamera(Transform newCamera, GameCameraType newCameraType) {
            base.DisableCamera(newCamera, newCameraType);
            canvasGroupController.SetVisible(false, 0.3f);
            UnsetCamera();
        }
        static bool CanSpectate(GameCharacter character) =>
            character && !character.IsDead && character.CanSpectate.Value;
        /// <summary>
        /// The current rotation, read straight from the shared team registry. The registry
        /// drops a character when it dies, despawns or is destroyed and takes it back when it
        /// revives or respawns, so no separate list is kept in step with it here.
        /// </summary>
        static List<GameCharacter> Spectatable() =>
            GameCharacter.TeamToCharacter.Values.SelectMany(characters => characters)
                .Where(CanSpectate).ToList();
        void AdvancePosition(int deltaPosition) {
            List<GameCharacter> characters = Spectatable();
            if (characters.Count == 0) {
                // This is expected while waiting for the next round to spawn.
                currentIndex = 0;
                SetCamera(null);
                return;
            }
            int index = characters.IndexOf(currentCharacter);
            // A target still in the rotation steps from where it is. One that has left is
            // replaced by whoever now holds its slot.
            index = index >= 0 ? index + deltaPosition : currentIndex;
            currentIndex = MathHelper.Mod(index, characters.Count);
            SetCamera(characters[currentIndex]);
        }
        void UnsetCamera() {
            // Unity reports a destroyed character as null. Comparing against a real null keeps
            // these detaches running so this camera cannot stay subscribed to a character it
            // no longer shows and be pulled off a live target by that character's last events.
            if (currentPlayer is not null) {
                currentPlayer.username.OnChange -= OnPlayerNameChanged;
                currentPlayer.xp.OnChange -= OnPlayerXPChanged;
            }
            if (currentCharacter is not null) {
                currentCharacter.DisplayNameSync.OnChange -= OnPlayerNameChanged;
                currentCharacter.TeamSync.OnChange -= OnPlayerTeamChanged;
                currentCharacter.Health.OnChange -= OnPlayerHealthChanged;
                currentCharacter.MaxHealth.OnChange -= OnPlayerHealthChanged;
                currentCharacter.MyGameCharacterRemoved -= OnMyGameCharacterRemoved;
                currentCharacter.OnDied -= OnSpectatedCharacterDied;
                currentCharacter.CanSpectate.OnChange -= OnSpectateEligibilityChanged;
            }

            currentCharacter = null;
            currentPlayer = null;
        }
        void SetCamera(GameCharacter character) {
            UnsetCamera();
            currentCharacter = character;
            currentPlayer = character ? PlayerData.GetPlayerData(character.Owner) : null;

            if (character != null) {
                if (currentPlayer) {
                    currentPlayer.username.OnChange += OnPlayerNameChanged;
                    currentPlayer.xp.OnChange += OnPlayerXPChanged;
                }
                currentCharacter.DisplayNameSync.OnChange += OnPlayerNameChanged;
                currentCharacter.TeamSync.OnChange += OnPlayerTeamChanged;
                currentCharacter.Health.OnChange += OnPlayerHealthChanged;
                currentCharacter.MaxHealth.OnChange += OnPlayerHealthChanged;
                currentCharacter.MyGameCharacterRemoved += OnMyGameCharacterRemoved;
                currentCharacter.OnDied += OnSpectatedCharacterDied;
                currentCharacter.CanSpectate.OnChange += OnSpectateEligibilityChanged;

            }
            controller.SetCameraTarget(character);
            canvasGroupController.SetVisible(character != null && !barHidden, 0.3f);
            UpdatePlayerLabel();
            UpdatePlayerHealth();
            UpdatePlayerLevel();

        }
        void OnGameCharacterAdded(GameCharacter character) {
            // A respawned investigator is picked up immediately when this camera is showing
            // nobody, and stays reachable through the arrows when it is already following one.
            if (isSpectating && currentCharacter == null)
                AdvancePosition(0);
        }
        void OnDestroy() {
            if (instance == this) {
                instance = null;
                barHidden = false;
            }
            GameCharacter.GameCharacterAdded -= OnGameCharacterAdded;
            UnsetCamera();
        }
        /// <summary>Moves on only once the spectated character has actually left the rotation.</summary>
        void ReselectIfLost() {
            if (CanSpectate(currentCharacter))
                return;
            // One death raises death, health, eligibility and removal notifications. Re-resolving
            // instead of stepping forward on each keeps them from skipping past living players.
            AdvancePosition(0);
        }
        // Dumb wrappers
        void OnPlayerNameChanged(string oldVal, string newVal, bool asServer) {
            UpdatePlayerLabel();
        }
        void OnPlayerTeamChanged(TeamConfig oldTeamConfig, TeamConfig newTeamConfig, bool asServer) {
            UpdatePlayerLabel();
        }
        void OnPlayerHealthChanged(long oldVal, long newVal, bool asServer) {
            if (currentCharacter != null && currentCharacter.IsDead) ReselectIfLost();
            else UpdatePlayerHealth();
        }
        void OnPlayerXPChanged(ulong oldVal, ulong newVal, bool asServer) {
            UpdatePlayerLevel();
        }
        void OnMyGameCharacterRemoved(GameCharacter character) {
            ReselectIfLost();
        }
        void OnSpectatedCharacterDied(DamageType damage, IEntity source) => ReselectIfLost();
        void OnSpectateEligibilityChanged(bool before, bool after, bool asServer) {
            if (!after) ReselectIfLost();
        }

        void UpdatePlayerLabel() {
            if (currentCharacter == null)
                return;
            playerName.text = (currentPlayer && currentPlayer.username.Value != currentCharacter.DisplayName) ?
                    $"{currentCharacter.DisplayName} (@{currentPlayer.username.Value})"
                    : currentCharacter.DisplayName;
            // Team colours are tuned for name tags in the world; softened towards white they stay
            // readable on the bar's dark glass.
            playerName.color = Color.Lerp(currentCharacter.GetTeam().realTeamColor, Color.white, teamColorSoftening);
        }
        /// <summary>Health only shows while the watched character is hurt: a full bar says nothing.</summary>
        void UpdatePlayerHealth() {
            if (currentCharacter == null)
                return;
            long health = currentCharacter.Health.Value, max = currentCharacter.MaxHealth.Value;
            bool hurt = max > 0 && health < max;
            characterHealth.gameObject.SetActive(hurt);
            if (hurt)
                characterHealth.text = $"{health}/{max}";
        }
        /// <summary>Players show their level; NPCs show nothing extra.</summary>
        void UpdatePlayerLevel() {
            if (currentCharacter == null)
                return;
            bool player = currentPlayer;
            playerLevel.gameObject.SetActive(player);
            if (player)
                playerLevel.text = $"· Level {LevelsCalc.GetRank(currentPlayer.xp.Value)}";
        }
    }
}
