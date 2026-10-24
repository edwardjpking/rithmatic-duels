using System;
using System.Linq;
using NUnit.Framework;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Tests
{
    public class BindPointTests
    {
        private static readonly Vec2 Centre = new Vec2(6f, 10f);
        private static readonly Vec2 Right = new Vec2(1f, 0f);

        [TestCase(4)]
        [TestCase(6)]
        [TestCase(9)]
        public void StandardCirclesHaveTheirBindPointCount(int count)
        {
            var points = BindPoints.ForCircle(count, Centre, 3f, Right);
            Assert.AreEqual(count, points.Count);
            foreach (var point in points) Assert.AreEqual(3f, Vec2.Distance(point, Centre), 1e-3);
        }

        [TestCase(4)]
        [TestCase(6)]
        [TestCase(9)]
        public void TheFirstBindPointFacesTheOpponent(int count)
        {
            var first = BindPoints.ForCircle(count, Centre, 3f, Right)[0];
            Assert.AreEqual(9f, first.X, 1e-3);
            Assert.AreEqual(10f, first.Y, 1e-3);
        }

        [Test]
        public void ATriangleGivesAtMostTwelveBindPoints()
        {
            // Equilateral triangle around a radius 3 circle with inradius 2, so every edge cuts the circle twice.
            var a = Vec2.FromAngle(0f) * 4f;
            var b = Vec2.FromAngle(2f * (float)Math.PI / 3f) * 4f;
            var c = Vec2.FromAngle(4f * (float)Math.PI / 3f) * 4f;
            Assert.AreEqual(12, BindPoints.FromTriangle(Vec2.Zero, 3f, a, b, c).Count);
        }

        [Test]
        public void AnEllipseHasBindPointsAtTheEndsOfItsLongAxis()
        {
            var ellipse = WardShape.Ellipse(Centre, 3.5f, 2.5f, 0f);
            var points = BindPoints.ForEllipse(ellipse);
            Assert.AreEqual(2, points.Count);
            Assert.AreEqual(9.5f, points[0].X, 1e-3);
            Assert.AreEqual(2.5f, points[1].X, 1e-3);
        }
    }

    public class ShapeTests
    {
        [Test]
        public void ReflectionKeepsTheAngleOfIncidence()
        {
            // Travelling right into a 45 degree mirror turns the line straight up.
            var normal = new Vec2(-1f, 1f).Normalized;
            var reflected = Vec2.Reflect(new Vec2(1f, 0f), normal);
            Assert.AreEqual(0f, reflected.X, 1e-5);
            Assert.AreEqual(1f, reflected.Y, 1e-5);
        }

        [Test]
        public void RaysHitCirclesFromOutsideAndInside()
        {
            float distance;
            var circle = WardShape.Circle(Vec2.Zero, 3f);
            Assert.IsTrue(circle.Raycast(new Vec2(-10f, 0f), new Vec2(1f, 0f), out distance));
            Assert.AreEqual(7f, distance, 1e-4);
            Assert.IsTrue(circle.Raycast(Vec2.Zero, new Vec2(0f, 1f), out distance));
            Assert.AreEqual(3f, distance, 1e-4);
            Assert.IsFalse(circle.Raycast(new Vec2(-10f, 5f), new Vec2(1f, 0f), out distance));
        }

        [Test]
        public void RaysHitSegmentsWithinTheirEnds()
        {
            float distance;
            Vec2 normal;
            var wall = new Segment(new Vec2(5f, -1f), new Vec2(5f, 1f));
            Assert.IsTrue(Intersect.RaySegment(Vec2.Zero, new Vec2(1f, 0f), wall, out distance, out normal));
            Assert.AreEqual(5f, distance, 1e-4);
            Assert.AreEqual(-1f, normal.X, 1e-4);
            Assert.IsFalse(Intersect.RaySegment(new Vec2(0f, 2f), new Vec2(1f, 0f), wall, out distance, out normal));
        }
    }

    public class WardDurabilityTests
    {
        private static Line Ward(int modifierSteps = 0)
        {
            return new Line(1, Player.A, DrawingSpec.Warding(4), modifierSteps, new RulesConfig());
        }

        [Test]
        public void TheDefaultCircleHasTwelveAtEveryPoint()
        {
            var body = new WardBody(Ward(), WardShape.Circle(Vec2.Zero, 3f), new Vec2[0], new RulesConfig());
            Assert.AreEqual(12, body.Weakest);
            Assert.AreEqual(12, body.WeakestMax);
        }

        [Test]
        public void SmallerCirclesAreStrongerPerPoint()
        {
            var body = new WardBody(Ward(), WardShape.Circle(Vec2.Zero, 1f), new Vec2[0], new RulesConfig());
            // 12 x sqrt(1 / (1/3)) = 20.8
            Assert.AreEqual(21, body.Weakest);
        }

        [Test]
        public void AnEllipseIsStrongAtItsEndsAndWeakAlongItsSides()
        {
            var body = new WardBody(Ward(), WardShape.Ellipse(Vec2.Zero, 3.5f, 2.5f, 0f), new Vec2[0], new RulesConfig());
            int end = body.SampleMax(0);
            int side = body.SampleMax(body.SampleCount / 4);
            Assert.Greater(end, 12);
            Assert.Less(side, 12);
            Assert.AreEqual(side, body.Weakest);
        }

        [Test]
        public void AHitOnlyDamagesNearbyPoints()
        {
            var body = new WardBody(Ward(), WardShape.Circle(Vec2.Zero, 3f), new Vec2[0], new RulesConfig());
            body.ApplyHit(new Vec2(3f, 0f), 2, 0.5f);
            Assert.AreEqual(10, body.SampleDurability(0));
            Assert.AreEqual(12, body.SampleDurability(body.SampleCount / 2));
            Assert.AreEqual(10, body.Weakest);
        }

        [Test]
        public void TheModifierShiftsEveryPoint()
        {
            var body = new WardBody(Ward(+1), WardShape.Circle(Vec2.Zero, 3f), new Vec2[0], new RulesConfig());
            Assert.AreEqual(14, body.Weakest);
        }
    }

    public class ChalkBoardTests
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
            _board = new ChalkBoard(_config);
            _duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), _board);

            // Four-point circles: 2 turns each.
            var ward = PlayerAction.Start(DrawingSpec.Warding(4));
            _duel.ResolveTurn(ward, ward);
            while (_duel.GetPlayer(Player.A).HasWorkInProgress) _duel.ResolveTurn(Continue, Continue);
        }

        private Line MainWard(Player player) { return _duel.GetPlayer(player).MainWard; }

        /// <summary>
        /// Start a drawing for one player with a placement, and continue until it completes. The other passes.
        /// Travel turns are worked out from the placement, as the UI does.
        /// </summary>
        private Line Draw(Player player, DrawingSpec spec, Placement placement, DrawingPace pace = DrawingPace.Normal)
        {
            spec = spec.WithTravel(_board.TravelTurns(player, placement));
            Assert.IsNull(_board.ValidatePlacement(player, spec, placement));
            _board.SetPendingPlacement(player, spec, placement);
            var start = PlayerAction.Start(spec, pace);
            if (player == Player.A) _duel.ResolveTurn(start, Pass); else _duel.ResolveTurn(Pass, start);
            while (_duel.GetPlayer(player).HasWorkInProgress)
            {
                if (player == Player.A) _duel.ResolveTurn(Continue, Pass); else _duel.ResolveTurn(Pass, Continue);
            }
            return _duel.Lines.Last(l => l.Owner == player);
        }

        private TurnReport PassTurn() { return _duel.ResolveTurn(Pass, Pass); }

        private static Vec2 V(float x, float y) { return new Vec2(x, y); }

        [Test]
        public void MainWardsSitAtHomeWithTheirBindPoints()
        {
            var a = _board.MainWardOf(Player.A);
            var b = _board.MainWardOf(Player.B);
            Assert.AreEqual(6f, a.Shape.Centre.X, 1e-4);
            Assert.AreEqual(26f, b.Shape.Centre.X, 1e-4);
            Assert.AreEqual(4, _board.BindPointsOf(Player.A).Count);
            Assert.AreEqual(9f, _board.BindPointsOf(Player.A)[0].X, 1e-3); // front bind point faces B
            Assert.AreEqual(23f, _board.BindPointsOf(Player.B)[0].X, 1e-3);
            Assert.AreEqual(12, MainWard(Player.A).Durability);
        }

        [Test]
        public void VigorFliesAndHitsTheOpponentsWard()
        {
            // High tier: speed 7, damage 3. From x=10 it reaches B's ward front at x=23 on its second tick.
            var vigor = Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            var body = (VigorBody)_board.BodyOf(vigor.Id);
            Assert.AreEqual(17f, body.Position.X, 1e-3);

            var report = PassTurn();
            var damage = report.Events.Single(e => e.Type == TurnEventType.LineDamaged);
            Assert.AreEqual(MainWard(Player.B).Id, damage.LineId);
            Assert.AreEqual(3, damage.Value);
            Assert.AreEqual(9, MainWard(Player.B).Durability);
            Assert.IsFalse(vigor.IsActive);
            Assert.IsNull(_board.BodyOf(vigor.Id));
        }

        [Test]
        public void HitsOnTheSamePointAddUpButSpreadHitsDoNot()
        {
            var aim = V(26f, 10f);
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(16f, 10f), aim));
            PassTurn(); PassTurn(); PassTurn();
            Assert.AreEqual(11, MainWard(Player.B).Durability);

            // A hit on the top of the circle damages different points, so the weakest point stays at 11.
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(16f, 13f), V(26f, 13f)));
            PassTurn(); PassTurn(); PassTurn(); PassTurn();
            Assert.AreEqual(11, MainWard(Player.B).Durability);
            var ward = _board.MainWardOf(Player.B);
            Assert.AreEqual(11, ward.Weakest);
        }

        [Test]
        public void ForbiddanceReflectsVigorBack()
        {
            // B's wall on its own half, square to the line of fire.
            var wall = Draw(Player.B, DrawingSpec.Forbiddance(0), Placement.Segment(V(20f, 6f), V(20f, 14f)));
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.Medium), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            for (int i = 0; i < 5; i++) PassTurn();

            Assert.AreEqual(12, MainWard(Player.B).Durability);
            Assert.AreEqual(10, MainWard(Player.A).Durability);
            Assert.AreEqual(2, wall.Durability, "a reflected LoV deals half its damage (2), rounded up, to the LoF");
        }

        [Test]
        public void AnAngledForbiddanceSendsVigorOffTheBoard()
        {
            Draw(Player.B, DrawingSpec.Forbiddance(0), Placement.Segment(V(18f, 8f), V(22f, 12f)));
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            for (int i = 0; i < 3; i++) PassTurn();

            Assert.AreEqual(12, MainWard(Player.A).Durability);
            Assert.AreEqual(12, MainWard(Player.B).Durability);
            Assert.IsFalse(_duel.Lines.Any(l => l.Type == LineType.Vigor && l.IsActive));
        }

        [Test]
        public void LinesBetweenBindPointsAndTangentsUseTheMainWard()
        {
            var chord = Draw(Player.A, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(0, 1));
            var tangent = Draw(Player.A, DrawingSpec.Forbiddance(1), Placement.TangentAt(0));
            var points = _board.BindPointsOf(Player.A);

            var chordSegment = ((ForbiddanceBody)_board.BodyOf(chord.Id)).Segment;
            Assert.AreEqual(0f, Vec2.Distance(chordSegment.A, points[0]), 1e-4);
            Assert.AreEqual(0f, Vec2.Distance(chordSegment.B, points[1]), 1e-4);

            var tangentSegment = ((ForbiddanceBody)_board.BodyOf(tangent.Id)).Segment;
            Assert.AreEqual(9f, tangentSegment.A.X, 1e-3);
            Assert.AreEqual(9f, tangentSegment.B.X, 1e-3);
            Assert.AreEqual(_config.Geometry.TangentForbiddanceLength, tangentSegment.Length, 1e-3);
        }

        [Test]
        public void AttachedWardsSitOutsideTheMainWard()
        {
            var cross = Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(0));
            var body = (WardBody)_board.BodyOf(cross.Id);
            Assert.AreEqual(10f, body.Shape.Centre.X, 1e-3); // front bind point at x=9, radius 1
            Assert.AreEqual(4, body.BindPoints.Count);
            Assert.AreEqual(21, cross.Durability); // smaller circle, stronger per point
        }

        [Test]
        public void VigorOnVigorUsesTheTierRules()
        {
            _board.SetPendingPlacement(Player.A, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(14f, 10f), V(26f, 10f)));
            _board.SetPendingPlacement(Player.B, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(18f, 10f), V(6f, 10f)));
            var low = PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low));
            _duel.ResolveTurn(low, low); // both complete, fly 3 each and meet in the middle

            Assert.IsFalse(_duel.Lines.Any(l => l.Type == LineType.Vigor && l.IsActive));
            Assert.That(_board.LastTickNotes.Single(), Does.Contain("cancelled"));
        }

        [Test]
        public void VigorHitsAndPushesABoundChalklingWithinItsTether()
        {
            var spec = DrawingSpec.Making(durabilityBuffs: 2, boundBindPoints: 1);
            var chalkling = Draw(Player.B, spec, Placement.AtBindPoint(0));
            Assert.AreEqual(7, chalkling.Durability); // 1 + 2 x 2 + 2 for binding

            Draw(Player.A, DrawingSpec.Vigor(VigorTier.Medium), Placement.Aimed(V(12f, 10f), V(26f, 10f)));
            for (int i = 0; i < 3 && chalkling.Durability == 7; i++) PassTurn();

            Assert.AreEqual(5, chalkling.Durability);
            Assert.AreEqual(12, MainWard(Player.B).Durability);
            var body = (ChalklingBody)_board.BodyOf(chalkling.Id);
            Assert.LessOrEqual(Vec2.Distance(body.Position, body.Tether.Value), _config.Making.TetherDistance + 1e-4);
            Assert.Greater(body.Position.X, 23.25f); // pushed back toward B
        }

        [Test]
        public void RepairsSpreadOverTheWholeWard()
        {
            Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            PassTurn();
            Assert.AreEqual(9, MainWard(Player.B).Durability);

            var repair = PlayerAction.Repair(MainWard(Player.B).Id, 2);
            _duel.ResolveTurn(Pass, repair);
            while (_duel.GetPlayer(Player.B).HasWorkInProgress) _duel.ResolveTurn(Pass, Continue);
            PassTurn(); // the board catches up on the next tick

            Assert.AreEqual(11, MainWard(Player.B).Durability);
            Assert.AreEqual(11, _board.MainWardOf(Player.B).Weakest);
        }

        [Test]
        public void AnEllipticalMainWardIsWeakestAlongItsSides()
        {
            var config = new RulesConfig();
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), PlayerAction.Start(DrawingSpec.Warding(4)));
            while (duel.GetPlayer(Player.B).HasWorkInProgress) duel.ResolveTurn(Pass, Continue);

            var ellipse = duel.GetPlayer(Player.A).MainWard;
            Assert.IsNotNull(ellipse);
            Assert.Less(ellipse.Durability, 12);
            Assert.AreEqual(2, board.BindPointsOf(Player.A).Count);
        }

        [Test]
        public void RepeatedVigorOnOnePointBreachesTheMainWard()
        {
            for (int shot = 0; shot < 10 && _duel.Outcome == DuelOutcome.InProgress; shot++)
            {
                // Each shot is drawn while the last one is still flying, so the duel can end mid-draw.
                _board.SetPendingPlacement(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(16f, 10f), V(26f, 10f)));
                _duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Vigor(VigorTier.High)), Pass);
                while (_duel.Outcome == DuelOutcome.InProgress && _duel.GetPlayer(Player.A).HasWorkInProgress)
                    _duel.ResolveTurn(Continue, Pass);
            }
            for (int i = 0; i < 3 && _duel.Outcome == DuelOutcome.InProgress; i++) PassTurn();
            // 12 durability, 3 damage a hit at the same point.
            Assert.AreEqual(DuelOutcome.PlayerAWins, _duel.Outcome);
            Assert.AreEqual(0, MainWard(Player.B).Durability);
        }

        [Test]
        public void TravelTurnsGrowWithDistanceFromTheMainWard()
        {
            // A's four-point circle: centre (6, 10), radius 3, so its outline reaches x = 9.
            Assert.AreEqual(0, _board.TravelTurns(Player.A, Placement.Aimed(V(6f, 10f), V(26f, 10f))), "inside");
            Assert.AreEqual(0, _board.TravelTurns(Player.A, Placement.Aimed(V(10.5f, 10f), V(26f, 10f))));
            Assert.AreEqual(1, _board.TravelTurns(Player.A, Placement.Aimed(V(12f, 10f), V(26f, 10f))));
            Assert.AreEqual(2, _board.TravelTurns(Player.A, Placement.At(V(14f, 10f))));
            Assert.AreEqual(3, _board.TravelTurns(Player.A, Placement.Aimed(V(16f, 18f), V(26f, 10f))));
            Assert.AreEqual(0, _board.TravelTurns(Player.A, Placement.TangentAt(0)), "bind points cost nothing");
            Assert.AreEqual(1, _board.TravelTurns(Player.A, Placement.Segment(V(12f, 4f), V(12f, 10f))), "nearest point counts");
        }

        [Test]
        public void ADrawingFarFromTheMainWardTakesLonger()
        {
            var spec = DrawingSpec.Vigor(VigorTier.Low).WithTravel(_board.TravelTurns(Player.A, Placement.At(V(14f, 10f))));
            _board.SetPendingPlacement(Player.A, spec, Placement.Aimed(V(14f, 10f), V(26f, 10f)));
            _duel.ResolveTurn(PlayerAction.Start(spec), Pass);
            Assert.AreEqual(1 + 2, _duel.GetPlayer(Player.A).Work.TotalTurns);
        }

        [Test]
        public void TangentForbiddanceLengthCanBeChosen()
        {
            var tangent = Draw(Player.A, DrawingSpec.Forbiddance(1), Placement.TangentAt(0, 5f));
            Assert.AreEqual(5f, ((ForbiddanceBody)_board.BodyOf(tangent.Id)).Segment.Length, 1e-3);
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Forbiddance(1), Placement.TangentAt(0, 20f)));
        }

        [Test]
        public void AChordForbiddanceTakesHitsOnTheSmallerArcFirst()
        {
            // A's four-point circle: bind point 0 faces B at (9, 10); the chord to its neighbour cuts off the front arc.
            var points = _board.BindPointsOf(Player.A);
            int neighbour = 1;
            var chord = Draw(Player.A, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(0, neighbour));
            int before = chord.Durability;

            // B fires at the middle of that arc.
            Vec2 middle = (points[0] + points[neighbour]) / 2f;
            Vec2 onArc = V(6f, 10f) + (middle - V(6f, 10f)).Normalized * 3f;
            Vec2 from = V(20f, onArc.Y);
            Draw(Player.B, DrawingSpec.Vigor(VigorTier.Medium), Placement.Aimed(from, onArc));
            for (int i = 0; i < 4; i++) PassTurn();

            Assert.AreEqual(12, MainWard(Player.A).Durability, "the LoW behind the chord is untouched");
            Assert.AreEqual(before - 2, chord.Durability, "the chord takes the full damage");
        }

        [Test]
        public void AChordDoesNotShieldTheLargerArc()
        {
            var points = _board.BindPointsOf(Player.A);
            var chord = Draw(Player.A, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(0, 1));
            int before = chord.Durability;

            // Mirror the chord's arc across the line of fire: the same spot on the other side of the front bind point.
            Vec2 middle = (points[0] + points[1]) / 2f;
            Vec2 mirrored = V(6f, 10f) + (V(middle.X, 20f - middle.Y) - V(6f, 10f)).Normalized * 3f;
            Draw(Player.B, DrawingSpec.Vigor(VigorTier.Medium), Placement.Aimed(V(20f, mirrored.Y), mirrored));
            for (int i = 0; i < 4; i++) PassTurn();

            Assert.AreEqual(before, chord.Durability);
            Assert.AreEqual(10, MainWard(Player.A).Durability);
        }

        [Test]
        public void TheMainWardCanBePlacedAndShapedOnItsOwnHalf()
        {
            var config = new RulesConfig();
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);

            var ellipse = DrawingSpec.Warding(2);
            Assert.IsNotNull(board.ValidatePlacement(Player.A, ellipse, Placement.MainWard(V(14f, 10f), 2f)), "crosses the centre line");
            Assert.IsNotNull(board.ValidatePlacement(Player.A, ellipse, Placement.MainWard(V(6f, 1f), 2f)), "off the board");
            Assert.IsNotNull(board.ValidatePlacement(Player.A, ellipse, Placement.MainWard(V(6f, 10f), 0.5f)), "too flat");
            Assert.IsNull(board.ValidatePlacement(Player.A, DrawingSpec.Warding(4), Placement.MainWard(V(4f, 15f))));

            var placement = Placement.MainWard(V(8f, 6f), 2f);
            Assert.IsNull(board.ValidatePlacement(Player.A, ellipse, placement));
            board.SetPendingPlacement(Player.A, ellipse, placement);
            duel.ResolveTurn(PlayerAction.Start(ellipse), PlayerAction.Start(DrawingSpec.Warding(2)));

            var main = board.MainWardOf(Player.A);
            Assert.AreEqual(8f, main.Shape.Centre.X, 1e-4);
            Assert.AreEqual(6f, main.Shape.Centre.Y, 1e-4);
            Assert.AreEqual(2f, main.Shape.B, 1e-4);
            Assert.AreEqual(2, main.BindPoints.Count);
        }

        [Test]
        public void PlacementsAreCheckedAgainstTheSpecAndTheBoard()
        {
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Forbiddance(1), Placement.BetweenBindPoints(0, 1)));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(0, 0)));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(20f, 10f), V(26f, 10f))));
            // 3 units from B's main LoW: one travel turn.
            Assert.IsNull(_board.ValidatePlacement(Player.B, DrawingSpec.Vigor(VigorTier.Low).WithTravel(1), Placement.Aimed(V(20f, 10f), V(6f, 10f))));
            Assert.IsNotNull(_board.ValidatePlacement(Player.B, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(20f, 10f), V(6f, 10f))));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Vigor(VigorTier.Low), null));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(V(10f, 2f), V(10f, 12f))), "too long");
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Making(), Placement.AtBindPoint(0)));
            Assert.IsNull(_board.ValidatePlacement(Player.A, DrawingSpec.Making(boundBindPoints: 1), Placement.AtBindPoint(0)));
        }
    }
}
