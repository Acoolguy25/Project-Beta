using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Characters.Client {
    public class StaminaController : MonoBehaviour {
        public static StaminaController Instance { get; private set; }
        public static float Stamina {  get; private set; }
        public static event Action<float> StaminaChanged;
        public static bool StaminaLoaded => PlayerData.localData != null;
        public static bool StaminaEnabled => MaxStamina != 0;
        public static float MaxStamina => PlayerData.localData.staminaMax.Value;
        public static float StaminaRegen => PlayerData.localData.staminaRegen.Value;
        public static float StaminaCooldown => PlayerData.localData.staminaCooldown.Value;
        static float lastTimeStaminaRegen;
        public static void SetStamina(float stamina) {
            if (LocalPlayer.Character == null || LocalPlayer.Character.IsDead())
                return;
            stamina = Mathf.Clamp(stamina, 0, MaxStamina);
            if (Stamina > stamina) lastTimeStaminaRegen = Time.time; // Trigger cooldown if stamina is consumed
            Stamina = stamina;
            StaminaChanged?.Invoke(Stamina);
        }
        public static void DeltaStamina(float delta) {
            SetStamina(Stamina + delta);
        }
        public static bool ConsumeStamina(float amount) {
            if (!StaminaEnabled)
                return true; // Always enabled, no stamina
            if (Stamina < amount)
                return false;
            SetStamina(Stamina - amount);
            return true;
        }
        void Update() {
            if (PlayerData.localData && Time.time >= lastTimeStaminaRegen + StaminaCooldown) {
                //lastTimeStaminaRegen = Time.time;
                DeltaStamina(Time.deltaTime * StaminaRegen);
            }
        }
        void Awake() {
            Instance = this;
        }
        void OnEnable() {
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved += OnMyPlayerRemoved;
        }
        void OnDisable() {
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved -= OnMyPlayerRemoved;
        }
        void OnMyPlayerAdded(PlayerData playerData) {
            SetStamina(MaxStamina);
            playerData.staminaMax.OnChange += OnMaxStaminaChanged;
            //playerData.staminaRegen.OnChange += OnUpdateDetected;
            //playerData.staminaCooldown.OnChange += OnUpdateDetected;
        }
        void OnMyPlayerRemoved(PlayerData playerData) {
            playerData.staminaMax.OnChange -= OnMaxStaminaChanged;
            //playerData.staminaRegen.OnChange -= OnUpdateDetected;
            //playerData.staminaCooldown.OnChange -= OnUpdateDetected;
        }
        void OnMaxStaminaChanged(float oldValue, float newValue, bool asServer) {
            if (newValue > oldValue)
                DeltaStamina(newValue - oldValue); // increase stamina if max increased
            else
                DeltaStamina(0); // signal an update, clip it to max if max decreased
        }
        void OnCharacterAdded(LocalCharacter character) {
            if (!StaminaLoaded)
                return;
            SetStamina(MaxStamina);
        }
    }
}