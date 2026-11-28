using System.Collections.Generic;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Tells the local commander what is happening to their base while they look elsewhere: a
    /// building under attack, a building or vehicle lost, and construction finished.
    /// <para>
    /// Everything is read from state every client already has - ownership, replicated health, and
    /// the construction flag - by a cheap periodic sweep, so no new message crosses the wire.
    /// Attack alerts are throttled per building and overall, so a siege reads as one warning rather
    /// than as a wall of text.
    /// </para>
    /// </summary>
    public sealed class WV_BaseAlerts {
        /// <summary>Seconds between sweeps. Alerts are for a human, so a quarter second is plenty.</summary>
        const float SweepInterval = 0.25f;

        /// <summary>The same building is not reported under attack again within this many seconds.</summary>
        const float PerTargetAttackCooldown = 12f;

        /// <summary>No two attack alerts of any kind within this many seconds.</summary>
        const float GlobalAttackCooldown = 4f;

        const string AttackColor = "#FF7A5A";
        const string LossColor = "#FF4A3A";
        const string CompleteColor = "#8CE07A";

        sealed class Tracked {
            public EntityBase Entity;
            public WV_Constructable Constructable;
            public string Name;
            public long LastHealth;
            public bool WasOperational;
            public bool IsStructure;
            public float NextAttackAlert;
            public bool Seen;
        }

        readonly Dictionary<WV_Owned, Tracked> tracked = new();
        readonly List<WV_Owned> lost = new();
        readonly System.Action<string> show;
        float nextSweep;
        float nextGlobalAttackAlert;
        int trackedClientId = WV_Owned.NoOwner;

        public WV_BaseAlerts(System.Action<string> show) {
            this.show = show;
        }

        /// <summary>Forgets everything, for a new round or a different commander.</summary>
        public void Reset() {
            tracked.Clear();
            trackedClientId = WV_Owned.NoOwner;
        }

        /// <summary>
        /// True between a round's end and the next round's first wave: a base being knocked about under
        /// the result banner is not news, so no alert is shown (the base is still tracked).
        /// </summary>
        public bool Muted { get; set; }

        public void Tick(int localClientId) {
            float now = Time.unscaledTime;
            if (now < nextSweep)
                return;
            nextSweep = now + SweepInterval;

            if (localClientId != trackedClientId) {
                // The first sweep for a commander is a silent baseline, so joining a round in
                // progress does not announce every building already standing.
                tracked.Clear();
                trackedClientId = localClientId;
                Sweep(localClientId, now, announce: false);
                return;
            }
            Sweep(localClientId, now, announce: true);
        }

        void Sweep(int clientId, float now, bool announce) {
            foreach (Tracked entry in tracked.Values)
                entry.Seen = false;

            if (clientId != WV_Owned.NoOwner) {
                foreach (WV_Owned owned in WV_Owned.All) {
                    if (owned == null || !owned.IsOwnedBy(clientId))
                        continue;
                    if (!tracked.TryGetValue(owned, out Tracked entry)) {
                        entry = Track(owned);
                        if (entry == null)
                            continue;
                        tracked.Add(owned, entry);
                    }
                    entry.Seen = true;
                    if (announce)
                        Inspect(entry, now);
                    else
                        Baseline(entry);
                }
            }

            // Anything no longer listed as ours was destroyed, sold, or handed away. A sale is the
            // player's own doing, so only something that died is reported.
            lost.Clear();
            foreach (KeyValuePair<WV_Owned, Tracked> pair in tracked) {
                if (pair.Value.Seen && pair.Key != null)
                    continue;
                lost.Add(pair.Key);
                Tracked entry = pair.Value;
                bool died = entry.Entity == null || entry.Entity.IsDead || entry.LastHealth <= 0;
                if (announce && died && entry.WasOperational && !Muted)
                    show($"<color={LossColor}>{entry.Name} destroyed!</color>");
            }
            foreach (WV_Owned owned in lost)
                tracked.Remove(owned);
        }

        static Tracked Track(WV_Owned owned) {
            // Only buildings: units are in the fight on purpose and their owner is already watching
            // them, while buildings are what get hit while the commander is looking elsewhere.
            if (!owned.TryGetComponent(out StructureComponent entity))
                return null;
            var constructable = owned.GetComponent<WV_Constructable>();
            var entry = new Tracked {
                Entity = entity,
                Constructable = constructable,
                IsStructure = entity is StructureComponent,
                Name = string.IsNullOrEmpty(entity.DisplayName) ? owned.name : entity.DisplayName
            };
            Baseline(entry);
            return entry;
        }

        static void Baseline(Tracked entry) {
            if (entry.Entity == null)
                return;
            entry.LastHealth = entry.Entity.Health.Value;
            entry.WasOperational = entry.Constructable == null || entry.Constructable.IsOperational;
        }

        void Inspect(Tracked entry, float now) {
            if (entry.Entity == null)
                return;

            bool operational = entry.Constructable == null || entry.Constructable.IsOperational;
            if (operational && !entry.WasOperational)
                show($"<color={CompleteColor}>{entry.Name} complete</color>");
            entry.WasOperational = operational;

            long health = entry.Entity.Health.Value;
            bool hit = health < entry.LastHealth;
            entry.LastHealth = health;
            // Units are in the fight on purpose; their owner is already watching them. Buildings
            // are what get hit while the commander is looking somewhere else.
            if (!hit || Muted || !entry.IsStructure || now < entry.NextAttackAlert || now < nextGlobalAttackAlert)
                return;

            entry.NextAttackAlert = now + PerTargetAttackCooldown;
            nextGlobalAttackAlert = now + GlobalAttackCooldown;
            long max = entry.Entity.MaxHealth.Value;
            int percent = max > 0 ? Mathf.CeilToInt(100f * health / max) : 0;
            show($"<color={AttackColor}>Your {entry.Name} is under attack! ({percent}%)</color>");
        }
    }
}
