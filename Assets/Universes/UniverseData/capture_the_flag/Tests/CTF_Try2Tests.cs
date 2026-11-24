using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RyanAssets.Retention;

namespace Universes.UniverseData.capture_the_flag.Tests {
    /// <summary>Try 2: guns that differ by range, the full board, the kill feed's gun, sudden death, the summary card, standing goals and grab pay.</summary>
    public sealed class CTF_Try2Tests {
        const int Health = 200;
        static readonly float[] Ranges = { 8f, 20f, 40f, 70f, 110f };

        static float Ttk(CTF_Weapon weapon, float distance) => CTF_Loadouts.TimeToKill(CTF_Loadouts.Get(weapon).stats, Health, distance);

        // ---------------------------------------------------------------- time to kill (S8)
        [Test]
        public void TimeToKillTableGivesEveryGunARange() {
            var sb = new StringBuilder("Expected time to kill a 200-health target (1 m hit radius), seconds; '-' = out of range:\n");
            sb.Append("gun".PadRight(16));
            foreach (float r in Ranges) sb.Append($"{r,6:0} m");
            foreach (var gun in CTF_Loadouts.Weapons) {
                sb.Append('\n').Append(gun.title.PadRight(16));
                foreach (float r in Ranges) {
                    float t = Ttk(gun.weapon, r);
                    sb.Append(float.IsInfinity(t) ? "      - " : $"{t,7:0.00} ");
                }
            }
            Console.WriteLine(sb.ToString());
            // Close in, the scattergun wins; the marksman wins at range; the burst rifle owns the middle.
            foreach (var other in new[] { CTF_Weapon.Sidearm, CTF_Weapon.BurstRifle, CTF_Weapon.Marksman })
                Assert.That(Ttk(CTF_Weapon.Scattergun, 8f), Is.LessThan(Ttk(other, 8f)), $"Scattergun beats {other} at 8 m");
            foreach (float r in new[] { 40f, 70f })
                Assert.That(Ttk(CTF_Weapon.Marksman, r), Is.LessThan(Ttk(CTF_Weapon.Sidearm, r)), $"Marksman beats the sidearm at {r} m (try 1: slower everywhere)");
            Assert.That(Ttk(CTF_Weapon.Marksman, 110f), Is.LessThan(float.PositiveInfinity), "the marksman reaches across the arena");
            Assert.That(Ttk(CTF_Weapon.Sidearm, 110f), Is.EqualTo(float.PositiveInfinity));
            Assert.That(Ttk(CTF_Weapon.Scattergun, 40f), Is.EqualTo(float.PositiveInfinity), "the scattergun is a close gun");
        }

        [Test]
        public void HitChanceFallsWithDistanceAndIsOneForADeadOnGun() {
            var marksman = CTF_Loadouts.Get(CTF_Weapon.Marksman).stats;
            var scatter = CTF_Loadouts.Get(CTF_Weapon.Scattergun).stats;
            Assert.That(CTF_Loadouts.HitChance(scatter, 5f, 1f), Is.GreaterThan(CTF_Loadouts.HitChance(scatter, 20f, 1f)));
            Assert.That(CTF_Loadouts.HitChance(marksman, 60f, 1f), Is.GreaterThan(0.9f));
            Assert.That(CTF_Loadouts.HitChance(scatter, 30f, 1f), Is.EqualTo(0f), "past its range");
        }

        // ---------------------------------------------------------------- board (M5)
        static CTF_BotView View(int id, string name, CTF_Team team, int captures, int kills, int deaths, bool alive = true) => new() {
            actorId = id, objectId = id + 100, name = name, team = team, captures = captures, kills = kills, deaths = deaths, alive = alive,
        };

