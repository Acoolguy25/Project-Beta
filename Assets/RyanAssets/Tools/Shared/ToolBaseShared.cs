using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using FishNet.Serializing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using System;
using UnityEngine;
using UnityEngine.UIElements;
using RyanAssets.DataService;
using RpcGen;
using System.Collections.Generic;
#if !UNITY_SERVER
using RyanAssets.Client.ClientAudio;
#endif


namespace RyanAssets.Tools.Shared {
    public class ToolBaseShared : NetworkBehaviour {
        [SerializeField]
        private string clientScript, clientObserver, serverScript;

        [Header("Tool Data")]
        [SerializeField]
        public DamageType defaultDamageType;
        [SerializeField]
        public ToolEnum toolEnum;
        [SerializeField]
        public string toolName, toolDesc;
        [SerializeField]
        public Sprite toolImage;

        [Header("Weapon Stats")]
        [SerializeField]
        public int staminaCostInit = 10;
        [SerializeField]
        public int hitDamageInit = 150;
        [SerializeField]
        public float attackCooldownInit = 0.85f;
        [SerializeField]
        public float reloadDurationInit = 1.0f;

        [Header("Ammo Stats")]
        [SerializeField]
        public int currentAmmo = -1; // -1 to disable ammo
        [SerializeField]
        public int maxClipAmmoInit = 10;
        [SerializeField]
        public int currentStoredAmmo = -1; // -1 for infinite ammo
        [SerializeField]
        public int maxStoredAmmo = 10;

        [Header("Animation")]
        [SerializeField]
        public string animationPackName;

        [Header("Audio")]
        [SerializeField]
        public AudioClip equipAudio;
        [SerializeField]
        public AudioClip unequipAudio, attackAudio;
        [SerializeField]
        public List<AudioClip> extraAudios;

        [Header("Internal")]
        [SerializeField]
        public string ParentObjectName = "RightHand";

        // Sync Vars
        public readonly SyncVar<int> staminaCostSync = new();
        public readonly SyncVar<int> hitDamageSync = new();
        public readonly SyncVar<float> attackCooldownSync = new();
        public readonly SyncVar<float> reloadDurationSync = new();
        public readonly SyncVar<int> maxClipAmmoSync = new();
        public readonly SyncVar<float> serverCooldownSync = new(0f);
        // Client Readonly Sync Vars
        public int staminaCost => staminaCostSync.Value;
        public int hitDamage => hitDamageSync.Value;
        public float attackCooldown => attackCooldownSync.Value;
        public float reloadDuration => reloadDurationSync.Value;
        public int maxClipAmmo {
            get => maxClipAmmoSync.Value;
            set => maxClipAmmoSync.Value = value;
        }

        /// <summary>
        /// Copies the Inspector tuning values to the authoritative runtime stats.
        /// This is intentionally server-only: the SyncVars then replicate the
        /// changed values to every client that has this tool spawned.
        /// </summary>
        public void ApplyInitialStatsLive() {
            if (!Application.isPlaying || !IsSpawned || !IsServerStarted)
                return;

            staminaCostSync.Value = staminaCostInit;
            hitDamageSync.Value = hitDamageInit;
            attackCooldownSync.Value = attackCooldownInit;
            reloadDurationSync.Value = reloadDurationInit;
            maxClipAmmoSync.Value = maxClipAmmoInit;
        }

        [SerializeField]
        public GameObject weaponRoot;

        [NonSerialized]
        public NetworkBehaviour connectedCharacter;
        int connectedCharacterObjectId = NetworkObject.UNSET_OBJECTID_VALUE;
        byte connectedCharacterComponentIndex = NetworkBehaviour.UNSET_NETWORKBEHAVIOUR_ID;

        public bool equipped => weaponRoot.activeInHierarchy;

        public Action<ToolBaseShared> equippedEvent, unequippedEvent;
        public Action<int> currentAmmoEvent, maxAmmoEvent;
        public static Action<ToolBaseShared> equippedStaticEvent, unequippedStaticEvent;
        public static Action<ToolBaseShared> createStaticEvent, destroyStaticEvent;

