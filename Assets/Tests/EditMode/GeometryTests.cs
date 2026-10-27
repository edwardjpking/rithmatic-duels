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
            _config.CentreWall.Enabled = false; // the centre LoF has its own tests
            _config.Warding.MainWardDurabilityMultiplier = 1f; // so is the main LoW's double durability
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
        /// Travel turns and LoF length are worked out from the placement, as the UI does.
        /// </summary>
        private Line Draw(Player player, DrawingSpec spec, Placement placement, DrawingPace pace = DrawingPace.Normal)
        {
            spec = _board.Measure(player, spec, placement);
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
            // High tier: speed 7, damage 3. A line finished at normal pace joins the board after movement
            // (DESIGN.md section 3), so it first flies on the next tick, and reaches B's ward front at x=23 on the one after.
            var vigor = Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            var body = (VigorBody)_board.BodyOf(vigor.Id);
            Assert.AreEqual(10f, body.Position.X, 1e-3);

            PassTurn();
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
        public void ARushedVigorFliesTheTickItIsFinished()
        {
            // DESIGN.md section 3: rushed lines join the board before animated lines act,
            // here widened to drawings of up to 3 normal-pace turns.
            _config.RushedEarlyMaxNormalTurns = 3;
            var vigor = Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)), DrawingPace.Rush);
            Assert.AreEqual(17f, ((VigorBody)_board.BodyOf(vigor.Id)).Position.X, 1e-3);
        }

        [Test]
        public void OnlyRushedOneTurnDrawingsFinishBeforeMovement()
        {
            // By default only a drawing that takes 1 turn at normal pace joins at stage 2.
            var high = Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)), DrawingPace.Rush);
            Assert.AreEqual(10f, ((VigorBody)_board.BodyOf(high.Id)).Position.X, 1e-3, "a rushed High LoV waits until stage 4");
        }

        [Test]
        public void ARushedOneTurnVigorFliesStraightAway()
        {
            // Travel turns count: a Low LoV drawn far from the main LoW takes 2 turns, so it would wait.
            // Close to the main LoW, so no travel turns: 1 turn at normal pace.
            var low = Draw(Player.A, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(10f, 10f), V(26f, 10f)), DrawingPace.Rush);
            Assert.AreEqual(13f, ((VigorBody)_board.BodyOf(low.Id)).Position.X, 1e-3, "a rushed Low LoV flies straight away");
        }

        [Test]
        public void LinesCanBeSetToFinishBeforeMovement()
        {
            // The swappable order (stages 3 and 4) is a config switch.
            _config.LinesFinishBeforeMovement = true;
            var vigor = Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            Assert.AreEqual(17f, ((VigorBody)_board.BodyOf(vigor.Id)).Position.X, 1e-3);
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
        public void ADrawingInProgressShowsWhereItWillGoButIsNotOnTheBoard()
        {
            var spec = DrawingSpec.Forbiddance(0);
            var placement = Placement.Segment(V(20f, 6f), V(20f, 14f));
            spec = _board.Measure(Player.B, spec, placement);
            _board.SetPendingPlacement(Player.B, spec, placement);
            Assert.IsNull(_board.WorkInProgressOf(Player.B), "a choice doesn't show before its turn resolves");

            _duel.ResolveTurn(Pass, PlayerAction.Start(spec));
            Assert.IsTrue(_duel.GetPlayer(Player.B).HasWorkInProgress, "this LoF takes more than one turn");
            var sketch = _board.WorkInProgressOf(Player.B);
            Assert.AreEqual(LineType.Forbiddance, sketch.Type);
            Assert.AreEqual(20f, sketch.Segment.Value.A.X, 1e-4);
            Assert.AreEqual(6f, sketch.Segment.Value.A.Y, 1e-4);
            Assert.AreEqual(14f, sketch.Segment.Value.B.Y, 1e-4);
            Assert.IsFalse(_board.Bodies.OfType<ForbiddanceBody>().Any(), "nothing is on the board until it is complete");
            Assert.IsNull(_board.WorkInProgressOf(Player.A));

            while (_duel.GetPlayer(Player.B).HasWorkInProgress) _duel.ResolveTurn(Pass, Continue);
            Assert.IsNull(_board.WorkInProgressOf(Player.B));
            Assert.AreEqual(14f, _board.Bodies.OfType<ForbiddanceBody>().Single().Segment.B.Y, 1e-4);
        }

        [Test]
        public void CancellingADrawingClearsItsSketch()
        {
            var spec = DrawingSpec.Forbiddance(0);
            var placement = Placement.Segment(V(20f, 6f), V(20f, 14f));
            spec = _board.Measure(Player.B, spec, placement);
            _board.SetPendingPlacement(Player.B, spec, placement);
            _duel.ResolveTurn(Pass, PlayerAction.Start(spec));
            Assert.IsNotNull(_board.WorkInProgressOf(Player.B));

            _duel.ResolveTurn(Pass, PlayerAction.Cancel());
            Assert.IsNull(_board.WorkInProgressOf(Player.B));
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
        public void AnAttachedWardWithTwoBindPointsIsAnEllipseStickingOut()
        {
            var ward = Draw(Player.A, DrawingSpec.Warding(2), Placement.AtBindPoint(0, 1f));
            var body = (WardBody)_board.BodyOf(ward.Id);
            float semiMajor = _config.Geometry.AttachedEllipseSemiMajor;
            Assert.AreEqual(semiMajor, body.Shape.A, 1e-4);
            Assert.AreEqual(1f, body.Shape.B, 1e-4);
            Assert.AreEqual(9f + semiMajor, body.Shape.Centre.X, 1e-3); // front bind point at x=9, long axis toward B
            Assert.AreEqual(2, body.BindPoints.Count);
            Assert.AreEqual(9f, Math.Min(body.BindPoints[0].X, body.BindPoints[1].X), 1e-3);
        }

        [Test]
        public void AnAttachedCircleCanBeSized()
        {
            var ward = Draw(Player.A, DrawingSpec.Warding(4), Placement.AtBindPoint(0, 2f));
            var body = (WardBody)_board.BodyOf(ward.Id);
            Assert.AreEqual(2f, body.Shape.A, 1e-4);
            Assert.AreEqual(11f, body.Shape.Centre.X, 1e-3);
        }

        [Test]
        public void AttachedWardSizesStayInTheirRange()
        {
            var geometry = _config.Geometry;
            var spec = DrawingSpec.Warding(4);
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, spec, Placement.AtBindPoint(0, geometry.AttachedWardMaxRadius + 1f)));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Warding(2), Placement.AtBindPoint(0, geometry.AttachedEllipseMinSemiMinor / 2f)));
            var placement = Placement.AtBindPoint(0, geometry.AttachedWardMinRadius);
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, spec, placement), placement));
        }

        [Test]
        public void TheMainWardAlwaysGoesHome()
        {
            // Setup in SetUp asked for nothing in particular; a placement elsewhere is ignored too.
            var config = new RulesConfig();
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);
            var circle = DrawingSpec.Warding(4);
            board.SetPendingPlacement(Player.A, circle, Placement.MainWard(V(12f, 6f)));
            duel.ResolveTurn(PlayerAction.Start(circle), PlayerAction.Start(circle));
            Assert.AreEqual(board.Home(Player.A).X, board.MainWardOf(Player.A).Shape.Centre.X, 1e-4);
            Assert.AreEqual(board.Home(Player.A).Y, board.MainWardOf(Player.A).Shape.Centre.Y, 1e-4);
        }

        [Test]
        public void NothingCanBeDrawnReachingIntoTheOpponentsHalf()
        {
            // A free LoF from x=14 toward B, 3 long, would end at x=17.
            var free = Placement.Segment(V(14f, 4f), V(17f, 4f));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), free), free));
            // The longest it can be stops short of the centre line, which is drawn as wide as a LoF.
            float half = _config.Geometry.ForbiddanceWidth / 2f;
            Assert.AreEqual(2f - half, _board.MaxForbiddanceLength(Player.A, Placement.Segment(V(14f, 4f), V(14f, 4f)), V(1f, 0f)), 1e-3);
        }

        [Test]
        public void AttachedWardsMustStayOnTheirHalf()
        {
            _config.Geometry.AttachedWardMaxRadius = 5f;
            var spec = DrawingSpec.Warding(4);
            // Front bind point at x=9: a circle there of radius r reaches x = 9 + 2r, so 3.5 is the most that fits.
            Assert.AreEqual(3.5f, _board.MaxAttachedSize(Player.A, spec, 0), 1e-2);
            var tooBig = Placement.AtBindPoint(0, 4f);
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, spec, tooBig), tooBig));
        }

        [Test]
        public void EachBindPointTakesOneMoreWard()
        {
            Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(0));
            var again = Placement.AtBindPoint(0);
            var spec = DrawingSpec.Warding(4);
            Assert.That(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, spec, again), again), Does.Contain("already"));
            var other = Placement.AtBindPoint(1);
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, spec, other), other));
        }

        [Test]
        public void AMarksCrossIsBracedBetweenItsBindPoints()
        {
            var cross = Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(0));
            var body = (WardBody)_board.BodyOf(cross.Id);
            Assert.AreEqual(2, body.Braces.Count);
            foreach (var brace in body.Braces)
            {
                Assert.IsTrue(body.BindPoints.Any(p => Vec2.Distance(p, brace.A) < 1e-4f));
                Assert.IsTrue(body.BindPoints.Any(p => Vec2.Distance(p, brace.B) < 1e-4f));
                Assert.AreEqual(2f * _config.Warding.MarksCrossRadius, brace.Length, 1e-3);
            }
        }

        [Test]
        public void BiggerAttachedWardsTakeLonger()
        {
            var small = _board.Measure(Player.A, DrawingSpec.Warding(4), Placement.AtBindPoint(0, 1f));
            var normal = _board.Measure(Player.A, DrawingSpec.Warding(4), Placement.AtBindPoint(0, 1.5f));
            var big = _board.Measure(Player.A, DrawingSpec.Warding(4), Placement.AtBindPoint(0, 2.5f));
            Assert.AreEqual(1, DrawTimes.BaseTurns(small, _config));
            Assert.AreEqual(2, DrawTimes.BaseTurns(normal, _config), "the default size keeps bind points div 2");
            Assert.AreEqual(3, DrawTimes.BaseTurns(big, _config));
        }

        [Test]
        public void TheCentreWallReflectsVigorForItsTurns()
        {
            var config = new RulesConfig { MainWardFirst = false, RushedEarlyMaxNormalTurns = 3 };
            config.CentreWall.Turns = 4;
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);
            Assert.IsTrue(board.CentreWallUp);

            // A rushed High LoV from x=14 toward B: it reaches the wall at x=16 straight away and bounces back.
            var spec = DrawingSpec.Vigor(VigorTier.High);
            var aim = Placement.Aimed(V(14f, 2f), V(26f, 2f));
            board.SetPendingPlacement(Player.A, spec, aim);
            duel.ResolveTurn(PlayerAction.Start(spec, DrawingPace.Rush), Pass);
            duel.ResolveTurn(Continue, Pass);
            var vigor = duel.Lines.First(l => l.Type == LineType.Vigor);
            var body = (VigorBody)board.BodyOf(vigor.Id);
            Assert.Less(body.Direction.X, 0f, "reflected back toward A");
            Assert.That(board.LastTickNotes, Has.Some.Contains("centre"));

            for (int i = 0; i < 2; i++) duel.ResolveTurn(Pass, Pass);
            Assert.IsFalse(board.CentreWallUp, "gone from turn 5");
        }

        [Test]
        public void BindPointsOfOtherWardsCanBeUsed()
        {
            // A Mark's Cross at the front bind point (x=9) adds its other three bind points to the list;
            // the one where it touches the main LoW is shared and listed once.
            int before = _board.BindPointsOf(Player.A).Count;
            var cross = Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(0));
            var points = _board.BindPointsOf(Player.A);
            Assert.AreEqual(before + 3, points.Count);

            // Its front bind point at x=11 takes a tangent LoF and another LoW.
            int front = Enumerable.Range(0, points.Count).Single(i => Math.Abs(points[i].X - 11f) < 1e-3f);
            var tangent = Draw(Player.A, DrawingSpec.Forbiddance(1), Placement.TangentAt(front, 2f));
            var segment = ((ForbiddanceBody)_board.BodyOf(tangent.Id)).Segment;
            Assert.AreEqual(11f, segment.A.X, 1e-3);
            Assert.AreEqual(11f, segment.B.X, 1e-3);

            var ward = Draw(Player.A, DrawingSpec.Warding(4), Placement.AtBindPoint(front, 1f));
            Assert.AreEqual(12f, ((WardBody)_board.BodyOf(ward.Id)).Shape.Centre.X, 1e-3);
            Assert.IsTrue(_board.BindPointFull(Player.A, front));
            Assert.IsTrue(_board.BindPointFull(Player.A, 0), "the main LoW's front bind point has its Mark's Cross");
        }

        [Test]
        public void AChordOfAnyWardShieldsIt()
        {
            var cross = Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(0));
            var points = _board.BindPointsOf(Player.A);
            int top = Enumerable.Range(0, points.Count).Single(i => Math.Abs(points[i].X - 10f) < 1e-3f && points[i].Y > 10.5f);
            int bottom = Enumerable.Range(0, points.Count).Single(i => Math.Abs(points[i].X - 10f) < 1e-3f && points[i].Y < 9.5f);
            var chord = Draw(Player.A, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(top, bottom));
            Assert.AreEqual(cross.Id, ((ForbiddanceBody)_board.BodyOf(chord.Id)).ChordOf);
        }

        [Test]
        public void AForbiddanceIsARectangleThatMustStayOnItsHalf()
        {
            float half = _config.Geometry.ForbiddanceWidth / 2f;
            // Upright at x=16 - 2 half: its edge just touches the edge of the centre line (drawn as wide as a LoF).
            var touching = Placement.Segment(V(16f - 2f * half, 4f), V(16f - 2f * half, 7f));
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), touching), touching));
            var over = Placement.Segment(V(16f - 1.5f * half, 4f), V(16f - 1.5f * half, 7f));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), over), over));

            // Slanting toward the line from x=14: capped where its corner first reaches the line's edge.
            Vec2 direction = new Vec2(1f, 1f).Normalized;
            float max = _board.MaxForbiddanceLength(Player.A, Placement.Segment(V(14f, 4f), V(14f, 4f)), direction);
            Vec2 end = V(14f, 4f) + direction * max;
            float cornerX = end.X + Math.Abs(direction.Perp.X) * half;
            Assert.AreEqual(16f - half, cornerX, 1e-3);
        }

        [Test]
        public void ATangentLoFStopsShortOfANeighbouringWard()
        {
            // A radius 1.5 circle at A's front bind point (9, 10): centre (10.5, 10), top at (10.5, 11.5).
            Draw(Player.A, DrawingSpec.Warding(4), Placement.AtBindPoint(0, 1.5f));
            var points = _board.BindPointsOf(Player.A);
            int top = 0;
            for (int i = 1; i < points.Count; i++) if (Vec2.Distance(points[i], V(10.5f, 11.5f)) < Vec2.Distance(points[top], V(10.5f, 11.5f))) top = i;
            Assert.AreEqual(0f, Vec2.Distance(points[top], V(10.5f, 11.5f)), 1e-2, "the small circle has a bind point at its top");

            // Along y = 11.5 the main LoW (centre (6, 10), radius 3) reaches x = 6 + sqrt(6.75), about 8.6,
            // so a tangent centred at x = 10.5 must be shorter than about 3.8.
            float max = _board.MaxForbiddanceLength(Player.A, Placement.TangentAt(top), Vec2.Zero);
            Assert.Less(max, 3.85f);
            var tooLong = Placement.TangentAt(top, 4.5f);
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(1), tooLong), tooLong));
            var capped = Placement.TangentAt(top, max);
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(1), capped), capped));
        }

        [Test]
        public void LowSizesStepThroughWholeNumberDurabilities()
        {
            var sizes = _board.MainEllipseSizes(Player.A);
            var values = sizes.Select(size => _board.WeakestDurability(WardShape.Ellipse(V(6f, 10f), _config.Geometry.EllipseSemiMajor, size, 0f))).ToList();
            CollectionAssert.AllItemsAreUnique(values);
            for (int i = 1; i < values.Count; i++) Assert.AreEqual(values[i - 1] + 1, values[i], "one step is one point of durability");
            Assert.Greater(values.Count, 2);

            var circles = _board.AttachedSizes(Player.A, DrawingSpec.Warding(4), 0);
            var circleValues = circles.Select(r => _board.WeakestDurability(WardShape.Circle(V(0f, 0f), r))).ToList();
            CollectionAssert.AllItemsAreUnique(circleValues);
        }

        [Test]
        public void ForbiddanceCannotCrossAWard()
        {
            // Through the main LoW (centre (6, 10), radius 3), from outside to inside.
            var through = Placement.Segment(V(1f, 10f), V(7f, 10f));
            Assert.That(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), through), through), Does.Contain("overlap"));
            // Wholly inside, or wholly outside, is fine; so is a tangent.
            var inside = Placement.Segment(V(5f, 9f), V(7f, 9f));
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), inside), inside));
            var tangent = Placement.TangentAt(0, 4f);
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(1), tangent), tangent));
            // A free line touching the circle from outside, along its top.
            var along = Placement.Segment(V(4f, 13f), V(8f, 13f));
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), along), along));
        }

        [Test]
        public void AFreeLoFKeepsClearOfALoWAsDrawn()
        {
            // Just outside the main LoW (centre (6, 10), radius 3), across its outline at 45 degrees, away from any bind point.
            Vec2 normal = new Vec2(1f, 1f).Normalized;
            Vec2 along = normal.Perp;
            Placement At(float distance)
            {
                Vec2 middle = V(6f, 10f) + normal * distance;
                return Placement.Segment(middle - along, middle + along);
            }
            // The LoF is drawn 0.07 either side and the main LoW 0.06, so a gap of 0.08 would overlap them; 0.2 is clear.
            var close = At(3.08f);
            Assert.That(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), close), close), Does.Contain("overlap"));
            var clear = At(3.2f);
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Forbiddance(0), clear), clear));
        }

        [Test]
        public void ALoWKeepsClearOfALoFAsDrawn()
        {
            Draw(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(V(12.6f, 8f), V(12.6f, 12f)));
            var points = _board.BindPointsOf(Player.A);
            int front = Enumerable.Range(0, points.Count).Single(i => points[i].X > 8.9f);
            // A circle on the front bind point (9, 10) reaches x = 9 + 2r: 12.5 would leave too small a gap to the LoF.
            var big = Placement.AtBindPoint(front, 1.75f);
            Assert.That(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Warding(4), big), big), Does.Contain("overlap"));
            var small = Placement.AtBindPoint(front, 1.5f);
            Assert.IsNull(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Warding(4), small), small));
        }

        [Test]
        public void WardsCannotOverlap()
        {
            // A Mark's Cross on top (bind point facing up) and then a big circle on its side bind point would
            // curl back into the main LoW.
            var points = _board.BindPointsOf(Player.A);
            int top = Enumerable.Range(0, points.Count).Single(i => points[i].Y > 12.9f);
            Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(top));
            points = _board.BindPointsOf(Player.A);
            int side = Enumerable.Range(0, points.Count).Single(i => Math.Abs(points[i].Y - 14f) < 1e-3f && points[i].X > 6.5f);
            var big = Placement.AtBindPoint(side, 2.5f);
            Assert.That(_board.ValidatePlacement(Player.A, _board.Measure(Player.A, DrawingSpec.Warding(4), big), big), Does.Contain("overlap"));
            Assert.Less(_board.MaxAttachedSize(Player.A, DrawingSpec.Warding(4), side), 2.5f);
        }

        [Test]
        public void VigorOnVigorUsesTheTierRules()
        {
            _board.SetPendingPlacement(Player.A, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(14f, 10f), V(26f, 10f)));
            _board.SetPendingPlacement(Player.B, DrawingSpec.Vigor(VigorTier.Low), Placement.Aimed(V(18f, 10f), V(6f, 10f)));
            var low = PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low));
            _duel.ResolveTurn(low, low); // both complete, and join the board after movement
            PassTurn(); // fly 3 each and meet in the middle

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
        public void HoveringAnywhereOnAFlyingVigorFindsIt()
        {
            // The LoV is drawn as a wave trailing behind its head, so hovering its tail finds it too.
            var vigor = Draw(Player.A, DrawingSpec.Vigor(VigorTier.High), Placement.Aimed(V(10f, 10f), V(26f, 10f)));
            var body = (VigorBody)_board.BodyOf(vigor.Id);
            Vec2 tail = body.Position - body.Direction * (_config.Geometry.VigorDrawnLength * 0.9f);
            Assert.AreSame(body, _board.BodyAt(tail, 0.3f));
            Assert.AreEqual(2, _board.ReflectDamage(vigor.Damage), "half of 3, rounded up");
        }

        [Test]
        public void HoveringNearSeveralLinesPicksByType()
        {
            // Order: LoM, LoV, LoW, LoF, whichever is nearest.
            var chalkling = Draw(Player.A, DrawingSpec.Making(glyph: Glyph.Defend, boundBindPoints: 1), Placement.AtBindPoint(0));
            var body = (ChalklingBody)_board.BodyOf(chalkling.Id);
            Vec2 onOutline = body.Tether.Value;
            Assert.AreSame(body, _board.BodyAt(onOutline, 2f), "the chalkling beats the nearer LoW");

            var lof = Draw(Player.A, DrawingSpec.Forbiddance(1), Placement.TangentAt(1));
            var segment = ((ForbiddanceBody)_board.BodyOf(lof.Id)).Segment;
            Vec2 touching = (segment.A + segment.B) / 2f;
            Assert.AreEqual(MainWard(Player.A).Id, _board.BodyAt(touching, 0.3f).LineId, "the LoW beats a LoF touching it");
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
            Assert.Less(ellipse.Durability, duel.GetPlayer(Player.B).MainWard.Durability, "weaker than B's circle");
            Assert.AreEqual(2, board.BindPointsOf(Player.A).Count);
        }

        [Test]
        public void TheMainWardHasDoubleDurability()
        {
            // With the real config: a default circle (radius 3) is 12 at every point, 24 as the main LoW.
            var config = new RulesConfig();
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(4)), PlayerAction.Start(DrawingSpec.Warding(4)));
            var main = duel.GetPlayer(Player.A).MainWard;
            var shape = board.MainWardOf(Player.A).Shape;
            Assert.AreEqual(12, board.WeakestDurability(shape), "any other LoW this shape");
            Assert.AreEqual(24, board.WeakestDurability(shape, true));
            Assert.AreEqual(24, main.Durability);
            Assert.AreEqual(24, main.MaxDurability);
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
        public void AChordForbiddanceToughensTheSmallerArc()
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

            Assert.AreEqual(11, MainWard(Player.A).Durability, "the arc takes half the LoV's 2 damage");
            Assert.AreEqual(before - 1, chord.Durability, "the chord takes the same halved damage");
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
        public void SetupBindPointsAppearOnTheBoardOnlyWhenDrawn()
        {
            var config = new RulesConfig();
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), board);
            board.SetPendingPlacement(Player.A, DrawingSpec.Warding(9), Placement.MainWard(V(8f, 10f)));
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(9)), PlayerAction.Start(DrawingSpec.Warding(2)));

            Assert.IsNotNull(board.MainWardOf(Player.A), "the outline is on the board at once");
            Assert.AreEqual(0, board.BindPointsOf(Player.A).Count);
            Assert.AreEqual(2, board.BindPointsOf(Player.B).Count);
            CollectionAssert.AreEqual(new[] { 4, 6, 9 }, board.BindPointChoices(Player.A));
            CollectionAssert.AreEqual(new[] { 2 }, board.BindPointChoices(Player.B));
            Assert.AreEqual(9, board.PreviewBindPoints(Player.A, 9).Count);

            while (duel.GetPlayer(Player.A).HasWorkInProgress) duel.ResolveTurn(PlayerAction.Continue(), PlayerAction.Pass());
            Assert.AreEqual(9, board.BindPointsOf(Player.A).Count, "the board catches up in the tick they complete");
        }

        [Test]
        public void TheMainWardSnapsInsideItsHalfAndToPresets()
        {
            var board = new ChalkBoard(new RulesConfig());
            var circle = DrawingSpec.Warding(4);

            // Dragged past the centre line and off the top: pushed back so the whole circle fits.
            Vec2 clamped = board.ClampMainWard(Player.A, circle, V(30f, 25f), 0f);
            Assert.AreEqual(13f, clamped.X, 1e-2);
            Assert.AreEqual(17f, clamped.Y, 1e-2);
            Assert.IsNull(board.ValidatePlacement(Player.A, circle, Placement.MainWard(clamped)));

            var presets = board.MainWardPresets(Player.B, circle, 0f);
            CollectionAssert.IsNotEmpty(presets);
            foreach (var preset in presets) Assert.IsNull(board.ValidatePlacement(Player.B, circle, Placement.MainWard(preset)));
            Assert.AreEqual(24f, board.SnapMainWard(Player.B, circle, V(24.3f, 10.4f), 0f).X, 1e-3, "snaps to the centre of B's half");
            Assert.AreEqual(10f, board.SnapMainWard(Player.A, circle, V(11f, 10.4f), 0f).Y, 1e-3, "snaps to the midline");
            Assert.AreEqual(14f, board.SnapMainWard(Player.A, circle, V(11f, 14f), 0f).Y, 1e-3, "free away from the midline");
        }

        [Test]
        public void TheMainWardCanBePlacedAndShapedOnItsOwnHalf()
        {
            var config = new RulesConfig();
            config.Geometry.MainWardAtHome = false;
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
        public void MeasureFillsInTheLengthOfAForbiddance()
        {
            var points = _board.BindPointsOf(Player.A);
            var chord = _board.Measure(Player.A, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(0, 1));
            Assert.AreEqual(Vec2.Distance(points[0], points[1]), chord.Length, 1e-4);
            var free = _board.Measure(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(V(10f, 4f), V(10f, 10f)));
            Assert.AreEqual(6f, free.Length, 1e-4);
            Assert.AreEqual(3, DrawTimes.BaseTurns(free, _config));
            Assert.IsNotNull(_board.ValidatePlacement(Player.A, DrawingSpec.Forbiddance(0).WithTravel(free.TravelTurns),
                Placement.Segment(V(10f, 4f), V(10f, 10f))), "a spec without the length is rejected");
        }

        [Test]
        public void AMarksCrossHasItsCrossedForbiddanceInside()
        {
            var cross = Draw(Player.A, DrawingSpec.MarksCross(_config), Placement.AtBindPoint(0));
            var body = (WardBody)_board.BodyOf(cross.Id);
            Assert.AreEqual(2, body.Braces.Count);
            foreach (var brace in body.Braces)
            {
                Assert.AreEqual(2f * _config.Warding.MarksCrossRadius, brace.Length, 1e-3);
                Assert.AreEqual(0f, Vec2.Distance((brace.A + brace.B) / 2f, body.Shape.Centre), 1e-4);
            }
            Assert.AreEqual(0, ((WardBody)_board.BodyOf(MainWard(Player.A).Id)).Braces.Count);
        }

        [Test]
        public void ClickingNearALineFindsIt()
        {
            var lof = Draw(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(V(12f, 6f), V(12f, 12f)));
            Assert.AreEqual(lof.Id, _board.ForbiddanceAt(Player.A, V(12.2f, 9f), 0.5f).LineId);
            Assert.IsNull(_board.ForbiddanceAt(Player.B, V(12.2f, 9f), 0.5f), "only your own");
            Assert.IsNull(_board.ForbiddanceAt(Player.A, V(13f, 9f), 0.5f));
            Assert.AreEqual(MainWard(Player.A).Id, _board.BodyAt(V(9.1f, 10f), 0.3f).LineId);
            Assert.AreEqual(lof.Id, _board.BodyAt(V(12f, 7f), 0.3f).LineId);
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
