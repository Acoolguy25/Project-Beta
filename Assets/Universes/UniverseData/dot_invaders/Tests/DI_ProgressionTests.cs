using System.Collections.Generic;
using NUnit.Framework;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    /// <summary>The generated boards: every layout, many seeds.</summary>
    public class DI_BoardTests {
        const int SeedsPerBoard = 40;
        // Player teams a board must still have a start for once the bots have spawned.
        const int PlayerStarts = 4;
        const float SpacingSlack = 0.01f;

        static IEnumerable<string> BoardIds() {
            foreach (DI_BoardLayout layout in DI_BoardLayouts.All)
                yield return layout.id;
        }

        [Test]
        public void BoardIdsAreUniqueAndFindable() {
            var ids = new HashSet<string>();
            foreach (DI_BoardLayout layout in DI_BoardLayouts.All) {
                Assert.That(ids.Add(layout.id), Is.True, $"Duplicate board id {layout.id}.");
                Assert.That(DI_BoardLayouts.Find(layout.id), Is.SameAs(layout));
                Assert.That(string.IsNullOrEmpty(layout.title), Is.False);
                Assert.That(layout.baseCount, Is.InRange(layout.SpecialCount + layout.npcTeams + PlayerStarts, DI_Arena.MaxBases));
            }
            Assert.That(DI_BoardLayouts.All.Count, Is.GreaterThanOrEqualTo(5), "The plan asks for several boards.");
            Assert.That(DI_BoardLayouts.Find("nope"), Is.Null);
        }

        [TestCaseSource(nameof(BoardIds))]
        public void EveryBoardIsConnectedInsideTheArenaAndSpacedOut(string id) {
            DI_BoardLayout layout = DI_BoardLayouts.Find(id);
            for (int seed = 0; seed < SeedsPerBoard; seed++) {
                DI_BoardPlan plan = DI_BoardGenerator.Generate(layout, seed);
                Assert.That(plan.Count, Is.EqualTo(layout.baseCount), $"{id} seed {seed}: base count");
                Assert.That(plan.kinds.Length, Is.EqualTo(plan.Count));
                Assert.That(plan.spawnable.Length, Is.EqualTo(plan.Count));
                for (int i = 0; i < plan.Count; i++) {
                    Assert.That(DI_Arena.Contains(plan.positions[i]), Is.True, $"{id} seed {seed}: base {i} outside the arena");
                    for (int j = i + 1; j < plan.Count; j++)
                        Assert.That((plan.positions[i] - plan.positions[j]).magnitude, Is.GreaterThanOrEqualTo(layout.minSpacing - SpacingSlack),
                            $"{id} seed {seed}: bases {i} and {j} overlap");
                }
                Assert.That(Reachable(plan), Is.EqualTo(plan.Count), $"{id} seed {seed}: the board is split");
            }
        }

        [TestCaseSource(nameof(BoardIds))]
        public void RoadsAreUniqueAndNeverRunThroughABase(string id) {
            DI_BoardLayout layout = DI_BoardLayouts.Find(id);
            for (int seed = 0; seed < SeedsPerBoard; seed++) {
                DI_BoardPlan plan = DI_BoardGenerator.Generate(layout, seed);
                var seen = new HashSet<Vector2Int>();
                foreach (Vector2Int link in plan.links) {
                    Assert.That(link.x, Is.LessThan(link.y), $"{id} seed {seed}: road {link} is not (low, high)");
                    Assert.That(link.y, Is.LessThan(plan.Count));
                    Assert.That(seen.Add(link), Is.True, $"{id} seed {seed}: road {link} twice");
                    Assert.That(DI_BoardGenerator.PassesThroughBase(plan.positions, link.x, link.y), Is.False,
                        $"{id} seed {seed}: road {link} runs through a base");
                }
            }
        }

        [TestCaseSource(nameof(BoardIds))]
        public void EveryBoardHasItsSpecialBasesAndEnoughStarts(string id) {
            DI_BoardLayout layout = DI_BoardLayouts.Find(id);
            for (int seed = 0; seed < SeedsPerBoard; seed++) {
                DI_BoardPlan plan = DI_BoardGenerator.Generate(layout, seed);
                var counts = new Dictionary<DI_BaseKind, int>();
                int starts = 0;
                for (int i = 0; i < plan.Count; i++) {
                    counts.TryGetValue(plan.kinds[i], out int count);
                    counts[plan.kinds[i]] = count + 1;
                    if (plan.kinds[i] == DI_BaseKind.Plain && plan.spawnable[i])
                        starts++;
                }
                Assert.That(Count(counts, DI_BaseKind.Turret), Is.EqualTo(layout.turrets), $"{id} seed {seed}: turrets");
                Assert.That(Count(counts, DI_BaseKind.Super), Is.EqualTo(layout.superBases), $"{id} seed {seed}: super bases");
                Assert.That(Count(counts, DI_BaseKind.Speed), Is.EqualTo(layout.speedBases), $"{id} seed {seed}: speed bases");
                Assert.That(Count(counts, DI_BaseKind.Factory), Is.EqualTo(layout.factories), $"{id} seed {seed}: factories");
                Assert.That(Count(counts, DI_BaseKind.Fortress), Is.EqualTo(layout.fortresses), $"{id} seed {seed}: fortresses");
                Assert.That(starts, Is.GreaterThanOrEqualTo(layout.npcTeams + PlayerStarts), $"{id} seed {seed}: start bases");
            }
        }

        [Test]
        public void TheHoneycombFillsTheArenaWithShortRoads() {
            DI_BoardLayout layout = DI_BoardLayouts.Find("honeycomb");
            for (int seed = 0; seed < SeedsPerBoard; seed++) {
                DI_BoardPlan plan = DI_BoardGenerator.Generate(layout, seed);
                var degree = new int[plan.Count];
                foreach (Vector2Int link in plan.links) {
                    degree[link.x]++;
                    degree[link.y]++;
                }
                float average = 0f;
                foreach (int roads in degree) average += roads;
                average /= plan.Count;
                // The old grid dropped a third of its cells at random and averaged about 3.4 roads.
                Assert.That(average, Is.GreaterThanOrEqualTo(4f), $"seed {seed}: average roads per base");

                // Spread over the whole arena: every quarter of the board holds bases.
                var quarters = new int[4];
                foreach (Vector2 position in plan.positions)
                    quarters[(position.x < 0f ? 0 : 1) + (position.y < 0f ? 0 : 2)]++;
                foreach (int count in quarters)
                    Assert.That(count, Is.GreaterThanOrEqualTo(plan.Count / 6), $"seed {seed}: a quarter of the board is nearly empty");
            }
        }

        [Test]
        public void VoteOptionsFindTheirBoardByTitle() {
            foreach (DI_BoardLayout layout in DI_BoardLayouts.All) {
                Assert.That(DI_BoardLayouts.FindByTitle(layout.title), Is.SameAs(layout));
                Assert.That(layout.Describe(), Does.StartWith(layout.tagline).And.Contain(layout.Stats));
            }
            Assert.That(DI_BoardLayouts.FindByTitle("Nowhere"), Is.Null);
            Assert.That(DI_BoardLayouts.FindByTitle(null), Is.Null);
        }

        [Test]
        public void TheSameSeedGivesTheSameBoard() {
            foreach (DI_BoardLayout layout in DI_BoardLayouts.All) {
                DI_BoardPlan a = DI_BoardGenerator.Generate(layout, 1234), b = DI_BoardGenerator.Generate(layout, 1234);
                Assert.That(b.positions, Is.EqualTo(a.positions), layout.id);
                Assert.That(b.links, Is.EqualTo(a.links), layout.id);
                Assert.That(b.kinds, Is.EqualTo(a.kinds), layout.id);
            }
        }

        [Test]
        public void TheVoteNeverOffersTheBoardJustPlayed() {
            var random = new System.Random(5);
            for (int last = 0; last < DI_BoardLayouts.All.Count; last++) {
                int[] options = DI_BoardLayouts.VoteOptions(3, last, random);
                Assert.That(options.Length, Is.EqualTo(3));
                Assert.That(options, Has.No.Member(last));
                Assert.That(options, Is.Unique);
            }
            Assert.That(DI_BoardLayouts.VoteOptions(99, 0, random).Length, Is.EqualTo(DI_BoardLayouts.All.Count - 1));
        }

        static int Count(Dictionary<DI_BaseKind, int> counts, DI_BaseKind kind) => counts.TryGetValue(kind, out int count) ? count : 0;

        static int Reachable(DI_BoardPlan plan) {
            var neighbours = new List<int>[plan.Count];
            for (int i = 0; i < plan.Count; i++) neighbours[i] = new List<int>();
            foreach (Vector2Int link in plan.links) {
                neighbours[link.x].Add(link.y);
                neighbours[link.y].Add(link.x);
            }
            var seen = new bool[plan.Count];
            var open = new Stack<int>();
            open.Push(0);
            seen[0] = true;
            int reached = 1;
            while (open.Count > 0)
                foreach (int next in neighbours[open.Pop()])
                    if (!seen[next]) {
                        seen[next] = true;
                        reached++;
                        open.Push(next);
                    }
            return reached;
        }
    }

    /// <summary>Elo rating, the rank ladder and the NPC tier pick.</summary>
    public class DI_RatingTests {
        static DI_RatingTuning Tuning() => new();

        [Test]
        public void ExpectedScoresOfTwoPlayersAddUpToOne() {
            Assert.That(DI_Rating.Expected(1000, 1000), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(DI_Rating.Expected(1200, 1000) + DI_Rating.Expected(1000, 1200), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(DI_Rating.Expected(1400, 1000), Is.GreaterThan(0.9));
        }

        [Test]
        public void BeatingAnEqualOpponentGainsHalfTheKFactor() {
            DI_RatingTuning tuning = Tuning();
            var win = new[] { new DI_RatedOpponent(1000, 1f) };
            var loss = new[] { new DI_RatedOpponent(1000, 0f) };
            int established = tuning.provisionalMatches;
            Assert.That(DI_Rating.Delta(1000, established, win, tuning), Is.EqualTo(Mathf.RoundToInt(tuning.kFactor / 2f)));
            Assert.That(DI_Rating.Delta(1000, established, loss, tuning), Is.EqualTo(-Mathf.RoundToInt(tuning.kFactor / 2f)));
            // New players move faster.
            Assert.That(DI_Rating.Delta(1000, 0, win, tuning), Is.EqualTo(Mathf.RoundToInt(tuning.provisionalKFactor / 2f)));
        }

        [Test]
        public void RatingNeverDropsBelowTheFloor() {
            DI_RatingTuning tuning = Tuning();
            var losses = new[] { new DI_RatedOpponent(100, 0f), new DI_RatedOpponent(100, 0f) };
            Assert.That(DI_Rating.Apply(tuning.minRating, 0, losses, tuning), Is.EqualTo(tuning.minRating));
        }

        [Test]
        public void AFreeForAllCountsAsOneGameAgainstEachOtherTeam() {
            var results = new List<DI_TeamResult> {
                new(0, long.MaxValue, 1000), // the winner
                new(1, 5, 1000),
                new(100, 0, 1000),           // went out first
            };
            Assert.That(DI_Rating.Placement(0, results), Is.EqualTo(1));
            Assert.That(DI_Rating.Placement(1, results), Is.EqualTo(2));
            Assert.That(DI_Rating.Placement(100, results), Is.EqualTo(3));
            List<DI_RatedOpponent> middle = DI_Rating.Opponents(1, results);
            Assert.That(middle.Count, Is.EqualTo(2));
            Assert.That(middle.Exists(o => o.Score == 0f) && middle.Exists(o => o.Score == 1f), Is.True);
            DI_RatingTuning tuning = Tuning();
            Assert.That(DI_Rating.Delta(1000, tuning.provisionalMatches, middle, tuning), Is.Zero, "One win and one loss against equals.");
            Assert.That(DI_Rating.Delta(1000, tuning.provisionalMatches, DI_Rating.Opponents(0, results), tuning), Is.GreaterThan(0));
        }

        [Test]
        public void TiedTeamsDrawWithEachOther() {
            var results = new List<DI_TeamResult> { new(0, 7, 1000), new(1, 7, 1000) };
            List<DI_RatedOpponent> opponents = DI_Rating.Opponents(0, results);
            Assert.That(opponents[0].Score, Is.EqualTo(0.5f));
        }

        [Test]
        public void TheRankLadderClimbsWithRating() {
            Assert.That(DI_Ranks.Of(0).Name, Is.EqualTo("Iron"));
            Assert.That(DI_Ranks.Of(1000).Name, Does.StartWith("Bronze"));
            int previous = -1;
            foreach (DI_Rank rank in DI_Ranks.All) {
                Assert.That(rank.MinRating, Is.GreaterThan(previous));
                Assert.That(DI_Ranks.Of(rank.MinRating).Index, Is.EqualTo(rank.Index));
                Assert.That(DI_Ranks.Of(rank.MinRating - 1).Index, Is.EqualTo(Mathf.Max(0, rank.Index - 1)));
                previous = rank.MinRating;
            }
            DI_Rank top = DI_Ranks.All[DI_Ranks.All.Count - 1];
            Assert.That(DI_Ranks.TryNext(top.MinRating, out _), Is.False);
            Assert.That(DI_Ranks.TryNext(0, out DI_Rank next), Is.True);
            Assert.That(next.Index, Is.EqualTo(1));
        }

        [Test]
        public void BotsArePickedToMatchThePlayers() {
            DI_NpcTier[] tiers = DI_NpcTiers.CreateDefaults();
            for (int i = 0; i < tiers.Length; i++)
                Assert.That(DI_NpcTiers.Pick(tiers, tiers[i].rating), Is.EqualTo(i));
            Assert.That(DI_NpcTiers.Pick(tiers, 0), Is.EqualTo(0));
            Assert.That(DI_NpcTiers.Pick(tiers, 100000), Is.EqualTo(tiers.Length - 1));
            Assert.That(DI_NpcTiers.Pick(tiers, new DI_RatingTuning().startRating), Is.InRange(0, tiers.Length - 1));
            for (int i = 1; i < tiers.Length; i++) {
                Assert.That(tiers[i].rating, Is.GreaterThan(tiers[i - 1].rating), "Tiers climb in rating.");
                Assert.That(tiers[i].intelligence, Is.GreaterThanOrEqualTo(tiers[i - 1].intelligence), "and in skill.");
            }
        }

        [Test]
        public void OrdinalsReadNaturally() {
            Assert.That(DI_Rating.Ordinal(1), Is.EqualTo("1st"));
            Assert.That(DI_Rating.Ordinal(2), Is.EqualTo("2nd"));
            Assert.That(DI_Rating.Ordinal(3), Is.EqualTo("3rd"));
            Assert.That(DI_Rating.Ordinal(4), Is.EqualTo("4th"));
            Assert.That(DI_Rating.Ordinal(11), Is.EqualTo("11th"));
        }
    }

    /// <summary>Commander perks: unlock levels, slots and what the panel sends.</summary>
    public class DI_PerkTests {
        [Test]
        public void PerksUnlockInOrderWithUniqueIds() {
            var ids = new HashSet<string>();
            int previous = 1;
            foreach (DI_PerkDefinition perk in DI_Perks.All) {
                Assert.That(ids.Add(perk.Id), Is.True, perk.Id);
                Assert.That(perk.UnlockLevel, Is.GreaterThan(previous), $"{perk.Id} unlocks after the one before");
                Assert.That(perk.UnlockLevel, Is.LessThan(new RetentionTuning().levels.maxLevel));
                previous = perk.UnlockLevel;
            }
            Assert.That(DI_Perks.SecondSlotLevel, Is.LessThan(new RetentionTuning().levels.maxLevel));
        }

        [Test]
        public void SlotsComeWithLevels() {
            int first = DI_Perks.All[0].UnlockLevel;
            Assert.That(DI_Perks.SlotsAt(first - 1), Is.Zero);
            Assert.That(DI_Perks.SlotsAt(first), Is.EqualTo(1));
            Assert.That(DI_Perks.SlotsAt(DI_Perks.SecondSlotLevel - 1), Is.EqualTo(1));
            Assert.That(DI_Perks.SlotsAt(DI_Perks.SecondSlotLevel), Is.EqualTo(DI_Perks.MaxSlots));
        }

        [Test]
        public void SanitizeKeepsOnlyUnlockedKnownPerksUpToTheSlots() {
            int level = DI_Perks.Find(DI_Perks.SupplyLines).UnlockLevel;
            List<string> loadout = DI_Perks.Sanitize(new[] { "unknown", DI_Perks.Vanguard, DI_Perks.SupplyLines, DI_Perks.HeadStart }, level);
            Assert.That(loadout, Is.EqualTo(new[] { DI_Perks.SupplyLines }), "Vanguard is locked, one slot.");
            loadout = DI_Perks.Sanitize(new[] { DI_Perks.HeadStart, DI_Perks.HeadStart, DI_Perks.QuickMarch, DI_Perks.Vanguard }, DI_Perks.SecondSlotLevel);
            Assert.That(loadout, Is.EqualTo(new[] { DI_Perks.HeadStart, DI_Perks.QuickMarch }));
            Assert.That(DI_Perks.Sanitize(null, 50), Is.Empty);
        }

        [Test]
        public void ToggleEquipsRemovesAndSwapsTheOldest() {
            int level = DI_Perks.SecondSlotLevel;
            List<string> loadout = DI_Perks.Toggle(new List<string>(), DI_Perks.HeadStart, level);
            loadout = DI_Perks.Toggle(loadout, DI_Perks.QuickMarch, level);
            Assert.That(loadout, Is.EqualTo(new[] { DI_Perks.HeadStart, DI_Perks.QuickMarch }));
            loadout = DI_Perks.Toggle(loadout, DI_Perks.IronWalls, level);
            Assert.That(loadout, Is.EqualTo(new[] { DI_Perks.QuickMarch, DI_Perks.IronWalls }), "Full: the oldest makes room.");
            loadout = DI_Perks.Toggle(loadout, DI_Perks.QuickMarch, level);
            Assert.That(loadout, Is.EqualTo(new[] { DI_Perks.IronWalls }), "Choosing an equipped perk takes it off.");
            Assert.That(DI_Perks.Toggle(loadout, DI_Perks.Vanguard, 1), Is.Empty, "Locked perks cannot be equipped.");
        }

        [Test]
        public void EffectsCombine() {
            DI_PerkEffects none = DI_Perks.EffectsOf(null);
            Assert.That(none.production, Is.EqualTo(1f));
            Assert.That(none.overdrive, Is.False);
            DI_PerkEffects both = DI_Perks.EffectsOf(new[] { DI_Perks.HeadStart, DI_Perks.Overdrive });
            Assert.That(both.startTroops, Is.GreaterThan(0));
            Assert.That(both.overdrive, Is.True);
            Assert.That(both.moveSpeed, Is.EqualTo(1f));
            Assert.That(DI_Perks.EffectsOf(new[] { DI_Perks.IronWalls }).defence, Is.GreaterThan(1f));
            Assert.That(DI_Perks.EffectsOf(new[] { DI_Perks.SiegeBreakers }).siege, Is.GreaterThan(1f));
            Assert.That(DI_Perks.EffectsOf(new[] { DI_Perks.Vanguard }).clash, Is.GreaterThan(1f));
        }

        [Test]
        public void PerkDefenceRaisesTheAttackersAGarrisonNeeds() {
            float walls = DI_Perks.EffectsOf(new[] { DI_Perks.IronWalls }).defence;
            float defence = DI_Rules.DefenceMultiplier(false, DI_Rules.DefaultFortressMultiplier, walls);
            Assert.That(DI_Rules.AttackersToDefeat(4, defence), Is.EqualTo(Mathf.CeilToInt(4 * walls - 0.001f)));
            Assert.That(DI_Rules.AttackersToDefeat(10, 1f), Is.EqualTo(10));
            Assert.That(DI_Rules.DefenceMultiplier(true, 2f, walls), Is.EqualTo(2f * walls).Within(1e-5));
            Assert.That(DI_Rules.ArrivalDamage(1f, defence), Is.EqualTo(1f / walls).Within(1e-5));
        }
    }

    /// <summary>The retention kit adoption: unlock table, challenge pool and the match rewards.</summary>
    public class DI_RetentionRulesTests {
        [Test]
        public void TheRulesBuildWithTheKitDefaults() {
            RetentionRules rules = DI_Retention.Rules(null);
            Assert.That(rules.UniverseId, Is.EqualTo(DI_Retention.Universe));
            Assert.That(rules.ChallengePool.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(rules.ChallengesFor(20000).Count, Is.EqualTo(new RetentionTuning().dailyChallengeCount));
        }

        [Test]
        public void EveryPerkAndTheSecondSlotIsAnUnlock() {
            List<UnlockDefinition> unlocks = DI_Retention.Unlocks();
            Assert.That(unlocks.Count, Is.InRange(6, 10), "The kit asks for 6 to 10 unlocks.");
            foreach (DI_PerkDefinition perk in DI_Perks.All)
                Assert.That(unlocks.Exists(u => u.id == perk.Id && u.level == perk.UnlockLevel), Is.True, perk.Id);
            Assert.That(unlocks.Exists(u => u.id == DI_Perks.SecondSlot && u.level == DI_Perks.SecondSlotLevel), Is.True);
        }

        [Test]
        public void ChallengesHaveUniqueIdsAndCountKnownStats() {
            var known = new HashSet<string> {
                DI_Retention.Stats.Captures, DI_Retention.Stats.SpecialCaptures, DI_Retention.Stats.FactoryCaptures,
                DI_Retention.Stats.FortressCaptures, DI_Retention.Stats.MostHeld, DI_Retention.Stats.Wins,
                DI_Retention.Stats.Matches, DI_Retention.Stats.Podiums, DI_Retention.Stats.Eliminations,
                DI_Retention.Stats.TroopsSent, DI_Retention.Stats.TroopsDefeated, DI_Retention.Stats.Bounties,
            };
            var ids = new HashSet<string>();
            foreach (ChallengeDefinition challenge in DI_Retention.ChallengePool()) {
                Assert.That(ids.Add(challenge.id), Is.True, challenge.id);
                Assert.That(known, Does.Contain(challenge.statKey), challenge.id);
                Assert.That(challenge.target, Is.GreaterThan(0));
                Assert.That(challenge.Describe(), Does.Not.Contain(ChallengeDefinition.TargetToken));
            }
        }

        [Test]
        public void AWinPaysVictoryAndTheTeamsOutlasted() {
            var rewards = new DI_MatchRewards();
            var result = new DI_MatchResult { won = true, placement = 1, teamCount = 5, captures = 12, eliminations = 2 };
            List<RewardLine> lines = rewards.Lines(result);
            Assert.That(lines.Exists(l => l.Label == "Victory" && l.Xp == rewards.victoryXp && l.Gold == rewards.victoryGold), Is.True);
            Assert.That(lines.Exists(l => l.Label == "Teams outlasted" && l.Xp == 4 * rewards.teamBeatenXp), Is.True);
            Assert.That(lines.Exists(l => l.Label == "Teams eliminated" && l.Xp == 2 * rewards.eliminationXp), Is.True);
            Assert.That(DI_MatchRewards.Title(result, false), Is.EqualTo("Victory!"));
        }

        [Test]
        public void CapturesPayUpToTheCap() {
            var rewards = new DI_MatchRewards();
            var result = new DI_MatchResult { placement = 3, teamCount = 5, captures = 500 };
            List<RewardLine> lines = rewards.Lines(result);
            RewardLine captures = lines.Find(l => l.Label == "Bases captured");
            Assert.That(captures.Xp, Is.EqualTo(rewards.captureXpCap));
            Assert.That(captures.Count, Is.EqualTo(500));
            Assert.That(lines.Exists(l => l.Label == "Finished 3rd of 5" && l.Xp == 2 * rewards.teamBeatenXp), Is.True);
            Assert.That(lines.Exists(l => l.Label == "Victory"), Is.False);
            Assert.That(DI_MatchRewards.Title(result, false), Is.EqualTo("3rd of 5"));
        }

        [Test]
        public void ANewRankPaysOnce() {
            var rewards = new DI_MatchRewards();
            List<RewardLine> lines = rewards.Lines(new DI_MatchResult { placement = 2, teamCount = 3, newRank = true, rankName = "Silver I" });
            Assert.That(lines.FindAll(l => l.Label == "New rank: Silver I").Count, Is.EqualTo(1));
            Assert.That(lines.TrueForAll(l => !l.IsEmpty), Is.True);
        }
    }
}
