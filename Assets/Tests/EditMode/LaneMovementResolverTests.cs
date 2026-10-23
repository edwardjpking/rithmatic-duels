using System.Linq;
using NUnit.Framework;
using RithmaticDuels.Rules.Prototype;

namespace RithmaticDuels.Rules.Tests
{
    public class LaneMovementResolverTests
    {
        private RulesConfig _config;
        private LaneConfig _lane;
        private LaneMovementResolver _movement;
        private Duel _duel;

        private static readonly PlayerAction Pass = PlayerAction.Pass();
        private static readonly PlayerAction Continue = PlayerAction.Continue();

        [SetUp]
        public void SetUp()
        {
            _config = new RulesConfig();
            // Low tier LoV fly 3 per tick, so a 5-long lane takes them two ticks.
            _lane = new LaneConfig { Length = 5f };
            _movement = new LaneMovementResolver(_lane);
            _duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), _movement);

            // Both main LoW: 2 bind points is 1 turn, completed on the tick after starting.
            _duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), PlayerAction.Start(DrawingSpec.Warding(2)));
            _duel.ResolveTurn(Continue, Continue);
        }

        private Line Ward(Player player)
        {
            return _duel.GetPlayer(player).MainWard;
        }

        [Test]
        public void DefaultLaneLengthComesFromTheBoard()
        {
            var resolver = new LaneMovementResolver();
            // 32 wide, minus 6 inset and 3 radius at each end.
            Assert.AreEqual(14f, resolver.LaneLength(_config), 1e-4);
        }

        [Test]
        public void VigorFliesDownTheLaneThenHitsTheMainWard()
        {
            _duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low)), Pass);
            var report = _duel.ResolveTurn(Continue, Pass); // completes and flies 3
            var vigor = _duel.Lines.Last();
            Assert.AreEqual(LineType.Vigor, vigor.Type);
            Assert.AreEqual(3f, _movement.DistanceTravelled(vigor.Id), 1e-4);
            Assert.IsFalse(report.Events.Any(e => e.Type == TurnEventType.LineDamaged));

            report = _duel.ResolveTurn(Pass, Pass);
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.LineDamaged && e.LineId == Ward(Player.B).Id));
            Assert.AreEqual(Ward(Player.B).MaxDurability - vigor.Damage, Ward(Player.B).Durability);
            Assert.IsFalse(vigor.IsActive);
            Assert.That(_movement.LastTickNotes.Single(), Does.Contain("hit"));
        }

        [Test]
        public void ForbiddanceReflectsVigorAway()
        {
            _duel.ResolveTurn(Pass, PlayerAction.Start(DrawingSpec.Forbiddance(3)));
            _duel.ResolveTurn(Pass, Continue); // LoF with 3 bind points takes 1 turn

            _duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low)), Pass);
            _duel.ResolveTurn(Continue, Pass);
            _duel.ResolveTurn(Pass, Pass);

            Assert.AreEqual(Ward(Player.B).MaxDurability, Ward(Player.B).Durability);
            Assert.That(_movement.LastTickNotes.Single(), Does.Contain("reflected"));
        }

        [Test]
        public void EqualTierVigorCollideAndCancelOut()
        {
            var low = PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low));
            _duel.ResolveTurn(low, low);
            _duel.ResolveTurn(Continue, Continue); // both fly 3 toward each other on a 5-long lane and meet

            Assert.IsFalse(_duel.Lines.Any(l => l.Type == LineType.Vigor && l.IsActive));
            Assert.That(_movement.LastTickNotes.Single(), Does.Contain("cancelled"));
            Assert.AreEqual(Ward(Player.A).MaxDurability, Ward(Player.A).Durability);
            Assert.AreEqual(Ward(Player.B).MaxDurability, Ward(Player.B).Durability);
        }

        [Test]
        public void BreachingTheMainWardWinsTheDuel()
        {
            _config.Warding.Durability = 1;
            _duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), _movement);
            _duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), PlayerAction.Start(DrawingSpec.Warding(2)));
            _duel.ResolveTurn(Continue, Continue);

            _duel.ResolveTurn(Pass, PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low)));
            var report = _duel.ResolveTurn(Pass, Continue);
            Assert.AreEqual(DuelOutcome.InProgress, report.Outcome);

            report = _duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.PlayerBWins, report.Outcome);
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.MainWardBreached && e.Player == Player.A));
        }
    }
}