        [Test]
        public void TheBoardListsBotsAndPlayersBestFirst() {
            var bots = new[] {
                View(1, "Rivet", CTF_Team.Red, 0, 5, 2), View(2, "Bolt", CTF_Team.Red, 1, 1, 4),
                View(3, "Cog", CTF_Team.Blue, 0, 2, 2), View(4, "Gear", CTF_Team.Red, 0, 5, 1, alive: false),
            };
            var players = new[] { View(10, "Sam", CTF_Team.Red, 0, 2, 0) };
            var rows = new List<CTF_BotView>();
            CTF_Board.Rows(bots, players, CTF_Team.Red, rows);
            Assert.That(rows.Select(r => r.name), Is.EqualTo(new[] { "Bolt", "Gear", "Rivet", "Sam" }), "captures, then eliminations, then fewest deaths");
            string text = CTF_Board.Lines(rows, 10, 3, "#FFD84D", new StringBuilder());
            Console.WriteLine(text);
            Assert.That(text, Does.Contain("Flags").And.Contain("Elims").And.Contain("Deaths"));
            Assert.That(text, Does.Contain("+1 more"), "rows past the cap fold into a count");
            Assert.That(text, Does.Contain("<color=#8A93A3>Gear"), "a downed bot is dimmed");
            CTF_Board.Rows(bots, players, CTF_Team.Red, rows);
            Assert.That(CTF_Board.Lines(rows, 10, 6, "#FFD84D", new StringBuilder()), Does.Contain("<b><color=#FFD84D>You"), "your own row is yours");
            CTF_Board.Rows(bots, players, CTF_Team.Blue, rows);
            Assert.That(rows.Count, Is.EqualTo(1));
        }

        [Test]
        public void NamesAreClippedAndCannotInjectTags() {
            Assert.That(CTF_Board.Clip("<color=red>Evil</color>", 40), Does.Not.Contain("<"));
            Assert.That(CTF_Board.Clip("AVeryLongPlayerNameIndeed", 10), Has.Length.EqualTo(10).And.EndsWith("…"));
            Assert.That(CTF_Board.Clip(null, 10), Is.EqualTo("?"));
        }

        // ---------------------------------------------------------------- kill feed and notices (M2, S2)
        [Test]
        public void AGunKillNamesTheGun() {
            Assert.That(CTF_Rules.KillVerb(CTF_KillCause.Gun, "Scattergun"), Is.EqualTo("[Scattergun]"));
            Assert.That(CTF_Rules.KillVerb(CTF_KillCause.Melee, "Scattergun"), Is.EqualTo("eliminated"));
            Assert.That(CTF_Rules.KillVerb(CTF_KillCause.Gun, null), Is.EqualTo("eliminated"));
        }

        [Test]
        public void APickNoticeSaysWhatThePickDoes() {
            var gun = CTF_Loadouts.Get(CTF_Weapon.Marksman);
            string now = CTF_Loadouts.PickNotice(gun.title, gun.description, false, 0.5f);
            Assert.That(now, Is.EqualTo($"Marksman Rifle\n<size=50%>{gun.description}</size>"));
            Assert.That(CTF_Loadouts.PickNotice("Sidearm", "", true, 0.5f), Is.EqualTo("Sidearm  (next life)"));
        }

        // ---------------------------------------------------------------- sudden death (S7)
        [Test]
        public void ATieAtTheBuzzerGoesToSuddenDeathAndTheNextCaptureEndsIt() {
            Assert.That(CTF_Rules.StartsSuddenDeath(1, 1, 0, false, 120), Is.True);
            Assert.That(CTF_Rules.StartsSuddenDeath(2, 1, 0, false, 120), Is.False, "a lead at the buzzer just wins");
            Assert.That(CTF_Rules.StartsSuddenDeath(1, 1, 0, true, 120), Is.False, "only once");
            Assert.That(CTF_Rules.StartsSuddenDeath(1, 1, 0, false, 0), Is.False, "off when tuned to 0");
            Assert.That(CTF_Rules.MatchOver(1, 1, 3, 60, true), Is.False, "still tied in sudden death");
            Assert.That(CTF_Rules.MatchOver(2, 1, 3, 60, true), Is.True, "the next capture wins");
            Assert.That(CTF_Rules.MatchOver(1, 1, 3, 0, true), Is.True, "sudden death runs out: a draw");
            Assert.That(CTF_Rules.MatchOver(3, 1, 3, 100, false), Is.True);
        }

