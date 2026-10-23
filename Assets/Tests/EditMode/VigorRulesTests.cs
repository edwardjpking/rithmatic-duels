using NUnit.Framework;

namespace RithmaticDuels.Rules.Tests
{
    public class VigorRulesTests
    {
        private RulesConfig _config;
        private int _nextId;

        [SetUp]
        public void SetUp()
        {
            _config = new RulesConfig();
            _nextId = 1;
        }

        private Line Lov(VigorTier tier, int modifierSteps = 0)
        {
            return new Line(_nextId++, Player.A, DrawingSpec.Vigor(tier), modifierSteps, _config);
        }

        [Test]
        public void HighHittingLowBecomesMedium()
        {
            var high = Lov(VigorTier.High);
            var low = Lov(VigorTier.Low);
            CollectionAssert.AreEqual(new[] { low.Id }, VigorRules.Collide(high, low, _config));
            Assert.AreEqual(LineStatus.Destroyed, low.Status);
            Assert.AreEqual(VigorTier.Medium, high.Tier);
            Assert.AreEqual(5, high.Speed);
            Assert.AreEqual(2, high.Damage);
        }

        [Test]
        public void HighHittingMediumBecomesLow()
        {
            var medium = Lov(VigorTier.Medium);
            var high = Lov(VigorTier.High);
            VigorRules.Collide(medium, high, _config);
            Assert.AreEqual(LineStatus.Destroyed, medium.Status);
            Assert.AreEqual(VigorTier.Low, high.Tier);
            Assert.IsTrue(high.IsActive);
        }

        [Test]
        public void EqualTiersDestroyEachOther()
        {
            var a = Lov(VigorTier.Medium);
            var b = Lov(VigorTier.Medium);
            Assert.AreEqual(2, VigorRules.Collide(a, b, _config).Length);
            Assert.IsFalse(a.IsActive);
            Assert.IsFalse(b.IsActive);
        }

        [Test]
        public void ModifierStaysWithTheSurvivor()
        {
            var high = Lov(VigorTier.High, modifierSteps: 1);
            VigorRules.Collide(high, Lov(VigorTier.Low), _config);
            Assert.AreEqual(3, high.Damage, "Medium base 2 plus the +1 modifier");
        }

        [Test]
        public void SurvivorLeftAtZeroDamageFizzles()
        {
            var high = Lov(VigorTier.High, modifierSteps: -1);
            var medium = Lov(VigorTier.Medium);
            Assert.AreEqual(2, VigorRules.Collide(high, medium, _config).Length);
            Assert.IsFalse(high.IsActive);
        }

        [TestCase(2, 1, 3f)]
        [TestCase(1, 4, 0.5f)]
        [TestCase(2, 5, 0.8f)]
        [TestCase(3, 1, 3f)]
        [TestCase(0, 3, 0f)]
        public void PushIsTwiceDamageOverMaxDurabilityCappedAtThree(int damage, int maxDurability, float expected)
        {
            Assert.AreEqual(expected, VigorRules.PushDistance(damage, maxDurability, _config), 1e-5);
        }
    }
}