        protected AudioSource audioSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            equippedStaticEvent = null;
            unequippedStaticEvent = null;
            createStaticEvent = null;
            destroyStaticEvent = null;
        }
        void Equip() {
            equippedStaticEvent?.Invoke(this);
            equippedEvent?.Invoke(this);
            weaponRoot.SetActive(true);

            PlayAudio(equipAudio);
        }
        public void Unequip() {
            unequippedStaticEvent?.Invoke(this);
            unequippedEvent?.Invoke(this);
            weaponRoot.SetActive(false);

            PlayAudio(unequipAudio);
        }
        [ObserversRpc(RunLocally = true, ExcludeOwner = true)]
        public void EquipOthersRpc() {
            Equip();
        }
        [ObserversRpc(RunLocally = true, ExcludeOwner = true)]
        public void UnequipOthersRpc() {
            Unequip();
        }
        [ObserversRpc(RunLocally = true)]
        public void EquipServer() {
            Equip();
        }
        [ObserversRpc(RunLocally = true)]
        public void UnequipServer() {
            Unequip();
        }
#if !UNITY_SERVER
        public void EquipClient() {
            Equip();
            EquipServerRpc();
        }
        public void UnequipClient() {
            Unequip();
            if (IsController)
                UnequipServerRpc();
        }
#endif
        [ServerRpc]
        public void EquipServerRpc() {
            EquipOthersRpc();
        }
        [ServerRpc]
        public void UnequipServerRpc() {
            UnequipOthersRpc();
        }
        public override void WritePayload(NetworkConnection connection, Writer writer) {
            // Spawn payloads are read before every object in the client's spawn
            // cache has necessarily been registered. Writing a NetworkBehaviour
            // directly makes FishNet resolve it too early and can return null for
            // a character being spawned in the same batch.
            writer.WriteNetworkObjectId(connectedCharacter != null
                ? connectedCharacter.NetworkObject.ObjectId
                : NetworkObject.UNSET_OBJECTID_VALUE);
            writer.WriteUInt8Unpacked(connectedCharacter != null
                ? connectedCharacter.ComponentIndex
                : NetworkBehaviour.UNSET_NETWORKBEHAVIOUR_ID);
        }

        public override void ReadPayload(NetworkConnection connection, Reader reader) {
            connectedCharacterObjectId = reader.ReadNetworkObjectId();
            connectedCharacterComponentIndex = reader.ReadUInt8Unpacked();
        }
#if !UNITY_SERVER
        
        public override void OnStartClient() {
            weaponRoot.SetActive(false); // unequipped by default
            // A tool can be spawned and despawned in the same client tick, or
            // become visible without its character. Do not add client scripts
            // whose Awake methods require a valid character in those cases.
            if (connectedCharacter == null)
                return;
            if (IsOwner) {
                SpawnClientScript();
            } else if (clientObserver != "") {
                Type clientObserverType = Type.GetType(clientObserver);
                gameObject.AddComponent(clientObserverType);
            }
            createStaticEvent?.Invoke(this);
        }
#else
        public override void OnStartServer() {
            weaponRoot.SetActive(false); // unequipped by default
            if (serverScript != ""){
                Type serverScriptType = Type.GetType(serverScript);
                gameObject.AddComponent(serverScriptType);
            }
            if (!Owner.IsValid) {
                SpawnClientScript();
            }
            weaponRoot.SetActive(false);
            createStaticEvent?.Invoke(this);
        }
        public void UpdateServerCooldown(float cooldown) {
            serverCooldownSync.Value = cooldown + NetworkHelper.GetServerTime();
        }
#endif
        public override void OnStartNetwork() {
            ResolveConnectedCharacter();
            if (connectedCharacter == null) {
                Debug.LogWarning($"Could not attach {name}: connected character ObjectId {connectedCharacterObjectId} is not spawned or visible.", this);
                return;
            }

            Transform rightHand = TransformHelper.FindChildRecursive(connectedCharacter.transform, ParentObjectName);
            if (rightHand == null) {
                Debug.LogError($"Could not attach {name}: {ParentObjectName} was not found under {connectedCharacter.name}.", this);
                return;
            }
            transform.SetParent(rightHand, false);
        }
        void ResolveConnectedCharacter() {
            if (connectedCharacter != null ||
                connectedCharacterObjectId == NetworkObject.UNSET_OBJECTID_VALUE ||
                connectedCharacterComponentIndex == NetworkBehaviour.UNSET_NETWORKBEHAVIOUR_ID)
                return;

            NetworkObject characterObject = null;
            if (NetworkManager.ClientManager.Started)
                NetworkManager.ClientManager.Objects.Spawned.TryGetValue(connectedCharacterObjectId, out characterObject);
            if (characterObject == null && NetworkManager.ServerManager.Started)
                NetworkManager.ServerManager.Objects.Spawned.TryGetValue(connectedCharacterObjectId, out characterObject);

            if (characterObject != null && connectedCharacterComponentIndex < characterObject.NetworkBehaviours.Count)
                connectedCharacter = characterObject.NetworkBehaviours[connectedCharacterComponentIndex];
        }
        void SpawnClientScript() {
            if (clientScript != "") {
                currentAmmo = maxClipAmmo;
                Type clientScriptType = Type.GetType(clientScript);
                gameObject.AddComponent(clientScriptType);
            }
        }
        protected virtual void Awake() {
            weaponRoot = transform.GetChild(0).gameObject;
            audioSource = weaponRoot.GetComponent<AudioSource>();
        }
#if UNITY_EDITOR
        void OnValidate() {
            // Runtime Inspector edits change serialized fields, not SyncVars.
            // Mirror them immediately when this is the spawned server copy.
            ApplyInitialStatsLive();
        }
#endif
        public override void OnStopNetwork() {
            if (equipped)
                Unequip();
            destroyStaticEvent?.Invoke(this);
        }


