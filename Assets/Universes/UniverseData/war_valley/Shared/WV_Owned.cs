using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Marks a structure or unit as belonging to one player's command.
    /// <para>
    /// War Valley deliberately keeps these objects server-owned at the FishNet level and records the
    /// commander separately. Handing the NetworkObject itself to a client connection would make
    /// FishNet despawn that player's whole base the moment they dropped, and would put a client on
    /// the authority side of gameplay state.
    /// </para>
    /// </summary>
    public sealed class WV_Owned : NetworkBehaviour {
        /// <summary>Sentinel for objects that belong to the match rather than to a player.</summary>
        public const int NoOwner = -1;

        readonly SyncVar<int> ownerClientId = new(NoOwner);

        static readonly List<WV_Owned> all = new();

        EntityBase entity;

        /// <summary>Every spawned owned object, for server sweeps and client selection.</summary>
        public static IReadOnlyList<WV_Owned> All => all;

        public int OwnerClientId => ownerClientId.Value;

        /// <summary>
        /// The side this object fights for, read from the entity it marks. Ownership says whose it
        /// is; this says whose side it is on, which is what alliance rules compare.
        /// </summary>
        public TeamConfig Team => entity != null ? entity.Team : null;

        void Awake() {
            entity = GetComponent<EntityBase>();
        }

        /// <summary>
        /// Raised on every build when the commander of this object is set or changes. Presentation
        /// binds to this rather than polling, because ownership is assigned a frame after the object
        /// spawns and a client would otherwise paint the object in the unowned colour first.
        /// </summary>
        public event Action<int> OwnerChanged;

        public bool IsOwnedBy(int clientId) => clientId != NoOwner && ownerClientId.Value == clientId;

        /// <summary>
        /// True between network start and stop. A placement preview is an unspawned copy of the
        /// prefab, and presentation keyed to ownership leaves it alone.
        /// </summary>
        public bool IsLive { get; private set; }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            IsLive = true;
            all.Add(this);
            ownerClientId.OnChange += HandleOwnerChanged;
            OwnerChanged?.Invoke(ownerClientId.Value);
        }

        public override void OnStopNetwork() {
            IsLive = false;
            ownerClientId.OnChange -= HandleOwnerChanged;
            all.Remove(this);
            base.OnStopNetwork();
        }

        void HandleOwnerChanged(int previous, int next, bool asServer) => OwnerChanged?.Invoke(next);

        [Server]
        public void SetOwnerClientId(int clientId) {
            ownerClientId.Value = clientId;
            // The server never receives its own SyncVar OnChange, so raise it here too. Otherwise a
            // listen-server host would show its own buildings untinted.
            OwnerChanged?.Invoke(clientId);
        }

        /// <summary>
        /// Resolves the commander of an arbitrary object in the scene. Used by order validation, which
        /// receives NetworkObject ids and must not assume the component sits on the root.
        /// </summary>
        public static bool TryGetOwnerClientId(Component target, out int clientId) {
            WV_Owned owned = target != null ? target.GetComponentInParent<WV_Owned>() : null;
            clientId = owned != null ? owned.OwnerClientId : NoOwner;
            return owned != null && clientId != NoOwner;
        }
    }
}