        // ---------------------------------------------------------------- summary card (S9, M3)
        [Test]
        public void TheSummaryLeadsWithTheResultAndNamesTheNextGoalAtTheCap() {
            var pay = CTF_SummaryPay.Defaults;
            var lines = new List<RewardLine>();
            var input = new CTF_SummaryInput { outcome = CTF_Outcome.Won, redScore = 3, blueScore = 1, mvp = true, captures = 2, kills = 6, deaths = 3 };
            CTF_Summary.Lines(input, pay, lines);
            Assert.That(lines[0].Label, Is.EqualTo("Victory"));
            Assert.That(lines[1].Label, Is.EqualTo("MVP"));
            Assert.That(lines.Any(l => l.Label == "Next goal"), Is.False, "below the cap the level bar is the next goal");
            input.nextGoal = "Wins 34/50";
            CTF_Summary.Lines(input, pay, lines);
            Assert.That(lines.Last().Label, Is.EqualTo("Next goal"));
            Assert.That(CTF_Summary.Outcome(true, false, false), Is.EqualTo(CTF_Outcome.LateWin));
            Assert.That(CTF_Summary.Outcome(false, false, true), Is.EqualTo(CTF_Outcome.Draw));
            Assert.That(CTF_Summary.Title(CTF_Outcome.Lost), Is.EqualTo("Defeat"));
            Assert.That(CTF_Summary.ResultXp(CTF_Outcome.LateWin, pay), Is.EqualTo(pay.lossXp));
        }

        // ---------------------------------------------------------------- standing goals (M3)
        [Test]
        public void StandingGoalsNeverRunOut() {
            Assert.That(CTF_Goals.Next(0, CTF_Goals.WinSteps), Is.EqualTo(5));
            Assert.That(CTF_Goals.Next(5, CTF_Goals.WinSteps), Is.EqualTo(10));
            Assert.That(CTF_Goals.Next(1000, CTF_Goals.WinSteps), Is.EqualTo(2000));
            Assert.That(CTF_Goals.Next(2500, CTF_Goals.WinSteps), Is.EqualTo(3000));
            var career = new CTF_CareerBroadcast {
                level = 30, maxLevel = 30, wins = 34, mvps = 2, bestStreak = 7, captures = 48, matches = 80, kills = 400,
                gunKills = new[] { 60, 12, 49, 3 },
            };
            var goals = new List<CTF_Goal>();
            CTF_Goals.All(career, goals);
            Assert.That(goals.Select(g => g.label), Is.EqualTo(new[] { "Wins", "MVPs", "Best streak", "Captures", "Burst Rifle mastery", "Scattergun mastery", "Marksman Rifle mastery" }));
            var nearest = CTF_Goals.Nearest(career, goals);
            Assert.That(nearest.ToString(), Is.EqualTo("Scattergun mastery 49/50"));
            string card = CTF_Goals.CardLines(career, goals, "#FFCC40", new StringBuilder());
            Console.WriteLine(CTF_Goals.CardHeader(career) + "\n" + card);
            Assert.That(card, Does.Contain("\u2713 Sidearm<pos=" + CTF_Goals.CardColumn + ">mastered</color>"));
            Assert.That(CTF_Goals.CardHeader(career), Is.EqualTo("80 matches \u00b7 400 eliminations"));
            career.matches = 1;
            Assert.That(CTF_Goals.CardHeader(career), Is.EqualTo("1 match \u00b7 400 eliminations"));
            Assert.That(CTF_Goals.Mastered(CTF_Retention.MasteryKills), Is.True);
            Assert.That(CTF_Goals.Mastered(CTF_Retention.MasteryKills - 1), Is.False);
        }

        // ---------------------------------------------------------------- grab pay (M3)
        [Test]
        public void AGrabPaysOncePerTripOffTheStand() {
            var pay = new CTF_GrabPay();
            Assert.That(pay.Grab(), Is.True, "the grab off the stand pays");
            Assert.That(pay.Grab(), Is.False, "picked up again after a drop: no pay");
            Assert.That(pay.Grab(), Is.False);
            pay.Home();
            Assert.That(pay.Grab(), Is.True, "home and taken again: pays");
        }
    }
}