        // RPC HIT DETECTION
#pragma warning disable CS0067
        public event Action<NetworkObject> hitEvent;
#pragma warning restore CS0067
        public void OnHit(NetworkObject gameCharacter) {
#if UNITY_SERVER
            hitEvent.Invoke(gameCharacter);
#else
        _OnHitRpc(gameCharacter);
#endif
        }
        [ServerRpc(RequireOwnership = true)]
        public void _OnHitRpc(NetworkObject gameCharacter) {
            OnHit(gameCharacter);
        }

        // AUDIO
        protected virtual void PlayAudio(AudioClip audioClip) {
#if ENABLE_AUDIO && !UNITY_SERVER
            if (audioClip == null)
                return;
            if (audioSource.isActiveAndEnabled)
                MusicService.CreateOneShot(audioSource, audioClip);
#endif
        }
        public virtual void PlayAudio(int audioClipIdx) {
            if (extraAudios[audioClipIdx] == null)
                return;
            PlayAudioRpc(audioClipIdx);
        }
        [SharedRpc(RunOnServer = false, RunOnCallingClient = true, RunOnCallingServer = false)]
        protected virtual void PlayAudioRpc(int audioClipIdx) {
            PlayAudio(extraAudios[audioClipIdx]);
        }
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(RyanAssets.Tools.Shared.ToolBaseShared), true)]
public class ToolBaseSharedEditor : UnityEditor.Editor {
    public override bool RequiresConstantRepaint() => UnityEngine.Application.isPlaying;

    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        if (!UnityEngine.Application.isPlaying)
            return;

        var tool = (RyanAssets.Tools.Shared.ToolBaseShared)target;
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Live Runtime Stats", UnityEditor.EditorStyles.boldLabel);
        using (new UnityEditor.EditorGUI.DisabledScope(true)) {
            UnityEditor.EditorGUILayout.IntField("Stamina Cost", tool.staminaCost);
            UnityEditor.EditorGUILayout.IntField("Hit Damage", tool.hitDamage);
            UnityEditor.EditorGUILayout.FloatField("Attack Cooldown", tool.attackCooldown);
            UnityEditor.EditorGUILayout.FloatField("Reload Duration", tool.reloadDuration);
            UnityEditor.EditorGUILayout.IntField("Max Clip Ammo", tool.maxClipAmmo);
        }

        if (tool.IsSpawned && !tool.IsServerStarted)
            UnityEditor.EditorGUILayout.HelpBox("Edit init values in the server Editor to apply them live to all clients.", UnityEditor.MessageType.Info);
    }
}
#endif
