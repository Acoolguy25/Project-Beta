using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.DataService;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    [RequireComponent(typeof(EffectsComponent), typeof(HealthComponent))]
    public class GameCharacter : NetworkBehaviour, IEntity {
        public const string AnonymousDisplayName = "Anonymous";

        [SerializeField] public Transform CharacterCamera;
        [SerializeField] private EffectsComponent effectsComponent;
        [SerializeField] private HealthComponent healthComponent;
        [SerializeField] private bool fallHeightEnabled = true;
        [SerializeField] private float fallenPartsDestroyHeight = 0.0f;

        public EffectsComponent EffectsComponent => effectsComponent;
        public HealthComponent HealthComponent => healthComponent;

        public static Dictionary<TeamColor, HashSet<GameCharacter>> TeamToCharacter = new();
        public static event Action<GameCharacter> GameCharacterAdded, GameCharacterRemoved;
        public event Action<GameCharacter> MyGameCharacterRemoved;

        public readonly SyncVar<TeamConfig> TeamSync = new(new(), new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<ToolBaseShared> ActiveTool = new(null, new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<string> DisplayNameSync = new(AnonymousDisplayName);
        public readonly SyncVar<Vector3> CharacterScale = new(Vector3.one);
        public readonly SyncVar<bool> CanSpectate = new(true);

        public TeamConfig Team => TeamSync.Value;
        public string DisplayName {
            get => NormalizeDisplayName(DisplayNameSync.Value);
            set => DisplayNameSync.Value = NormalizeDisplayName(value);
        }
        public bool FallHeightEnabled => fallHeightEnabled;
        public float FallenPartsDestroyHeight => fallenPartsDestroyHeight;
        public bool IsDead => HealthComponent.IsDead;
        public bool IsFullHealth => HealthComponent.IsFullHealth;
        public SyncVar<long> Health => HealthComponent.Health;
        public SyncVar<long> MaxHealth => HealthComponent.MaxHealth;
        public SyncDictionary<CharacterEffect, float> ActiveEffects => EffectsComponent.ActiveEffects;
        public ToolBaseShared[] Tools => gameObject.GetComponentsInChildren<ToolBaseShared>(false);
        public event Action<DamageType, IEntity> OnDamage {
            add => HealthComponent.OnDamage += value;
            remove => HealthComponent.OnDamage -= value;
        }
        public event Action<DamageType, IEntity> OnDied {
            add => HealthComponent.OnDied += value;
            remove => HealthComponent.OnDied -= value;
        }

        public static string NormalizeDisplayName(string value) =>
            string.IsNullOrWhiteSpace(value) ? AnonymousDisplayName : value.Trim();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            GameCharacterAdded = null;
            GameCharacterRemoved = null;
            TeamToCharacter.Clear();
        }

        protected virtual void Awake() {
            effectsComponent ??= GetComponent<EffectsComponent>();
            healthComponent ??= GetComponent<HealthComponent>();
            if (effectsComponent == null || healthComponent == null)
                throw new MissingComponentException($"{nameof(GameCharacter)} requires {nameof(EffectsComponent)} and {nameof(HealthComponent)}.");
            healthComponent.OnDied += SharedDied;
        }

        protected virtual void OnDestroy() {
            DisplayNameSync.OnChange -= OnDisplayNameChanged;
            if (healthComponent != null)
                healthComponent.OnDied -= SharedDied;
        }

        private void OnDisplayNameChanged(string _, string newValue, bool __) {
            gameObject.name = $"{NormalizeDisplayName(newValue)} ({NetworkObject.ObjectId})";
        }

        public void SwitchTool(ToolBaseShared tool) {
            if (tool == ActiveTool.Value || (IsDead && tool))
                return;
#if UNITY_SERVER
            if (ActiveTool.Value) {
                if (gameObject != null)
                    ActiveTool.Value.UnequipServer();
                else
                    ActiveTool.Value.Unequip();
            }
            if (tool)
                tool.EquipServer();
#else
            if (ActiveTool.Value)
                ActiveTool.Value.UnequipClient();
            if (tool)
                tool.EquipClient();
#endif
            ActiveTool.Value = tool;
        }

        public bool Equipped(ToolBaseShared tool) => ActiveTool.Value == tool;
        public bool IsEffectActive(CharacterEffect effect) => EffectsComponent.IsEffectActive(effect);

#if UNITY_EDITOR
        [SerializeField] private TeamConfig teamEditor;

        protected virtual void OnEnable() {
            TeamSync.OnChange += UpdateTeamEditorOptions;
        }

        protected virtual void OnDisable() {
            TeamSync.OnChange -= UpdateTeamEditorOptions;
        }

        protected virtual void UpdateTeamEditorOptions(TeamConfig _, TeamConfig newValue, bool __) {
            teamEditor = newValue;
        }
#endif

#if UNITY_SERVER
        [Server]
        public virtual bool IsProtected(GameCharacter sourceCharacter = null, DamageType damageType = DamageType.None) {
            return HealthComponent.IsProtected(sourceCharacter, damageType);
        }

        [Server]
        public bool IsProtected(GameCharacter sourceCharacter) => IsProtected(sourceCharacter, DamageType.None);

        [Server]
        public virtual bool TakeDamage(long damage, DamageType source = DamageType.None, NetworkObject sourceObject = null) {
            IEntity sourceEntity = sourceObject ? sourceObject.GetComponent<IEntity>() : null;
            if (sourceObject != null && sourceEntity == null)
                Debug.LogError($"Damage source object {sourceObject.name} does not implement {nameof(IEntity)}.");
            return HealthComponent.TakeDamage(damage, source, sourceEntity);
        }

        [Server] public virtual void HealHealth(long hitpoints) => HealthComponent.HealHealth(hitpoints);
        [Server] public virtual void HealMaxHealth(long hitpoints) => HealthComponent.HealMaxHealth(hitpoints);
        [Server] public virtual void AddEffect(CharacterEffect effect, float duration) => EffectsComponent.AddEffect(effect, duration);
        [Server] public virtual void RemoveEffect(CharacterEffect effect) => EffectsComponent.RemoveEffect(effect);
        [Server] public virtual void ClearEffects() => EffectsComponent.ClearEffects();
        [Server] public virtual void Init(long hp, long maxHp) => HealthComponent.Init(hp, maxHp);
        [Server] public void Init(long hp) => HealthComponent.Init(hp);
        [Server] public void InitDefaultEffects() => EffectsComponent.AddEffect(CharacterEffect.Invul, 5f);
        [Server] public virtual void Kill(DamageType source, NetworkObject sourceObject = null) {
            HealthComponent.Kill(source, sourceObject ? sourceObject.GetComponent<IEntity>() : null);
        }

        [Server]
        public virtual void SetTeam(TeamConfig teamConfig) {
            if (IsDead) {
                //Debug.LogWarning($"Cannot set team for dead character {gameObject.name}");
                return;
            }
            UpdateTeamRegistry(Team, teamConfig);
            TeamSync.Value = teamConfig;
#if UNITY_EDITOR
            UpdateTeamEditorOptions(default, teamConfig, true);
#endif
        }

        [Server]
        private void FixedUpdate() {
            if (FallHeightEnabled && IsSpawned && transform.position.y < FallenPartsDestroyHeight) {
                Kill(DamageType.Fall);
                InstanceFinder.ServerManager.Despawn(gameObject);
            }
        }

        [Server]
        public override void OnStopServer() {
            base.OnStopServer();
            if (!InstanceFinder.IsServerStarted)
                return;

            foreach (ToolBaseShared tool in GetComponentsInChildren<ToolBaseShared>(true)) {
                // OnStopServer may run from NetworkObject.OnDestroy after Unity has
                // already invalidated this hierarchy. Such children cannot be despawned.
                if (tool.IsSpawned && tool.gameObject.scene.IsValid())
                    tool.Despawn();
            }
        }
#else
        public override void OnStartClient() {
            TeamSync.OnChange += UpdateTeamClient;
            if (!IsDead)
                UpdateTeamRegistry(default, Team);
            GameCharacterAdded?.Invoke(this);
        }

        private void UpdateTeamClient(TeamConfig oldTeam, TeamConfig newTeam, bool _) {
            UpdateTeamRegistry(oldTeam, newTeam);
        }
#endif

        protected void RemoveTeamRegistry(TeamConfig team) {
            if (team != null && TeamToCharacter.ContainsKey(team.team))
                TeamToCharacter[team.team].Remove(this);
        }

        protected void UpdateTeamRegistry(TeamConfig oldTeam, TeamConfig newTeam) {
            RemoveTeamRegistry(oldTeam);
            if (!TeamToCharacter.ContainsKey(newTeam.team))
                TeamToCharacter[newTeam.team] = new HashSet<GameCharacter>();
            TeamToCharacter[newTeam.team].Add(this);
        }

        public virtual TeamConfig GetTeam() => Team;

        protected virtual void SharedDied(DamageType source, IEntity sourceEntity) {
            SwitchTool(null);
            if (!transform.tag.StartsWith("Dead"))
                transform.tag = "Dead" + transform.tag;
            RemoveTeamRegistry(GetTeam());
        }

        public override void OnStopNetwork() {
            DisplayNameSync.OnChange -= OnDisplayNameChanged;
            RemoveTeamRegistry(GetTeam());
            SwitchTool(null);
            GameCharacterRemoved?.Invoke(this);
            MyGameCharacterRemoved?.Invoke(this);
        }

        public override void OnStartNetwork() {
            DisplayNameSync.OnChange -= OnDisplayNameChanged;
            DisplayNameSync.OnChange += OnDisplayNameChanged;
            if (ActiveTool.Value)
#if UNITY_SERVER
                ActiveTool.Value.EquipServer();
#else
                ActiveTool.Value.EquipClient();
#endif
            if (IsDead) {
#if UNITY_SERVER
                Kill(DamageType.None);
#else
                SharedDied(DamageType.None, null);
#endif
            }
            OnDisplayNameChanged(default, DisplayName, IsServerStarted);
        }
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(RyanAssets.Characters.Shared.GameCharacter), true)]
public class GameCharacterEditor : UnityEditor.Editor {
    public override bool RequiresConstantRepaint() => UnityEngine.Application.isPlaying;

    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        var character = (RyanAssets.Characters.Shared.GameCharacter)target;
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Runtime State", UnityEditor.EditorStyles.boldLabel);

