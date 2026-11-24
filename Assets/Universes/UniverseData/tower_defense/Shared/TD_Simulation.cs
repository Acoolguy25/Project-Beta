using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>A tower on a pad, as the server simulates it.</summary>
    public sealed class TD_SimTower {
        public byte slot;
        public TD_TowerKind kind;
        public int level = 1;
        public float cooldown;
        /// <summary>The builder's id (a connection's client id on the server); -1 for none.</summary>
        public int owner = -1;
        /// <summary>The builder's perks, fixed when it was built.</summary>
        public TD_TowerPerks perks = TD_TowerPerks.None;
        /// <summary>Cash spent on it so far (build plus upgrades, after discounts).</summary>
        public int spent;

        public int Refund => Mathf.RoundToInt(spent * perks.refundRate);
    }

    /// <summary>An enemy destroyed (or a shield broken) and who did it.</summary>
    public struct TD_Kill {
        public ushort enemy;
        public TD_EnemyKind kind;
        /// <summary>The tower's pad, or -1 for an ability.</summary>
        public int slot;
        /// <summary>The tower's builder or the ability's caster (-1 for none).</summary>
        public int owner;
        public int reward;
    }

    /// <summary>What happened since the events were last cleared: what clients draw and the server counts.</summary>
    public sealed class TD_SimEvents {
        public readonly List<TD_Shot> shots = new();
        /// <summary>Enemies destroyed (by towers or abilities).</summary>
        public readonly List<ushort> killed = new();
        /// <summary>Enemies that reached the base.</summary>
        public readonly List<ushort> leaked = new();
        /// <summary>Enemies a medic healed.</summary>
        public readonly List<ushort> healed = new();
        /// <summary>Who destroyed what, for rewards, stats and challenges.</summary>
        public readonly List<TD_Kill> kills = new();
        /// <summary>Who broke which shield.</summary>
        public readonly List<TD_Kill> shieldBreaks = new();
        /// <summary>Base health lost to leaks.</summary>
        public int baseDamage;
        /// <summary>Cash paid for kills.</summary>
        public int cashEarned;

        public bool Any => shots.Count > 0 || killed.Count > 0 || leaked.Count > 0 || healed.Count > 0;

        public void Clear() {
            shots.Clear(); killed.Clear(); leaked.Clear(); healed.Clear(); kills.Clear(); shieldBreaks.Clear();
            baseDamage = 0;
            cashEarned = 0;
        }
    }

    public enum TD_BuildResult : byte { Built, Upgraded, MaxLevel, NoCash, BadSlot }

    /// <summary>
    /// One game of tower defense without any networking: the purse, the base, the towers on their pads,
    /// the wave's spawn schedule, enemy movement, targeting, damage, shields, medics, carriers and the
    /// players' abilities. The server runner owns one and broadcasts what it does; tests and the balance
    /// bots play whole games with it.
    /// </summary>
    public sealed class TD_Simulation {
        struct Spawn {
            public float time;
            public TD_EnemyKind kind;
        }

        /// <summary>An airstrike called but not landed yet (the jet is on its way).</summary>
        struct PendingStrike {
            public float time, distance;
            public int owner;
            /// <summary>Its share of full damage (<see cref="TD_Abilities.RepeatScale"/>).</summary>
            public float scale;
        }

        readonly List<Spawn> schedule = new();
        readonly List<PendingStrike> strikes = new();
        readonly List<TD_SimEnemy> chainHits = new();
        int nextSpawn, strikesThisWave, empsThisWave;
        float waveStart;
        ushort nextId = 1;

        public readonly List<TD_SimEnemy> Enemies = new();
        /// <summary>The tower on each pad (index = slot), or null.</summary>
        public readonly TD_SimTower[] Towers = new TD_SimTower[TD_Rules.Slots.Length];
        public readonly TD_SimEvents Events = new();

        public TD_Difficulty Mode { get; private set; }
        public TD_ModeStats ModeStats => TD_Modes.Get(Mode);
        /// <summary>The day's twist on this game (<see cref="TD_Daily"/>).</summary>
        public TD_Modifier Modifier { get; private set; }
        public TD_ModifierStats ModifierStats => TD_Daily.Get(Modifier);
        public int Cash { get; set; }
        public int BaseHealth { get; set; }
        /// <summary>The wave being fought or about to be (0 before wave 1).</summary>
        public int Wave { get; private set; }
        /// <summary>Simulation clock in seconds (runs faster under fast forward).</summary>
        public float Time { get; private set; }

        public TD_Simulation(TD_Difficulty mode = TD_Difficulty.Normal, TD_Modifier modifier = TD_Modifier.None) => Reset(mode, modifier);

        /// <summary>A fresh game of <paramref name="mode"/> under <paramref name="modifier"/>: its starting cash, a full base, no towers or enemies.</summary>
        public void Reset(TD_Difficulty mode, TD_Modifier modifier = TD_Modifier.None) {
            Mode = TD_Modes.IsValid(mode) ? mode : TD_Difficulty.Normal;
            Modifier = TD_Daily.IsValid(modifier) ? modifier : TD_Modifier.None;
            Cash = TD_Daily.StartCash(Mode, Modifier);
            BaseHealth = TD_Rules.BaseHealth;
            Wave = 0;
            Time = 0f;
            Enemies.Clear();
            schedule.Clear();
            strikes.Clear();
            nextSpawn = 0;
            System.Array.Clear(Towers, 0, Towers.Length);
            Events.Clear();
        }

        /// <summary>Waves to win (0 in Endless).</summary>
        public int WaveCount => ModeStats.waveCount;
        public bool Endless => ModeStats.Endless;
        public bool Defeated => BaseHealth <= 0;
        public int PendingSpawns => schedule.Count - nextSpawn;
        public int EnemiesLeft => PendingSpawns + Enemies.Count;
        public bool WaveCleared => PendingSpawns == 0 && Enemies.Count == 0;
        /// <summary>The game is won once the last wave of a non-endless mode is cleared.</summary>
        public bool IsLastWave => !Endless && Wave >= WaveCount;

        // ---------------------------------------------------------------- waves

        /// <summary>Starts <paramref name="wave"/>: lays out its spawn schedule from the wave plan.</summary>
        public void StartWave(int wave) {
            Wave = wave;
            schedule.Clear();
            strikes.Clear(); // a strike still in the air when the last wave ended hits nothing
            nextSpawn = 0;
            strikesThisWave = empsThisWave = 0;
            waveStart = Time;
            float t = 0f;
            foreach (var group in TD_Rules.Wave(wave)) {
                t += group.delay;
                for (int i = 0; i < group.count; i++) {
                    schedule.Add(new Spawn { time = t, kind = group.kind });
                    t += group.spacing;
                }
            }
        }

        /// <summary>Pays the end-of-wave bonus for the current wave and returns it.</summary>
        public int PayWaveBonus() {
            int bonus = TD_Daily.WaveBonus(Wave, Mode, Modifier);
            Cash += bonus;
            return bonus;
        }

        /// <summary>Health scale of enemies spawned now.</summary>
        public float HealthScale => TD_Daily.HealthScale(Mathf.Max(1, Wave), Mode, Modifier);

        /// <summary>Puts an enemy on the road (also used by probes and tests).</summary>
        public TD_SimEnemy AddEnemy(TD_EnemyKind kind, float distance = 0f, float healthScale = -1f) {
            var stats = TD_Rules.Enemy(kind);
            float hp = stats.health * (healthScale > 0f ? healthScale : HealthScale);
            var e = new TD_SimEnemy { id = nextId++, kind = kind, distance = distance, health = hp, maxHealth = hp, maxShield = hp * stats.shield };
            e.shield = e.maxShield;
            if (nextId == 0) nextId = 1;
            Enemies.Add(e);
            return e;
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Advances the game <paramref name="dt"/> simulation seconds; what happens lands in <see cref="Events"/>.</summary>
        public void Tick(float dt) {
            if (dt <= 0f || Defeated) return;
            Time += dt;
            while (nextSpawn < schedule.Count && schedule[nextSpawn].time <= Time - waveStart) AddEnemy(schedule[nextSpawn++].kind);
            TD_Battle.Heal(Enemies, dt, Events.healed);
            int leak = TD_Battle.Advance(Enemies, dt, Time, Events.leaked, ModifierStats.speedScale);
            if (leak > 0) {
                BaseHealth = Mathf.Max(0, BaseHealth - leak);
                Events.baseDamage += leak;
            }
            for (int i = strikes.Count - 1; i >= 0; i--) {
                if (strikes[i].time > Time) continue;
                StrikeAt(strikes[i].distance, strikes[i].owner, strikes[i].scale);
                strikes.RemoveAt(i);
            }
            foreach (var tower in Towers)
                if (tower != null) Fire(tower, dt);
        }

        /// <summary>Airstrikes called and still on their way.</summary>
        public int PendingStrikes => strikes.Count;

        static Vector2 Flat(Vector3 p) => new(p.x, p.z);

        bool InRange(TD_SimTower tower, TD_SimEnemy e, float range) {
            if (!TD_Rules.CanHit(tower.kind, e.kind)) return false;
            float d2 = (Flat(TD_Rules.LaneTrack.Evaluate(e.distance)) - Flat(TD_Rules.Slots[tower.slot])).sqrMagnitude;
            float min = TD_Rules.Tower(tower.kind).minRange;
            return d2 <= range * range && d2 >= min * min;
        }

        /// <summary>
        /// Picks a target in range: snipers take the toughest enemy (health plus shield), frost the one not
        /// yet slowed (shields make it immune), everyone else the one furthest along the road.
        /// </summary>
        TD_SimEnemy Target(TD_SimTower tower) {
            float range = TD_Daily.Range(tower.kind, tower.level, Modifier);
            TD_SimEnemy target = null;
            foreach (var e in Enemies) {
                if (!InRange(tower, e, range)) continue;
                if (target == null || Better(tower.kind, e, target)) target = e;
            }
            return target;
        }

        bool Better(TD_TowerKind kind, TD_SimEnemy e, TD_SimEnemy current) {
            switch (kind) {
                case TD_TowerKind.Sniper: {
                    float a = e.health + e.shield, b = current.health + current.shield;
                    if (!Mathf.Approximately(a, b)) return a > b;
                    break;
                }
                case TD_TowerKind.Frost: {
                    bool open = !e.Shielded && Time >= e.slowUntil, currentOpen = !current.Shielded && Time >= current.slowUntil;
                    if (open != currentOpen) return open;
                    break;
                }
            }
            return e.distance > current.distance;
        }

        void Fire(TD_SimTower tower, float dt) {
            tower.cooldown -= dt;
            if (tower.cooldown > 0f) return;
            var target = Target(tower);
            if (target == null) { tower.cooldown = 0f; return; }
            tower.cooldown = 1f / (TD_Daily.FireRate(tower.kind, tower.level, Modifier) * (1f + tower.perks.fireRateBonus));
            var stats = TD_Rules.Tower(tower.kind);
            float raw = TD_Rules.Damage(tower.kind, tower.level) * tower.perks.damageScale;
            int shot = Events.shots.Count;
            Events.shots.Add(new TD_Shot { slot = tower.slot, enemy = target.id });
            if (stats.slow > 0f && !target.Shielded) {
                target.slowUntil = Time + TD_Rules.SlowSeconds;
                target.slowFactor = TD_Rules.SlowFactor(tower.kind, tower.level);
            }
            if (stats.splash > 0f) Splash(tower, target, raw, stats.splash);
            else if (stats.chain > 0) Chain(tower, target, raw, shot);
            else MarkBlocked(shot, Hit(tower.slot, tower.owner, target, raw, TD_Rules.Pierces(tower.kind)));
        }

        /// <summary>Flags shot <paramref name="index"/> as bounced off a shield, so the client can show that it did nothing.</summary>
        void MarkBlocked(int index, TD_HitResult result) {
            if (result != TD_HitResult.Blocked || index < 0 || index >= Events.shots.Count) return;
            var shot = Events.shots[index];
            shot.blocked = true;
            Events.shots[index] = shot;
        }

        void Splash(TD_SimTower tower, TD_SimEnemy target, float raw, float radius) {
            Vector3 centre = TD_Rules.LaneTrack.Evaluate(target.distance);
            for (int i = Enemies.Count - 1; i >= 0; i--) { // backwards: a kill removes the enemy
                if (i >= Enemies.Count) continue;
                var e = Enemies[i];
                if (!e.Flying && (TD_Rules.LaneTrack.Evaluate(e.distance) - centre).sqrMagnitude <= radius * radius)
                    Hit(tower.slot, tower.owner, e, raw * (e == target ? 1f : TD_Rules.SplashFalloff), false);
            }
        }

        /// <summary>Hits the target, then jumps to the nearest enemy not hit yet, losing <see cref="TD_Rules.ChainFalloff"/> each jump.</summary>
        void Chain(TD_SimTower tower, TD_SimEnemy first, float raw, int shot) {
            chainHits.Clear();
            int targets = TD_Rules.ChainTargets(tower.kind, tower.level);
            var current = first;
            for (int jump = 0; jump < targets && current != null; jump++) {
                chainHits.Add(current);
                Vector3 at = TD_Rules.EnemyPosition(current.kind, current.distance);
                MarkBlocked(shot, Hit(tower.slot, tower.owner, current, raw, false));
                raw *= TD_Rules.ChainFalloff;
                if (jump + 1 >= targets) break;
                TD_SimEnemy next = null;
                float best = TD_Rules.ChainRange * TD_Rules.ChainRange;
                foreach (var e in Enemies) {
                    if (chainHits.Contains(e)) continue;
                    float d2 = (TD_Rules.EnemyPosition(e.kind, e.distance) - at).sqrMagnitude;
                    if (d2 <= best) { best = d2; next = e; }
                }
                shot = -1;
                if (next != null) {
                    shot = Events.shots.Count;
                    Events.shots.Add(new TD_Shot { slot = tower.slot, enemy = next.id, from = current.id });
                }
                current = next;
            }
        }

        /// <summary>One hit, its reward, its shield break and, for a carrier, the drones it releases.</summary>
        TD_HitResult Hit(int slot, int owner, TD_SimEnemy e, float raw, bool pierce, bool ignoreBlock = false) {
            float distance = e.distance;
            var result = TD_Battle.Strike(Enemies, e, raw, pierce, Events.killed, ignoreBlock);
            if (result == TD_HitResult.ShieldBroken)
                Events.shieldBreaks.Add(new TD_Kill { enemy = e.id, kind = e.kind, slot = slot, owner = owner });
            if (result != TD_HitResult.Killed) return result;
            int reward = TD_Daily.KillReward(e.kind, Mode, Modifier);
            Cash += reward;
            Events.cashEarned += reward;
            Events.kills.Add(new TD_Kill { enemy = e.id, kind = e.kind, slot = slot, owner = owner, reward = reward });
            int splits = TD_Rules.Enemy(e.kind).splits;
            for (int i = 0; i < splits; i++) AddEnemy(TD_EnemyKind.Drone, Mathf.Max(0f, distance - i * TD_Rules.SplitSpacing));
            return result;
        }

        // ---------------------------------------------------------------- abilities

        /// <summary>
        /// Bombs the enemy furthest along the road and everything within the strike radius of it (ground
        /// and air; bombs ignore the shield bounce rule). False when the road is empty.
        /// </summary>
        public bool Airstrike(int owner, out float distance) => CallAirstrike(owner, 0f, out distance, out _);

        /// <summary>
        /// Calls an airstrike that lands <paramref name="delay"/> simulation seconds from now (the jet's
        /// flight), aimed where the lead enemy will be by then. <paramref name="distance"/> is that point
        /// on the road; <paramref name="inRadius"/> how many enemies are around it now. False (nothing
        /// called) when the road is empty.
        /// </summary>
        public bool CallAirstrike(int owner, float delay, out float distance, out int inRadius) {
            distance = 0f;
            inRadius = 0;
            TD_SimEnemy lead = null;
            foreach (var e in Enemies)
                if (lead == null || e.distance > lead.distance) lead = e;
            if (lead == null) return false;
            delay = Mathf.Max(0f, delay);
            distance = Mathf.Min(TD_Rules.LaneTrack.Length, lead.distance + TD_Daily.Speed(lead.kind, Modifier) * lead.Slow(Time) * delay);
            inRadius = InStrikeRadius(distance);
            float scale = TD_Abilities.RepeatScale(strikesThisWave++);
            if (delay <= 0f) StrikeAt(distance, owner, scale);
            else strikes.Add(new PendingStrike { time = Time + delay, distance = distance, owner = owner, scale = scale });
            return true;
        }

        int InStrikeRadius(float distance) {
            float radius = TD_Abilities.Get(TD_Ability.Airstrike).radius;
            Vector2 centre = Flat(TD_Rules.LaneTrack.Evaluate(distance));
            int count = 0;
            foreach (var e in Enemies)
                if ((Flat(TD_Rules.LaneTrack.Evaluate(e.distance)) - centre).sqrMagnitude <= radius * radius) count++;
            return count;
        }

        /// <summary>Airstrikes called so far this wave (each after the first hits softer, see <see cref="TD_Abilities.RepeatScale"/>).</summary>
        public int StrikesThisWave => strikesThisWave;
        /// <summary>EMPs fired so far this wave (each after the first stuns for less time).</summary>
        public int EmpsThisWave => empsThisWave;

        void StrikeAt(float distance, int owner, float strikeScale) {
            var stats = TD_Abilities.Get(TD_Ability.Airstrike);
            float damage = TD_Abilities.StrikeDamage(Mathf.Max(1, Wave), Mode) * ModifierStats.healthScale * strikeScale;
            Vector2 centre = Flat(TD_Rules.LaneTrack.Evaluate(distance));
            for (int i = Enemies.Count - 1; i >= 0; i--) {
                if (i >= Enemies.Count) continue; // a carrier's drones were added, or several left at once
                var e = Enemies[i];
                if ((Flat(TD_Rules.LaneTrack.Evaluate(e.distance)) - centre).sqrMagnitude <= stats.radius * stats.radius)
                    Hit(-1, owner, e, damage, false, ignoreBlock: true);
            }
        }

        /// <summary>Stuns every enemy on the road and strips every shield. Returns how many it caught.</summary>
        public int Emp(int owner) {
            if (Enemies.Count == 0) return 0;
            float until = Time + TD_Abilities.Get(TD_Ability.Emp).stunSeconds * TD_Abilities.RepeatScale(empsThisWave++);
            foreach (var e in Enemies) {
                e.stunUntil = Mathf.Max(e.stunUntil, until);
                if (!e.Shielded) continue;
                e.shield = 0f;
                Events.shieldBreaks.Add(new TD_Kill { enemy = e.id, kind = e.kind, slot = -1, owner = owner });
            }
            return Enemies.Count;
        }

        // ---------------------------------------------------------------- building

        public static bool IsSlot(int slot) => slot >= 0 && slot < TD_Rules.Slots.Length;

        /// <summary>
        /// Builds <paramref name="kind"/> on an empty pad, or upgrades the tower already there (its own kind;
        /// <paramref name="perks"/>' discount is the buyer's). <paramref name="cost"/> is the price paid or needed.
        /// </summary>
        public TD_BuildResult Build(int slot, TD_TowerKind kind, int owner, TD_TowerPerks perks, out int cost) {
            cost = 0;
            if (!IsSlot(slot) || (int)kind >= TD_Rules.Towers.Length) return TD_BuildResult.BadSlot;
            var tower = Towers[slot];
            if (tower != null) {
                if (tower.level >= TD_Rules.MaxLevel) return TD_BuildResult.MaxLevel;
                cost = TD_Rules.UpgradeCost(tower.kind, tower.level, perks.upgradeDiscount);
                if (Cash < cost) return TD_BuildResult.NoCash;
                Cash -= cost;
                tower.spent += cost;
                tower.level++;
                return TD_BuildResult.Upgraded;
            }
            cost = TD_Rules.Tower(kind).cost;
            if (Cash < cost) return TD_BuildResult.NoCash;
            Cash -= cost;
            Towers[slot] = new TD_SimTower { slot = (byte)slot, kind = kind, owner = owner, perks = perks, spent = cost };
            return TD_BuildResult.Built;
        }

        /// <summary>Sells the tower on <paramref name="slot"/> for its refund; false when the pad is empty.</summary>
        public bool Sell(int slot, out TD_SimTower sold) {
            sold = IsSlot(slot) ? Towers[slot] : null;
            if (sold == null) return false;
            Cash += sold.Refund;
            Towers[slot] = null;
            return true;
        }
    }
}
