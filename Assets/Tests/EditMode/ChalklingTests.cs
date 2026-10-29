using System.Linq;
using NUnit.Framework;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Tests
{
    /// <summary>Chalkling behaviour on a real board (DESIGN.md section 4.4).</summary>
    public class ChalklingTests
    {
        private RulesConfig _config;
        private ChalkBoard _board;
        private Duel _duel;

        private static readonly PlayerAction Pass = PlayerAction.Pass();
        private static readonly PlayerAction Continue = PlayerAction.Continue();

        [SetUp]
        public void SetUp()
        {
            _config = new RulesConfig();
            _config.CentreWall.Enabled = false;
            _config.Warding.MainWardDurabilityMultiplier = 1f;
            _board = new ChalkBoard(_config);
            _duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), _board);
            var ward = PlayerAction.Start(DrawingSpec.Warding(4));
            _duel.ResolveTurn(ward, ward);
            while (_duel.GetPlayer(Player.A).HasWorkInProgress) _duel.ResolveTurn(Continue, Continue);
        }

        private Line MainWard(Player player) { return _duel.GetPlayer(player).MainWard; }

        private Line Draw(Player player, DrawingSpec spec, Placement placement)
        {
            spec = _board.Measure(player, spec, placement);
            Assert.IsNull(_board.ValidatePlacement(player, spec, placement));
            _board.SetPendingPlacement(player, spec, placement);
            var start = PlayerAction.Start(spec);
            if (player == Player.A) _duel.ResolveTurn(start, Pass); else _duel.ResolveTurn(Pass, start);
            while (_duel.GetPlayer(player).HasWorkInProgress)
            {
                if (player == Player.A) _duel.ResolveTurn(Continue, Pass); else _duel.ResolveTurn(Pass, Continue);
            }
            return _duel.Lines.Last(l => l.Owner == player);
        }

        private void PassTurn() { _duel.ResolveTurn(Pass, Pass); }

        private ChalklingBody Body(Line line) { return (ChalklingBody)_board.BodyOf(line.Id); }

        private static Vec2 V(float x, float y) { return new Vec2(x, y); }

        [Test]
        public void AttackWalksToTheOpponentsMainWardAndScratchesIt()
        {
            var attacker = Draw(Player.A, DrawingSpec.Making(damageBuffs: 2, speedBuffs: 3), Placement.At(V(14f, 10f)));
            int full = MainWard(Player.B).Durability;
            for (int i = 0; i < 6 && MainWard(Player.B).Durability == full; i++) PassTurn();

            Assert.AreEqual(full - 2, MainWard(Player.B).Durability);
            Assert.AreEqual(23f - _config.Making.Radius, Body(attacker).Position.X, 0.05f);
            Assert.AreEqual(10f, Body(attacker).Position.Y, 0.05f);
        }

        [Test]
        public void AttackPathPreviewStopsAtTheOpponentsWard()
        {
            var path = _board.ChalklingAttackPath(Player.A, V(14f, 10f));
            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(23f - _config.Making.Radius, path[1].X, 0.05f);
        }

        [Test]
        public void OwnWardLetsItsChalklingsOut()
        {
            // Drawn inside player A's own circle, it walks straight out through it.
            var attacker = Draw(Player.A, DrawingSpec.Making(speedBuffs: 2), Placement.At(V(6f, 10f)));
            PassTurn();
            PassTurn();
            Assert.Greater(Body(attacker).Position.X, 9f);
        }

        [Test]
        public void EnemyForbiddanceBlocksAndIsAttackedFirst()
        {
            var lof = Draw(Player.B, DrawingSpec.Forbiddance(0), Placement.Segment(V(20f, 8f), V(20f, 12f)));
            int full = lof.Durability;
            Draw(Player.A, DrawingSpec.Making(damageBuffs: 1, speedBuffs: 4), Placement.At(V(15f, 10f)));
            for (int i = 0; i < 4 && lof.Durability == full; i++) PassTurn();
            Assert.Less(lof.Durability, full);
            Assert.AreEqual(MainWard(Player.B).MaxDurability, MainWard(Player.B).Durability);
        }

        [Test]
        public void DefenderMeetsAnAttackerAndTheyCanDestroyEachOther()
        {
            var defender = Draw(Player.B, DrawingSpec.Making(damageBuffs: 1, speedBuffs: 2, glyph: Glyph.Defend), Placement.At(V(19f, 10f)));
            var attacker = Draw(Player.A, DrawingSpec.Making(damageBuffs: 1, speedBuffs: 2), Placement.At(V(14f, 10f)));
            for (int i = 0; i < 6 && (attacker.IsActive || defender.IsActive); i++) PassTurn();

            Assert.IsFalse(attacker.IsActive);
            Assert.IsFalse(defender.IsActive);
            Assert.AreEqual(MainWard(Player.B).MaxDurability, MainWard(Player.B).Durability);
        }

        [Test]
        public void IdleDefenderGoesBackToItsAnchor()
        {
            var defender = Draw(Player.B, DrawingSpec.Making(speedBuffs: 2, glyph: Glyph.Defend), Placement.At(V(19f, 10f)));
            Assert.AreEqual(_config.Making.GuardDistance, Body(defender).GuardDistance, 1e-4);
            Body(defender).Position = V(21f, 10f);
            PassTurn();
            Assert.AreEqual(19f, Body(defender).Position.X, 1e-3);
        }

        [Test]
        public void VigorCarriesThroughAChalklingItDestroys()
        {
            // A 1-durability chalkling in front of B's LoW; a High LoV (3 damage) destroys it and hits the LoW for 2.
            var blocker = Draw(Player.B, DrawingSpec.Making(glyph: Glyph.Defend), Placement.At(V(20f, 10f)));
            int full = MainWard(Player.B).Durability;
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(12f, 10f), V(26f, 10f)));
            for (int i = 0; i < 4 && MainWard(Player.B).Durability == full; i++) PassTurn();

            Assert.IsFalse(blocker.IsActive);
            Assert.AreEqual(full - 2, MainWard(Player.B).Durability);
        }

        [Test]
        public void AttackNeverAttacksItsOwnForbiddance()
        {
            // Player A's own LoF holds its chalkling in, but the chalkling never scratches at it.
            var lof = Draw(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(V(12f, 8f), V(12f, 12f)));
            int full = lof.Durability;
            Draw(Player.A, DrawingSpec.Making(damageBuffs: 1, speedBuffs: 2), Placement.At(V(11f, 10f)));
            for (int i = 0; i < 4; i++) PassTurn();
            Assert.AreEqual(full, lof.Durability);
            Assert.IsTrue(lof.IsActive);
        }

        [Test]
        public void OnlyOneChalklingPerBindPoint()
        {
            var spec = DrawingSpec.Making(glyph: Glyph.Defend, boundBindPoints: 1);
            Draw(Player.A, spec, Placement.AtBindPoint(0));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, spec, Placement.AtBindPoint(0)), Placement.AtBindPoint(0)));
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, spec, Placement.AtBindPoint(1)), Placement.AtBindPoint(1)));
        }

        [Test]
        public void VigorPassesThroughItsOwnLines()
        {
            // Player A's own LoF and chalkling stand in the way; A's LoV flies through both and hits B's main LoW.
            var lof = Draw(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(V(13f, 8f), V(13f, 12f)));
            var chalkling = Draw(Player.A, DrawingSpec.Making(glyph: Glyph.Defend), Placement.At(V(14f, 10f)));
            int lofFull = lof.Durability;
            int full = MainWard(Player.B).Durability;
            var aim = _board.AimPath(V(11f, 10f), V(1f, 0f), Player.A);
            Assert.AreEqual(2, aim.Count);
            Assert.AreEqual(23f, aim[1].X, 0.05f, "the aim line runs through A's own lines to B's LoW");
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(11f, 10f), V(26f, 10f)));
            for (int i = 0; i < 4 && MainWard(Player.B).Durability == full; i++) PassTurn();

            Assert.Less(MainWard(Player.B).Durability, full);
            Assert.AreEqual(lofFull, lof.Durability);
            Assert.IsTrue(chalkling.IsActive);
        }

        [Test]
        public void ChalklingsTakeTwoTurnsUnlessRushed()
        {
            var spec = DrawingSpec.Making(glyph: Glyph.Defend);
            Assert.AreEqual(2, DrawTimes.Turns(spec, DrawingPace.Normal, _config));
            Assert.AreEqual(1, DrawTimes.Turns(spec, DrawingPace.Rush, _config));
        }

        [Test]
        public void AttackNeedsSpeed()
        {
            Assert.IsNotNull(DrawTimes.Validate(DrawingSpec.Making(glyph: Glyph.Attack), _config));
            Assert.IsNull(DrawTimes.Validate(DrawingSpec.Making(speedBuffs: 1, glyph: Glyph.Attack), _config));
        }
    }

    /// <summary>The centre LoF's timing (DESIGN.md section 1.1).</summary>
    public class CentreWallTests
    {
        [Test]
        public void CentreWallGoesInStageOneOfItsLastTurn()
        {
            var config = new RulesConfig();
            config.CentreWall.Turns = 10;
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);
            var pass = PlayerAction.Pass();
            var ward = PlayerAction.Start(DrawingSpec.Warding(4));
            duel.ResolveTurn(ward, ward);
            while (duel.GetPlayer(Player.A).HasWorkInProgress) duel.ResolveTurn(PlayerAction.Continue(), PlayerAction.Continue());
            // Between ticks, the turn being chosen is duel.Turn + 1.
            while (duel.Turn + 1 < config.CentreWall.Turns)
            {
                duel.ResolveTurn(pass, pass);
                Assert.That(board.LastTickNotes, Has.None.Contains("centre"));
            }
            // Choosing the last turn: still up. It goes during that turn's tick, before anything moves.
            Assert.IsTrue(board.CentreWallUp);
            duel.ResolveTurn(pass, pass);
            Assert.That(board.LastTickNotes, Has.Some.Contains("centre Line of Forbiddance is gone"));
            Assert.IsFalse(board.CentreWallUp);
        }
    }
}
