using System;
using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Component;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;
using Object = UnityEngine.Object;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The units and foot troops the local commander has selected, and the roster of characters
    /// they are picked from.
    /// <para>
    /// Selection is deliberately local. Nothing about which units are highlighted crosses the wire;
    /// only the resulting order does, naming the units it applies to, and the server re-checks that
    /// the sender actually commands each one.
    /// </para>
    /// <para>
    /// A commander's own foot troops are ordinary characters rather than <see cref="WV_Unit"/>s, and
    /// their ownership is recorded only on the server, so this client recognises its own by the
    /// commander colour the server already replicates on every troop's team: the same colour their
    /// buildings and their name tags carry.
    /// </para>
    /// <para>
    /// Selected troops wear an authored ring, and every gunner (selected or not) carries a range
    /// indicator that brightens while it is selected.
    /// </para>
    /// </summary>
    public sealed class WV_CommanderSelection {
        readonly Func<int> localClientId;
        readonly Func<WV_RangeIndicator> troopRangePrefab;
        readonly GameObject troopSelectionIndicator;
        readonly int maxSelection;

        readonly List<WV_Unit> units = new();
        readonly List<GameCharacter> troops = new();
        readonly List<WV_Unit> ownedScratch = new();

        // Every character this client can see. Troops have no client-visible roster of their own, so
        // the shared character events are the cheapest source of one that stays correct.
        readonly List<GameCharacter> characters = new();
        readonly Dictionary<GameCharacter, GameObject> troopIndicators = new();
        readonly Dictionary<GameCharacter, WV_RangeIndicator> troopRanges = new();

        public WV_CommanderSelection(
            Func<int> localClientId, Func<WV_RangeIndicator> troopRangePrefab,
            GameObject troopSelectionIndicator, int maxSelection) {
            this.localClientId = localClientId;
            this.troopRangePrefab = troopRangePrefab;
            this.troopSelectionIndicator = troopSelectionIndicator;
            this.maxSelection = Mathf.Max(1, maxSelection);
        }

        public IReadOnlyList<WV_Unit> Units => units;
        public IReadOnlyList<GameCharacter> Troops => troops;
        /// <summary>Every character this client currently sees, troops and players alike.</summary>
        public IReadOnlyList<GameCharacter> Characters => characters;
        public int Count => units.Count + troops.Count;
        public bool IsFull => Count >= maxSelection;
        public int MaxSelection => maxSelection;

        // --- Roster -------------------------------------------------------------

        /// <summary>Starts following the shared character events and takes in everyone already spawned.</summary>
        public void Enable() {
            GameCharacter.GameCharacterAdded += HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved += HandleCharacterRemoved;
            foreach (List<GameCharacter> team in GameCharacter.TeamToCharacter.Values) {
                foreach (GameCharacter character in team)
                    HandleCharacterAdded(character);
            }
        }

        /// <summary>Stops following the roster and drops the selection and every indicator it placed.</summary>
        public void Disable() {
            GameCharacter.GameCharacterAdded -= HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved -= HandleCharacterRemoved;
            ReleaseRoster();
            Clear();
        }

        /// <summary>Unsubscribes from every character and destroys the range indicators. Safe to call twice.</summary>
        public void ReleaseRoster() {
            foreach (GameCharacter character in characters) {
                if (character != null)
                    character.DisplayNameChanged -= HandleTroopNameChanged;
            }
            foreach (WV_RangeIndicator range in troopRanges.Values) {
                if (range != null)
                    Object.Destroy(range.gameObject);
            }
            troopRanges.Clear();
            characters.Clear();
        }

        void HandleCharacterAdded(GameCharacter character) {
            if (character == null || characters.Contains(character))
                return;
            characters.Add(character);
            character.DisplayNameChanged += HandleTroopNameChanged;
            RefreshTroopRange(character);
        }

        void HandleTroopNameChanged(EntityBase entity) => RefreshTroopRange((GameCharacter)entity);

        void HandleCharacterRemoved(GameCharacter character) {
            character.DisplayNameChanged -= HandleTroopNameChanged;
            if (troopRanges.Remove(character, out WV_RangeIndicator range) && range != null)
                Object.Destroy(range.gameObject);
            characters.Remove(character);
            Remove(character);
        }

        /// <summary>
        /// Gives a ranged troop its range ring, or takes it away. A troop's kind is known only from
        /// its display name, which can arrive after the character does, so this runs again on rename.
        /// </summary>
        void RefreshTroopRange(GameCharacter character) {
            bool ranged = character is not LocalCharacter && !character.IsDead
                && WV_Rules.TryGetTroopKind(character.DisplayName, out WV_TroopKind kind)
                && WV_TroopCatalog.Get(kind).UsesGun;
            if (!ranged) {
                if (troopRanges.Remove(character, out WV_RangeIndicator old) && old != null)
                    Object.Destroy(old.gameObject);
                return;
            }
            WV_RangeIndicator prefab = troopRangePrefab();
            if (prefab == null || troopRanges.ContainsKey(character))
                return;
            WV_RangeIndicator range = Object.Instantiate(prefab, character.transform);
            range.transform.localPosition = Vector3.zero;
            range.BindEntity(character);
            range.SetRadius(WV_Rules.GunnerEngageRange);
            range.SetSelected(troops.Contains(character));
            troopRanges.Add(character, range);
        }

        // --- Ownership ----------------------------------------------------------

        /// <summary>
        /// True for a foot troop this client commands. Player characters are excluded: a commander
        /// drives their own avatar directly and should never be able to box-select it as a unit.
        /// </summary>
        public bool IsCommandableTroop(GameCharacter character) {
            if (character == null || character is LocalCharacter || character.IsDead || !character.IsSpawned)
                return false;

            int clientId = localClientId();
            if (clientId == WV_Owned.NoOwner)
                return false;

            TeamConfig team = character.Team;
            return team != null && team.displayTeam == WV_Rules.GetCommanderColor(clientId);
        }

        /// <summary>Living troops this client commands: the same ones it can select.</summary>
        public int CountOwnTroops() {
            int count = 0;
            foreach (GameCharacter character in characters) {
                if (IsCommandableTroop(character))
                    count++;
            }
            return count;
        }

        // --- Membership ---------------------------------------------------------

        public bool Contains(WV_Unit unit) => units.Contains(unit);

        public bool Contains(GameCharacter troop) => troops.Contains(troop);

        public void Add(WV_Unit unit) {
            if (unit == null || unit.IsDead || units.Contains(unit))
                return;
            unit.SelectedLocally = true;
            units.Add(unit);
        }

        public void Add(GameCharacter troop) {
            if (troop == null || troops.Contains(troop))
                return;

            troops.Add(troop);
            if (troopRanges.TryGetValue(troop, out WV_RangeIndicator range))
                range.SetSelected(true);
            if (troopSelectionIndicator == null || troopIndicators.ContainsKey(troop))
                return;

            // The ring is an authored prefab instanced under the troop, not geometry built here.
            GameObject indicator = Object.Instantiate(troopSelectionIndicator, troop.transform);
            indicator.transform.localPosition = Vector3.zero;
            indicator.transform.localRotation = Quaternion.identity;
            troopIndicators[troop] = indicator;
        }

        public void Remove(GameCharacter troop) {
            if (troop == null)
                return;
            troops.Remove(troop);
            ClearTroopIndicator(troop);
        }

        public void Clear() {
            foreach (WV_Unit unit in units) {
                if (unit != null)
                    unit.SelectedLocally = false;
            }
            units.Clear();

            foreach (GameCharacter troop in troops) {
                if (troop != null)
                    ClearTroopIndicator(troop);
            }
            troops.Clear();
        }

        /// <summary>Drops units and troops that despawned or died while selected. True when anything was dropped.</summary>
        public bool Prune() {
            int before = Count;
            for (int i = units.Count - 1; i >= 0; i--) {
                WV_Unit unit = units[i];
                if (unit == null || unit.IsDead || !unit.IsSpawned)
                    units.RemoveAt(i);
            }

            for (int i = troops.Count - 1; i >= 0; i--) {
                GameCharacter troop = troops[i];
                if (troop == null || troop.IsDead || !troop.IsSpawned) {
                    if (troop != null)
                        ClearTroopIndicator(troop);
                    troops.RemoveAt(i);
                }
            }
            return Count != before;
        }

        void ClearTroopIndicator(GameCharacter troop) {
            if (troopRanges.TryGetValue(troop, out WV_RangeIndicator range))
                range.SetSelected(false);
            if (!troopIndicators.TryGetValue(troop, out GameObject indicator))
                return;
            troopIndicators.Remove(troop);
            if (indicator != null)
                Object.Destroy(indicator);
        }

        // --- Bulk selection -----------------------------------------------------

        /// <summary>Adds every unit and/or troop this commander owns, up to the selection cap.</summary>
        public void AddAllOwned(bool includeUnits, bool includeTroops) {
            if (includeUnits) {
                WV_Unit.CollectOwnedBy(localClientId(), ownedScratch);
                foreach (WV_Unit unit in ownedScratch) {
                    if (IsFull)
                        break;
                    Add(unit);
                }
            }

            if (includeTroops) {
                foreach (GameCharacter character in characters) {
                    if (IsFull)
                        break;
                    if (IsCommandableTroop(character))
                        Add(character);
                }
            }
        }

        /// <summary>Adds every owned unit and commandable troop whose drawn body touches <paramref name="box"/>.</summary>
        public void AddInBox(Camera camera, Rect box) {
            WV_Unit.CollectOwnedBy(localClientId(), ownedScratch);
            foreach (WV_Unit unit in ownedScratch) {
                if (IsFull)
                    break;
                if (WV_ScreenPicking.TouchesBox(camera, unit, box))
                    Add(unit);
            }

            foreach (GameCharacter character in characters) {
                if (IsFull)
                    break;
                if (IsCommandableTroop(character) && WV_ScreenPicking.TouchesBox(camera, character, box))
                    Add(character);
            }
        }

        /// <summary>
        /// Selects every vehicle of this kind the commander owns. Deliberately not limited to what
        /// is on screen: the point of the gesture is to grab the whole armoured push, and a tank
        /// that happened to be behind the camera is still part of it.
        /// </summary>
        public void AddAllOfKind(WV_UnitKind kind) {
            WV_Unit.CollectOwnedBy(localClientId(), ownedScratch);
            foreach (WV_Unit candidate in ownedScratch) {
                if (IsFull)
                    break;
                if (candidate != null && candidate.Kind == kind)
                    Add(candidate);
            }
        }

        /// <summary>
        /// Selects every troop of this loadout. A troop's kind is not replicated as a field, so its
        /// display name (which the server sets from that kind) is what identifies it, the same
        /// string the selection summary already groups by.
        /// </summary>
        public void AddAllTroopsNamed(string displayName) {
            foreach (GameCharacter candidate in characters) {
                if (IsFull)
                    break;
                if (IsCommandableTroop(candidate) && candidate.DisplayName == displayName)
                    Add(candidate);
            }
        }

        // --- Reading ------------------------------------------------------------

        /// <summary>Network ids of every living, spawned member, for an order or a sale.</summary>
        public void CollectObjectIds(List<int> results) {
            results.Clear();
            foreach (WV_Unit unit in units) {
                if (unit != null && unit.IsSpawned && !unit.IsDead)
                    results.Add(unit.NetworkObject.ObjectId);
            }
            foreach (GameCharacter troop in troops) {
                if (troop != null && troop.IsSpawned && !troop.IsDead)
                    results.Add(troop.NetworkObject.ObjectId);
            }
        }

        /// <summary>The team of the first living member, which decides who the selection may attack. Null when empty.</summary>
        public TeamConfig GetCommandingTeam() {
            foreach (WV_Unit unit in units) {
                if (unit != null)
                    return unit.Team;
            }
            foreach (GameCharacter troop in troops) {
                if (troop != null)
                    return troop.Team;
            }
            return null;
        }

        /// <summary>
        /// The refund the server will pay for the current selection, by the same rule: part of what
        /// each unit or troop cost, less for a damaged one.
        /// </summary>
        public long EstimateSellRefund() {
            long refund = 0;
            foreach (WV_Unit unit in units) {
                if (unit != null && !unit.IsDead)
                    refund += WV_Rules.GetSellRefund(unit.Cost, GetCondition(unit));
            }
            foreach (GameCharacter troop in troops) {
                if (troop != null && !troop.IsDead && WV_Rules.TryGetTroopKind(troop.DisplayName, out WV_TroopKind kind))
                    refund += WV_Rules.GetSellRefund(WV_Rules.GetTroopCost(kind), GetCondition(troop));
            }
            return refund;
        }

        static float GetCondition(IEntity entity) {
            long max = entity.MaxHealth.Value;
            return max > 0 ? Mathf.Clamp01(entity.Health.Value / (float)max) : 1f;
        }

        /// <summary>Identifies exactly what is selected, cheaply enough to compare on every change.</summary>
        public int Signature() {
            unchecked {
                int hash = 17;
                foreach (WV_Unit unit in units)
                    hash = hash * 31 + (unit != null ? unit.GetEntityId().GetHashCode() : 0);
                foreach (GameCharacter troop in troops)
                    hash = hash * 31 + (troop != null ? troop.GetEntityId().GetHashCode() : 0);
                return hash;
            }
        }
    }
}
