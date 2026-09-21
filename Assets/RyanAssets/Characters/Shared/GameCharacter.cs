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
    public class GameCharacter : EntityBase, ITeam {
        public const string AnonymousDisplayName = "Anonymous";
        [Tooltip("Whether this character receives the shared overhead name and health UI.")]
        [SerializeField] bool showNameTag = true;
        public bool ShowNameTag => showNameTag;

        [Header("Character References")]
        [SerializeField] public Transform CharacterCamera;

        [Header("Fall Handling")]
        [SerializeField] private bool fallHeightEnabled = true;
        [SerializeField] private float fallenPartsDestroyHeight = 0.0f;

        // Ordered so that anything cycling through a team (spectating, target selection) keeps a
        // stable sequence instead of the arbitrary order a HashSet produces after each add.
        public static Dictionary<TeamColor, List<GameCharacter>> TeamToCharacter = new();
        public static event Action<GameCharacter> GameCharacterAdded, GameCharacterRemoved;
        public event Action<GameCharacter> MyGameCharacterRemoved;
        // The bucket this character is actually in, or null when it is not registered.
        // Registration reads Team, but removal used to re-read the virtual GetTeam(), which
        // resolves through Owner and PlayerData and returns a different team -- or the White
        // default once Owner is cleared during despawn. Every such removal missed the real
        // bucket and left a destroyed character behind for the next iteration to trip over.
        // Recording the key the add used makes the two halves symmetric by construction.
        TeamColor? registeredTeam;

        public readonly SyncVar<TeamConfig> TeamSync = new(new(), new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<ToolBaseShared> ActiveTool = new(null, new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<string> DisplayNameSync = new(AnonymousDisplayName);
        public readonly SyncVar<Vector3> CharacterScale = new(Vector3.one);
        public readonly SyncVar<bool> CanSpectate = new(true);

        public override TeamConfig Team => TeamSync.Value;
        public override string DisplayName {
            get => NormalizeDisplayName(DisplayNameSync.Value);
            set => DisplayNameSync.Value = NormalizeDisplayName(value);
        }
        public bool FallHeightEnabled => fallHeightEnabled;
        public float FallenPartsDestroyHeight => fallenPartsDestroyHeight;
        public ToolBaseShared[] Tools => gameObject.GetComponentsInChildren<ToolBaseShared>(false);
        public static string NormalizeDisplayName(string value) =>
            string.IsNullOrWhiteSpace(value) ? AnonymousDisplayName : value.Trim();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            GameCharacterAdded = null;
            GameCharacterRemoved = null;
            TeamToCharacter.Clear();
        }

        protected override void Awake() {
            base.Awake();
            OnDied += SharedDied;
            OnRevive += SharedRevived;
            CharacterScale.OnChange += OnCharacterScaleChanged;
        }

        protected virtual void OnCharacterScaleChanged(Vector3 oldScale, Vector3 newScale, bool asServer) {
            ApplyScale(newScale);
        }

        private void ApplyScale(Vector3 newScale) {
            transform.localScale = newScale;
            Physics.SyncTransforms();

            foreach (Rigidbody rigidbody in GetComponentsInChildren<Rigidbody>(true)) {
                rigidbody.ResetCenterOfMass();
                rigidbody.ResetInertiaTensor();
            }

            // PhysX retains each joint constraint at the scale where it was created.
            // Reconnecting rebuilds the constraint from the newly scaled transforms.
            foreach (Joint joint in GetComponentsInChildren<Joint>(true)) {
                Rigidbody connectedBody = joint.connectedBody;
                if (connectedBody == null)
                    continue;

                joint.connectedBody = null;
                joint.connectedBody = connectedBody;
            }
        }

        protected virtual void OnDestroy() {
            // OnStopNetwork already does this for a normal despawn. Repeating it here covers
            // teardown paths that destroy the object without one, so the registry can never
            // hand a destroyed character to an iterator.
            RemoveTeamRegistry();
            DisplayNameSync.OnChange -= OnDisplayNameChanged;
            if (HealthComponent != null)
            {
                OnDied -= SharedDied;
                OnRevive -= SharedRevived;
            }
            CharacterScale.OnChange -= OnCharacterScaleChanged;
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

        public void SetScale(Vector3 newScale) {
            CharacterScale.Value = newScale;
            if (transform.localScale != newScale)
                ApplyScale(newScale);
        }

        public bool Equipped(ToolBaseShared tool) => ActiveTool.Value == tool;

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
        [Server] public void InitDefaultEffects() => EffectsComponent.AddEffect(CharacterEffect.Invul, 5f);

        [Server]
        public virtual void SetTeam(TeamConfig teamConfig) {
            // Dead characters have a role, but only living characters belong in
            // the attack-team registry. SharedRevived adds the new role back.
            if (!IsDead)
                UpdateTeamRegistry(teamConfig);
            TeamSync.Value = teamConfig;
#if UNITY_EDITOR
            UpdateTeamEditorOptions(default, teamConfig, true);
#endif
        }

        void ITeam.SetTeam(TeamConfig teamConfig) => SetTeam(teamConfig);

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
                UpdateTeamRegistry(Team);
            GameCharacterAdded?.Invoke(this);
        }

        private void UpdateTeamClient(TeamConfig oldTeam, TeamConfig newTeam, bool _) {
            if (IsDead)
                RemoveTeamRegistry();
            else
                UpdateTeamRegistry(newTeam);
        }
#endif

#if UNITY_SERVER
        [Server]
        protected virtual void FixedUpdate() {
            if (FallHeightEnabled && IsSpawned && transform.position.y < FallenPartsDestroyHeight) {
                Kill(DamageType.Fall);
                InstanceFinder.ServerManager.Despawn(gameObject);
            }
        }
#else
        protected virtual void FixedUpdate() { }
#endif

        /// <summary>
        /// Drops this character out of the registry, whichever bucket it actually sits in.
        /// </summary>
        protected void RemoveTeamRegistry() {
            if (registeredTeam is not TeamColor team)
                return;

            // Cleared before the removal so a re-entrant registry call cannot see a key that
            // no longer holds this character.
            registeredTeam = null;
            if (TeamToCharacter.TryGetValue(team, out List<GameCharacter> characters))
                characters.Remove(this);
        }

        /// <summary>
        /// Moves this character into <paramref name="newTeam"/>'s bucket, leaving whichever
        /// bucket it currently occupies.
        /// </summary>
        protected void UpdateTeamRegistry(TeamConfig newTeam) {
            TeamColor team = newTeam != null ? newTeam.realTeam : default;
            if (registeredTeam == team)
                return;

            RemoveTeamRegistry();
            if (!TeamToCharacter.TryGetValue(team, out List<GameCharacter> characters))
                TeamToCharacter[team] = characters = new List<GameCharacter>();
            characters.Add(this);
            registeredTeam = team;
        }

        public static int TeamCount(TeamColor teamColor) => TeamToCharacter.ContainsKey(teamColor) ? TeamToCharacter[teamColor].Count : 0;
        public virtual TeamConfig GetTeam() => Team;

        protected virtual void SharedDied(DamageType source, IEntity sourceEntity) {
            SwitchTool(null);
            if (!transform.tag.StartsWith("Dead"))
                transform.tag = "Dead" + transform.tag;
            RemoveTeamRegistry();
        }

        protected virtual void SharedRevived() {
            if (transform.tag.StartsWith("Dead"))
                transform.tag = transform.tag.Substring("Dead".Length);
            UpdateTeamRegistry(GetTeam());
        }

        public override void OnStopNetwork() {
            DisplayNameSync.OnChange -= OnDisplayNameChanged;
            RemoveTeamRegistry();
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
            DrawSectionHeader("Identity & Team");
            UnityEditor.EditorGUILayout.TextField("Display Name", character.DisplayName);
            UnityEditor.EditorGUILayout.EnumPopup("Team", character.Team?.realTeam ?? TeamColor.None);
            UnityEditor.EditorGUILayout.EnumPopup("Displayed Team", character.Team?.displayTeam ?? TeamColor.None);

            DrawSectionHeader("Equipment & Character");
            UnityEditor.EditorGUILayout.ObjectField("Active Tool", character.ActiveTool.Value, typeof(RyanAssets.Tools.Shared.ToolBaseShared), true);
            UnityEditor.EditorGUILayout.Vector3Field("Character Scale", character.CharacterScale.Value);
            UnityEditor.EditorGUILayout.Toggle("Can Spectate", character.CanSpectate.Value);

            DrawSectionHeader("Health & Effects");
            UnityEditor.EditorGUILayout.LongField("Health", character.Health?.Value ?? 0L);
            UnityEditor.EditorGUILayout.LongField("Max Health", character.MaxHealth?.Value ?? 0L);
            UnityEditor.EditorGUILayout.Toggle("Is Dead", character.HealthComponent != null && character.IsDead);
            UnityEditor.EditorGUILayout.Toggle("Is Full Health", character.HealthComponent != null && character.IsFullHealth);
            UnityEditor.EditorGUILayout.IntField("Active Effects", character.ActiveEffects?.Count ?? 0);
            UnityEditor.EditorGUILayout.IntField("Equipped Tools", character.Tools?.Length ?? 0);

            DrawSectionHeader("Network");
            UnityEditor.EditorGUILayout.Toggle("Is Spawned", character.IsSpawned);
            UnityEditor.EditorGUILayout.IntField("Network Object ID", character.NetworkObject != null ? character.NetworkObject.ObjectId : -1);
        }
    }

    private static void DrawSectionHeader(string label) {
        UnityEditor.EditorGUILayout.Space(6f);
        UnityEditor.EditorGUILayout.LabelField(label, UnityEditor.EditorStyles.boldLabel);
    }
}
#endif
