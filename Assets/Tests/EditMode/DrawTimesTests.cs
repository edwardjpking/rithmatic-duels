using NUnit.Framework;

namespace RithmaticDuels.Rules.Tests
{
    public class DrawTimesTests
    {
        private RulesConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = new RulesConfig();
        }

        [TestCase(2, 1)]
        [TestCase(4, 2)]
        [TestCase(6, 3)]
        [TestCase(9, 4)]
        [TestCase(0, 1)]
        public void MainWardTakesBindPointsDivTwo(int bindPoints, int expected)
        {
            Assert.AreEqual(expected, DrawTimes.BaseTurns(DrawingSpec.Warding(bindPoints), _config));
        }

        [Test]
        public void MarksCrossTakesTwoTurns()
        {
            Assert.AreEqual(2, DrawTimes.BaseTurns(DrawingSpec.MarksCross(_config), _config));
        }

        [TestCase(0, 3f, 2)]
        [TestCase(0, 4f, 2)]
        [TestCase(0, 4.5f, 3)]
        [TestCase(0, 8f, 4)]
        [TestCase(1, 5f, 2)]
        public void ForbiddanceTimeScalesWithLengthLessBindPoints(int bindPoints, float length, int expected)
        {
            var spec = DrawingSpec.Forbiddance(bindPoints);
            spec.Length = length;
            Assert.AreEqual(expected, DrawTimes.BaseTurns(spec, _config));
        }

        [TestCase(1f, 1)]
        [TestCase(3f, 1)]
        [TestCase(3.1f, 2)]
        [TestCase(6f, 2)]
        public void ForbiddanceBetweenTwoBindPointsTakesOneTurnUpToLengthThree(float length, int expected)
        {
            var spec = DrawingSpec.Forbiddance(2);
            spec.Length = length;
            Assert.AreEqual(expected, DrawTimes.BaseTurns(spec, _config));
        }

        [Test]
        public void ForbiddanceWithoutALengthUsesTheDefault()
        {
            Assert.AreEqual(2, DrawTimes.BaseTurns(DrawingSpec.Forbiddance(0), _config)); // 3 units
        }

        [TestCase(3, 0, 0, 2)]
        [TestCase(4, 0, 0, 2)]
        [TestCase(2, 2, 1, 2)]
        [TestCase(6, 3, 0, 3)]
        [TestCase(0, 0, 0, 2)]
        public void MakingTakesOnePerFourStatBuffs(int durability, int damage, int speed, int expected)
        {
            // The glyph is free, whichever it is.
            Assert.AreEqual(expected, DrawTimes.BaseTurns(DrawingSpec.Making(durability, damage, speed, Glyph.Defend), _config));
        }

        [Test]
        public void ABoundChalklingMustDefend()
        {
            Assert.AreEqual(Glyph.Defend, DrawingSpec.Making(glyph: Glyph.Attack, boundBindPoints: 1).Glyph);
            var spec = DrawingSpec.Making(boundBindPoints: 1);
            spec.Glyph = Glyph.Attack;
            Assert.IsNotNull(DrawTimes.Validate(spec, _config));
        }

        [TestCase(Glyph.Attack)]
        [TestCase(Glyph.Defend)]
        public void AnUnboundChalklingCanAttackOrDefend(Glyph glyph)
        {
            Assert.IsNull(DrawTimes.Validate(DrawingSpec.Making(speedBuffs: 1, glyph: glyph), _config));
        }

        [TestCase(VigorTier.Low, 1)]
        [TestCase(VigorTier.Medium, 2)]
        [TestCase(VigorTier.High, 3)]
        public void VigorDrawTimeComesFromItsTier(VigorTier tier, int expected)
        {
            Assert.AreEqual(expected, DrawTimes.BaseTurns(DrawingSpec.Vigor(tier), _config));
        }

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        public void RushHalvesRoundedUp(int baseTurns, int expected)
        {
            Assert.AreEqual(expected, DrawTimes.ApplyPace(baseTurns, DrawingPace.Rush, _config));
        }

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 5)]
        [TestCase(4, 6)]
        public void SteadyAddsHalfRoundedUpByDefault(int baseTurns, int expected)
        {
            Assert.AreEqual(expected, DrawTimes.ApplyPace(baseTurns, DrawingPace.Steady, _config));
        }

        [Test]
        public void SteadyRoundingComesFromConfig()
        {
            _config.Steady.Rounding = RoundingMode.Down;
            Assert.AreEqual(4, DrawTimes.ApplyPace(3, DrawingPace.Steady, _config));
        }

        [Test]
        public void NormalPaceLeavesTimeUnchanged()
        {
            Assert.AreEqual(3, DrawTimes.ApplyPace(3, DrawingPace.Normal, _config));
        }

        [Test]
        public void FormulasReadFromConfig()
        {
            _config.Forbiddance.LengthPerDrawTurn = 1f;
            Assert.AreEqual(2, DrawTimes.BaseTurns(DrawingSpec.Forbiddance(1), _config));
        }

        [Test]
        public void WardingCannotExceedTwelveBindPoints()
        {
            Assert.IsNull(DrawTimes.Validate(DrawingSpec.Warding(12), _config));
            Assert.IsNotNull(DrawTimes.Validate(DrawingSpec.Warding(13), _config));
        }

    }
}
