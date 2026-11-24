using System.Collections.Generic;
using System.Text;

namespace Universes.UniverseData.tower_defense.Tests {
    /// <summary>
    /// Plays whole games on <see cref="TD_Simulation"/> with a fixed build queue and only the cash the game
    /// pays (no early calls), the way the try-3 natural-cash bot played the real server. Each build phase
    /// it buys the next queue entries it can afford, in order (an entry on a pad that already has a
    /// tower upgrades it). Used by the balance tests; <see cref="Report"/> is a readable wave table.
    /// </summary>
    public sealed class TD_BalanceBot {
        public struct Step {
            public int slot;
            public TD_TowerKind kind;
            public Step(int slot, TD_TowerKind kind) { this.slot = slot; this.kind = kind; }
        }

        public sealed class Result {
            public bool won;
            /// <summary>Wave the base fell on (0 when it held).</summary>
            public int lostOnWave;
            /// <summary>Last wave cleared.</summary>
            public int wavesCleared;
            public int baseHealth;
            /// <summary>First wave in which an enemy reached the base (0 = none).</summary>
            public int firstLeakWave;
            public readonly List<int> baseAfterWave = new();
            public readonly StringBuilder log = new();
            public string Report => log.ToString();
        }

        /// <summary>The try-3 natural-cash queue: Gunners first, then Cannons, Frost and Snipers, every tower to level 3.</summary>
        public static readonly Step[] Balanced = Queue(
            (8, "G"), (6, "G"), (9, "G"), (5, "C"), (8, "G"), (12, "F"), (13, "S"), (6, "G"), (9, "G"), (7, "C"), (5, "C"), (2, "G"),
            (13, "S"), (8, "G"), (6, "G"), (9, "G"), (10, "C"), (4, "S"), (7, "C"), (12, "F"), (16, "S"), (10, "C"), (2, "G"), (4, "S"),
            (5, "C"), (13, "S"), (12, "F"), (7, "C"), (10, "C"), (2, "G"), (4, "S"), (16, "S"), (16, "S"), (11, "G"), (11, "G"), (11, "G"),
            (14, "C"), (14, "C"), (14, "C"), (0, "F"), (0, "F"), (0, "F"), (17, "S"), (17, "S"), (17, "S"), (1, "G"), (1, "G"), (1, "G"),
            (15, "C"), (15, "C"), (15, "C"), (3, "S"), (3, "S"), (3, "S"));

        /// <summary>A level-6+ player's queue: the balanced one with Tesla Coils and Mortars in the mix.</summary>
        public static readonly Step[] Unlocked = Queue(
            (8, "G"), (6, "G"), (9, "G"), (5, "C"), (8, "G"), (12, "F"), (13, "S"), (6, "G"), (9, "G"), (7, "M"), (5, "C"), (2, "T"),
            (13, "S"), (8, "G"), (6, "G"), (9, "G"), (10, "C"), (4, "S"), (7, "M"), (12, "F"), (16, "M"), (10, "C"), (2, "T"), (4, "S"),
            (5, "C"), (13, "S"), (12, "F"), (7, "M"), (10, "C"), (2, "T"), (4, "S"), (16, "M"), (16, "M"), (11, "T"), (11, "T"), (11, "T"),
            (14, "C"), (14, "C"), (14, "C"), (0, "F"), (0, "F"), (0, "F"), (17, "S"), (17, "S"), (17, "S"), (1, "G"), (1, "G"), (1, "G"),
            (15, "M"), (15, "M"), (15, "M"), (3, "S"), (3, "S"), (3, "S"));

        /// <summary>
        /// The greedy build: Gunners only (the cheapest tower, and it hits air), three pads at a time in the
        /// order a player reaches them, each group taken to level 3 before the next.
        /// </summary>
        public static Step[] GreedyGunners {
            get {
                int[] order = { 8, 6, 9, 5, 12, 13, 2, 7, 10, 4, 16, 11, 14, 0, 17, 1, 15, 3 };
                const int group = 3;
                var steps = new List<Step>();
                for (int start = 0; start < order.Length; start += group)
                    for (int level = 1; level <= TD_Rules.MaxLevel; level++)
                        for (int i = start; i < start + group && i < order.Length; i++) steps.Add(new Step(order[i], TD_TowerKind.Gunner));
                return steps.ToArray();
            }
        }