        using (new UnityEditor.EditorGUI.DisabledScope(true)) {
            UnityEditor.EditorGUILayout.TextField("Display Name", character.DisplayName);
            UnityEditor.EditorGUILayout.EnumPopup("Team", character.Team?.team ?? TeamColor.None);
            UnityEditor.EditorGUILayout.EnumPopup("Displayed Team", character.Team?.displayTeam ?? TeamColor.None);
            UnityEditor.EditorGUILayout.ObjectField("Active Tool", character.ActiveTool.Value, typeof(RyanAssets.Tools.Shared.ToolBaseShared), true);
            UnityEditor.EditorGUILayout.Vector3Field("Character Scale", character.CharacterScale.Value);
            UnityEditor.EditorGUILayout.Toggle("Can Spectate", character.CanSpectate.Value);
            UnityEditor.EditorGUILayout.LongField("Health", character.Health?.Value ?? 0L);
            UnityEditor.EditorGUILayout.LongField("Max Health", character.MaxHealth?.Value ?? 0L);
            UnityEditor.EditorGUILayout.Toggle("Is Dead", character.HealthComponent != null && character.IsDead);
            UnityEditor.EditorGUILayout.Toggle("Is Full Health", character.HealthComponent != null && character.IsFullHealth);
            UnityEditor.EditorGUILayout.IntField("Active Effects", character.ActiveEffects?.Count ?? 0);
            UnityEditor.EditorGUILayout.IntField("Equipped Tools", character.Tools?.Length ?? 0);
            UnityEditor.EditorGUILayout.Toggle("Is Spawned", character.IsSpawned);
            UnityEditor.EditorGUILayout.IntField("Network Object ID", character.NetworkObject != null ? character.NetworkObject.ObjectId : -1);
        }

    }
}
#endif
