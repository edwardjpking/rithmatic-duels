using System.Collections.Generic;
using NUnit.Framework;

namespace RithmaticDuels.Rules.Tests
{
    public class ModifierTests
    {
        private RulesConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = new RulesConfig();
        }

        [Test]
        public void SameSeedGivesSameSequence()
        {
            var a = new SeededRandom(1234);
            var b = new SeededRandom(1234);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextInt(24), b.NextInt(24));
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var a = new SeededRandom(1);
            var b = new SeededRandom(2);
            bool differed = false;
            for (int i = 0; i < 20; i++) differed |= a.NextInt(1000) != b.NextInt(1000);
            Assert.IsTrue(differed);
        }

        [Test]
        public void SeededRandomIsStableAcrossRuntimes()
        {
            // Pinned values: if these change, saved duels and replays would no longer reproduce.
            var random = new SeededRandom(42);
            var values = new int[5];
            for (int i = 0; i < values.Length; i++) values[i] = random.NextInt(24);
            CollectionAssert.AreEqual(PinnedSeed42, values);
        }

        // SplitMix64 from seed 42, reduced to [0, 24). Checked against an independent reference implementation.
        private static readonly int[] PinnedSeed42 = { 13, 19, 18, 12, 10 };

        [Test]
        public void NormalOddsAreOneEighthEachWay()
        {
            AssertOdds(_config.NormalOdds, new Dictionary<int, int> { { -1, 3 }, { 1, 3 }, { 0, 18 } });
        }

        [Test]
        public void RushOddsWorsen()
        {
            // 2x -ve 1/6, -ve 1/4, never positive.
            AssertOdds(_config.RushOdds, new Dictionary<int, int> { { -2, 4 }, { -1, 6 }, { 0, 14 } });
        }

        [Test]
        public void SteadyOddsImprove()
        {
            // -ve 1/12, +ve 1/3, 2x +ve 1/6.
            AssertOdds(_config.SteadyOdds, new Dictionary<int, int> { { -1, 2 }, { 1, 8 }, { 2, 4 }, { 0, 10 } });
        }

        [Test]
        public void SeededRollsRoughlyMatchOdds()
        {
            var random = new SeededRandom(7);
            int negative = 0, positive = 0;
            const int rolls = 24000;
            for (int i = 0; i < rolls; i++)
            {
                int steps = _config.NormalOdds.Roll(random);
                if (steps < 0) negative++;
                if (steps > 0) positive++;
            }
            Assert.AreEqual(rolls / 8, negative, rolls / 80);
            Assert.AreEqual(rolls / 8, positive, rolls / 80);
        }

        [Test]
        public void OverfullOddsAreRejected()
        {
            var odds = ModifierOdds.Over(4, new ModifierOutcome { Steps = 1, Weight = 5 });
            Assert.Throws<System.InvalidOperationException>(() => odds.Roll(new SeededRandom(1)));
        }

        [TestCase(0, 12)]
        [TestCase(1, 14)]
        [TestCase(-1, 10)]
        [TestCase(-2, 8)]
        [TestCase(2, 16)]
        public void WardingModifierChangesDurabilityByTwoPerStep(int steps, int expected)
        {
            var line = new Line(1, Player.A, DrawingSpec.Warding(4), steps, _config);
            Assert.AreEqual(expected, line.MaxDurability);
            Assert.AreEqual(expected, line.Durability);
        }

        [TestCase(0, 0, 3)]
        [TestCase(2, 0, 9)]
        [TestCase(2, 1, 11)]
        [TestCase(0, -2, 1)]
        public void ForbiddanceDurabilityAndModifier(int bindPoints, int steps, int expected)
        {
            var line = new Line(1, Player.A, DrawingSpec.Forbiddance(bindPoints), steps, _config);
            Assert.AreEqual(expected, line.MaxDurability);
        }

        [TestCase(VigorTier.Low, 3, 1)]
        [TestCase(VigorTier.Medium, 5, 2)]
        [TestCase(VigorTier.High, 7, 3)]
        public void VigorSpeedAndDamageComeFromItsTier(VigorTier tier, int speed, int damage)
        {
            var line = new Line(1, Player.A, DrawingSpec.Vigor(tier), 0, _config);
            Assert.AreEqual(speed, line.Speed);
            Assert.AreEqual(damage, line.Damage);
            Assert.IsFalse(line.HasDurability);
        }

        [TestCase(VigorTier.Medium, 1, 3)]
        [TestCase(VigorTier.Medium, -2, 0)]
        [TestCase(VigorTier.Low, -1, 0)]
        public void VigorModifierChangesDamageByOnePerStep(VigorTier tier, int steps, int expected)
        {
            var line = new Line(1, Player.A, DrawingSpec.Vigor(tier), steps, _config);
            Assert.AreEqual(expected, line.Damage);
        }

        [Test]
        public void ChalklingStatsComeFromBuffsAndBinding()
        {
            var spec = DrawingSpec.Making(durabilityBuffs: 2, damageBuffs: 3, speedBuffs: 1, boundBindPoints: 1);
            var line = new Line(1, Player.A, spec, 0, _config);
            Assert.AreEqual(1 + 2 * 2 + 1 * 2, line.MaxDurability);
            Assert.AreEqual(3, line.Damage);
            Assert.AreEqual(1, line.Speed);
            Assert.AreEqual(6, line.TotalStatBuffs);
        }

        [Test]
        public void UnbuffedChalklingHasBaseStats()
        {
            var line = new Line(1, Player.A, DrawingSpec.Making(glyphs: 1), 0, _config);
            Assert.AreEqual(1, line.MaxDurability);
            Assert.AreEqual(0, line.Damage);
            Assert.AreEqual(0, line.Speed);
        }

        [Test]
        public void PositiveChalklingModifierGoesToTheChosenStat()
        {
            var spec = DrawingSpec.Making(damageBuffs: 2, positiveModifierStat: ChalklingStat.Speed);
            var line = new Line(1, Player.A, spec, 2, _config);
            Assert.AreEqual(2, line.Speed);
            Assert.AreEqual(2, line.Damage);
        }

        [Test]
        public void NegativeChalklingModifierHitsTheHighestStat()
        {
            // Durability 1, damage 4: the -1 comes off damage.
            var line = new Line(1, Player.A, DrawingSpec.Making(damageBuffs: 4), -1, _config);
            Assert.AreEqual(3, line.Damage);
            Assert.AreEqual(1, line.MaxDurability);
        }

        [Test]
        public void NegativeChalklingModifierRechecksTheHighestStatEachStep()
        {
            // Durability 5, damage 5: durability wins the tie, then damage is highest.
            var line = new Line(1, Player.A, DrawingSpec.Making(durabilityBuffs: 2, damageBuffs: 5), -2, _config);
            Assert.AreEqual(4, line.MaxDurability);
            Assert.AreEqual(4, line.Damage);
        }

        private static void AssertOdds(ModifierOdds odds, Dictionary<int, int> expectedCounts)
        {
            var counts = new Dictionary<int, int>();
            for (int value = 0; value < odds.Denominator; value++)
            {
                int steps = odds.Roll(new ScriptedRandom(0, value));
                counts[steps] = counts.TryGetValue(steps, out int c) ? c + 1 : 1;
            }
            CollectionAssert.AreEquivalent(expectedCounts, counts);
        }
    }
}