        bool Threatened(TD_Simulation sim) {
            float line = TD_Rules.LaneTrack.Length * abilityAt;
            foreach (var e in sim.Enemies)
                if (e.distance >= line) return true;
            return false;
        }

        static Step[] Queue(params (int slot, string kind)[] entries) {
            var steps = new Step[entries.Length];
            for (int i = 0; i < entries.Length; i++) {
                var kind = entries[i].kind switch {
                    "G" => TD_TowerKind.Gunner, "C" => TD_TowerKind.Cannon, "F" => TD_TowerKind.Frost,
                    "S" => TD_TowerKind.Sniper, "T" => TD_TowerKind.Tesla, _ => TD_TowerKind.Mortar,
                };
                steps[i] = new Step(entries[i].slot, kind);
            }
            return steps;
        }

        /// <summary>Simulation step in seconds (the server ticks at its frame rate; 0.05 s is close enough).</summary>
        public float tick = 0.05f;
        /// <summary>Call Airstrike (and EMP when its cooldown allows) once an enemy gets this far along the road (0..1); 0 = never.</summary>
        public float abilityAt;
        /// <summary>Players calling abilities, each with their own cooldowns (the server's rule); each next one calls a moment after the one before.</summary>
        public int casters = 1;
        /// <summary>Simulation seconds between two casters' calls in the same wave.</summary>
        public float casterGap = 1.5f;

        /// <summary>Plays one game of <paramref name="mode"/> (up to <paramref name="maxWaves"/> waves in Endless) under the day's <paramref name="modifier"/>.</summary>
        public Result Play(Step[] queue, TD_Difficulty mode, int maxWaves = TD_Rules.WaveCount, TD_Modifier modifier = TD_Modifier.None) {
            var sim = new TD_Simulation(mode, modifier);
            var result = new Result();
            int step = 0;
            var lastEmp = new int[System.Math.Max(1, casters)];
            var struck = new bool[lastEmp.Length];
            int waves = sim.Endless ? maxWaves : sim.WaveCount;
            result.log.AppendLine($"mode {mode}: wave | base after | cash after bonus | bought before it");
            for (int wave = 1; wave <= waves; wave++) {
                var bought = new StringBuilder();
                while (step < queue.Length) {
                    var s = queue[step];
                    var outcome = sim.Build(s.slot, s.kind, 0, TD_TowerPerks.None, out int cost);
                    if (outcome == TD_BuildResult.NoCash) break;
                    if (outcome is TD_BuildResult.Built or TD_BuildResult.Upgraded) bought.Append($"{s.kind}@{s.slot}L{sim.Towers[s.slot].level} ");
                    step++;
                }
                sim.StartWave(wave);
                int before = sim.BaseHealth;
                System.Array.Clear(struck, 0, struck.Length);
                float nextCall = 0f;
                while (!sim.WaveCleared && !sim.Defeated) {
                    sim.Tick(tick);
                    sim.Events.Clear();
                    if (abilityAt <= 0f || sim.Time < nextCall || !Threatened(sim)) continue;
                    for (int c = 0; c < struck.Length; c++) {
                        if (struck[c]) continue;
                        struck[c] = sim.Airstrike(c, out _);
                        if (TD_Abilities.Ready(TD_Ability.Emp, lastEmp[c], wave) && sim.Emp(c) > 0) lastEmp[c] = wave;
                        nextCall = sim.Time + casterGap;
                        break;
                    }
                }
                if (sim.BaseHealth < before && result.firstLeakWave == 0) result.firstLeakWave = wave;
                if (sim.Defeated) {
                    result.lostOnWave = wave;
                    result.log.AppendLine($"{wave} | FELL | {sim.Cash} | {bought}");
                    break;
                }
                sim.PayWaveBonus();
                result.wavesCleared = wave;
                result.baseAfterWave.Add(sim.BaseHealth);
                result.log.AppendLine($"{wave} | {sim.BaseHealth} | {sim.Cash} | {bought}");
            }
            result.baseHealth = sim.BaseHealth;
            result.won = !sim.Defeated && !sim.Endless && result.wavesCleared >= sim.WaveCount;
            return result;
        }
    }
}
